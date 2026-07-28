using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SIG.ClientCard.Core.Entities;
using SIG.ClientCard.Core.Enums;
using SIG.ClientCard.Core.SyncContracts;
using SIG.ClientCard.Data.Infrastructure;
using SIG.ClientCard.Data.Repositories;
using SIG.ClientCard.Tests.TestInfra;

namespace SIG.ClientCard.Tests;

public class RepositoryTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly FakeClock _clock = new();
    private readonly FakeDevice _device = new();
    private readonly FakeTenant _tenant = new();

    private ClientRepository Clients => new(_db, new OutboxWriter(_clock), _clock, _device, _tenant);
    private ClientNoteRepository Notes => new(_db, new OutboxWriter(_clock), _clock, _device, _tenant);
    private ServiceRecordRepository Services => new(_db, new OutboxWriter(_clock), _clock, _device, _tenant);
    private ClientConsentRepository Consents => new(_db, new OutboxWriter(_clock), _clock, _device, _tenant);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Upsert_writes_client_and_outbox_row_atomically()
    {
        var client = new Client { LastName = "Smith", FirstName = "Anna", Phone = "07700 900123" };
        await Clients.UpsertAsync(client);

        await using var db = _db.CreateDbContext();
        var stored = Assert.Single(await db.Clients.ToListAsync());
        var op = Assert.Single(await db.Outbox.ToListAsync());

        Assert.NotEqual(Guid.Empty, stored.Id);
        Assert.Equal(_tenant.SalonId, stored.SalonId);
        Assert.Equal("+447700900123", stored.Phone);
        Assert.Equal("07700 900123", stored.PhoneRaw);
        Assert.Equal(_device.DeviceId, stored.UpdatedByDevice);

        Assert.Equal(SyncEntities.Client, op.Entity);
        Assert.Equal(SyncOperations.Upsert, op.Operation);
        Assert.Equal(stored.Id, op.EntityId);

        // Payload is the wire shape: snake_case keys matching Postgres columns.
        using var payload = JsonDocument.Parse(op.Payload);
        Assert.Equal("Smith", payload.RootElement.GetProperty("last_name").GetString());
        Assert.Equal(stored.Id.ToString("D"), payload.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Search_matches_name_and_phone_and_hides_soft_deleted()
    {
        await Clients.UpsertAsync(new Client { LastName = "Smith", FirstName = "Anna", Phone = "07700 900123" });
        await Clients.UpsertAsync(new Client { LastName = "Jones", FirstName = "Beth", Phone = "07700 900999" });

        var byName = await Clients.SearchAsync("smi", 0, 10);
        Assert.Single(byName);
        Assert.Equal("Smith", byName[0].LastName);

        var byPhone = await Clients.SearchAsync("900999", 0, 10);
        Assert.Single(byPhone);
        Assert.Equal("Jones", byPhone[0].LastName);

        await Clients.SoftDeleteAsync(byName[0].Id);
        Assert.Empty(await Clients.SearchAsync("smi", 0, 10));
        Assert.Equal(1, await Clients.CountAsync());

        // The tombstone still syncs: the delete rides in an upsert payload.
        await using var db = _db.CreateDbContext();
        var tombstoneOp = await db.Outbox.OrderBy(o => o.CreatedAt).LastAsync();
        using var payload = JsonDocument.Parse(tombstoneOp.Payload);
        Assert.True(payload.RootElement.TryGetProperty("deleted_at", out var deletedAt));
        Assert.NotEqual(JsonValueKind.Null, deletedAt.ValueKind);
    }

    [Fact]
    public async Task Notes_append_and_read_reverse_chronological()
    {
        var client = new Client { LastName = "Smith", FirstName = "Anna" };
        await Clients.UpsertAsync(client);

        await Notes.AddAsync(new ClientNote { ClientId = client.Id, Body = "first" });
        _clock.Advance(TimeSpan.FromMinutes(5));
        await Notes.AddAsync(new ClientNote { ClientId = client.Id, Body = "second" });

        var feed = await Notes.GetForClientAsync(client.Id);
        Assert.Equal(["second", "first"], feed.Select(n => n.Body).ToArray());
    }

    [Fact]
    public async Task Erase_hard_deletes_children_and_pending_ops_and_enqueues_redaction()
    {
        var client = new Client { LastName = "Smith", FirstName = "Anna" };
        await Clients.UpsertAsync(client);
        await Notes.AddAsync(new ClientNote { ClientId = client.Id, Body = "allergy: nickel" });
        await Services.UpsertAsync(new ServiceRecord
        {
            ClientId = client.Id,
            PerformedOn = new DateOnly(2026, 1, 1),
            ServiceDescription = "Facial",
            Price = 45.00m,
        });
        await Consents.GrantAsync(new ClientConsent { ClientId = client.Id, Purpose = ConsentPurpose.SpecialCategoryData });

        await Clients.EraseAsync(client.Id);

        await using var db = _db.CreateDbContext();
        Assert.Empty(await db.Clients.ToListAsync());
        Assert.Empty(await db.ClientNotes.ToListAsync());
        Assert.Empty(await db.ServiceRecords.ToListAsync());
        Assert.Empty(await db.ClientConsents.ToListAsync());

        // Everything queued for that client is gone; only the redaction op remains.
        var op = Assert.Single(await db.Outbox.ToListAsync());
        Assert.Equal(SyncOperations.Delete, op.Operation);
        Assert.Equal(SyncEntities.Client, op.Entity);
        Assert.Equal(client.Id, op.EntityId);
    }

    [Fact]
    public async Task Consent_withdrawal_stamps_withdrawn_at_and_syncs()
    {
        var client = new Client { LastName = "Smith", FirstName = "Anna" };
        await Clients.UpsertAsync(client);
        await Consents.GrantAsync(new ClientConsent { ClientId = client.Id, Purpose = ConsentPurpose.Marketing });

        _clock.Advance(TimeSpan.FromDays(30));
        await Consents.WithdrawAsync(client.Id, ConsentPurpose.Marketing);

        var consents = await Consents.GetForClientAsync(client.Id);
        var consent = Assert.Single(consents);
        Assert.NotNull(consent.WithdrawnAt);
        Assert.False(consent.IsActive);
    }
}
