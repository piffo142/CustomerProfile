using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SIG.ClientCard.Core.Entities;
using SIG.ClientCard.Data.Infrastructure;
using SIG.ClientCard.Data.Repositories;
using SIG.ClientCard.Tests.TestInfra;

namespace SIG.ClientCard.Tests;

public class TenantMigratorTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly FakeClock _clock = new();
    private readonly FakeDevice _device = new();
    private readonly FakeTenant _tenant = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Adoption_restamps_rows_and_queued_outbox_payloads()
    {
        // Phase 0 data written under the placeholder salon id, with ops queued.
        var clients = new ClientRepository(_db, new OutboxWriter(_clock), _clock, _device, _tenant);
        var notes = new ClientNoteRepository(_db, new OutboxWriter(_clock), _clock, _device, _tenant);
        var client = new Client { LastName = "Smith", FirstName = "Anna" };
        await clients.UpsertAsync(client);
        await notes.AddAsync(new ClientNote { ClientId = client.Id, Body = "note" });

        var realSalonId = Guid.Parse("cccccccc-0000-0000-0000-000000000009");
        await new TenantMigrator(_db).AdoptSalonAsync(realSalonId);

        await using var db = _db.CreateDbContext();
        Assert.Equal(realSalonId, (await db.Clients.SingleAsync()).SalonId);
        Assert.Equal(realSalonId, (await db.ClientNotes.SingleAsync()).SalonId);

        // Queued payloads must carry the real tenant id or RLS with-check
        // rejects the first push.
        foreach (var op in await db.Outbox.ToListAsync())
        {
            using var payload = JsonDocument.Parse(op.Payload);
            Assert.Equal(
                realSalonId.ToString("D"),
                payload.RootElement.GetProperty("salon_id").GetString());
        }
    }
}
