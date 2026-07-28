using Microsoft.EntityFrameworkCore;
using SIG.ClientCard.Core.Entities;
using SIG.ClientCard.Core.Enums;
using SIG.ClientCard.Core.SyncContracts;
using SIG.ClientCard.Data.Infrastructure;
using SIG.ClientCard.Data.Repositories;
using SIG.ClientCard.Sync;
using SIG.ClientCard.Tests.TestInfra;

namespace SIG.ClientCard.Tests;

public class SyncEngineTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly FakeClock _clock = new();
    private readonly FakeDevice _device = new();
    private readonly FakeTenant _tenant = new();
    private readonly FakeTransport _transport = new();
    private readonly SyncOptions _options = new() { PullLimit = 3 };

    private SyncEngine Engine => new(_db, _transport, _clock, _options);
    private ClientRepository Clients => new(_db, new OutboxWriter(_clock), _clock, _device, _tenant);
    private ClientNoteRepository Notes => new(_db, new OutboxWriter(_clock), _clock, _device, _tenant);

    public void Dispose() => _db.Dispose();

    // ---------------------------------------------------------------- push

    [Fact]
    public async Task Push_orders_parents_before_children_regardless_of_insertion_order()
    {
        // Note written after the client, but engine must order by entity ordinal
        // even if outbox insertion order were scrambled; both land in one batch.
        var client = new Client { LastName = "Smith", FirstName = "Anna" };
        await Clients.UpsertAsync(client);
        await Notes.AddAsync(new ClientNote { ClientId = client.Id, Body = "note" });

        await Engine.PushPendingAsync();

        var batch = Assert.Single(_transport.PushedBatches);
        Assert.Equal([SyncEntities.Client, SyncEntities.ClientNote], batch.Select(o => o.Entity).ToArray());

        await using var db = _db.CreateDbContext();
        Assert.Empty(await db.Outbox.ToListAsync());
    }

    [Fact]
    public async Task Transport_failure_backs_off_and_retries_later()
    {
        await Clients.UpsertAsync(new Client { LastName = "Smith", FirstName = "Anna" });
        _transport.PushFailures.Enqueue(new SyncTransportException("offline"));

        await Engine.PushPendingAsync();
        Assert.Empty(_transport.PushedBatches);

        await using (var db = _db.CreateDbContext())
        {
            var entry = Assert.Single(await db.Outbox.ToListAsync());
            Assert.Equal(1, entry.Attempts);
            Assert.Equal("offline", entry.LastError);
            Assert.NotNull(entry.NextAttemptAt);
        }

        // Not eligible yet — nothing is sent.
        await Engine.PushPendingAsync();
        Assert.Empty(_transport.PushedBatches);

        // After the backoff window it goes through.
        _clock.Advance(TimeSpan.FromMinutes(1));
        await Engine.PushPendingAsync();
        Assert.Single(_transport.PushedBatches);
    }

    [Fact]
    public async Task Transport_failures_retry_forever_with_capped_backoff_and_never_park()
    {
        // A device offline for a long stretch must not lose queued changes to
        // the dead letter — only server validation rejections park.
        await Clients.UpsertAsync(new Client { LastName = "Smith", FirstName = "Anna" });

        for (var i = 0; i < 15; i++)
        {
            _transport.PushFailures.Enqueue(new SyncTransportException("offline"));
            await Engine.PushPendingAsync();
            _clock.Advance(TimeSpan.FromMinutes(10));
        }

        await using var db = _db.CreateDbContext();
        var entry = Assert.Single(await db.Outbox.ToListAsync());
        Assert.Empty(await db.DeadLetters.ToListAsync());
        Assert.Equal(15, entry.Attempts);

        // Backoff stays within cap (+1s jitter allowance).
        Assert.NotNull(entry.NextAttemptAt);
        Assert.True(entry.NextAttemptAt <= _clock.UtcNow + _options.BackoffCap + TimeSpan.FromSeconds(1));

        // Connectivity returns: the op still goes through.
        await Engine.PushPendingAsync();
        Assert.Single(_transport.PushedBatches);
    }

    [Fact]
    public async Task Server_rejection_parks_only_the_rejected_op()
    {
        var client = new Client { LastName = "Smith", FirstName = "Anna" };
        await Clients.UpsertAsync(client);
        await Notes.AddAsync(new ClientNote { ClientId = client.Id, Body = "note" });

        await using (var db = _db.CreateDbContext())
        {
            var noteOp = await db.Outbox.SingleAsync(o => o.Entity == SyncEntities.ClientNote);
            _transport.RejectOpIds.Add(noteOp.OpId);
        }

        await Engine.PushPendingAsync();

        await using (var db = _db.CreateDbContext())
        {
            Assert.Empty(await db.Outbox.ToListAsync());
            var dead = Assert.Single(await db.DeadLetters.ToListAsync());
            Assert.Equal(SyncEntities.ClientNote, dead.Entity);
        }
    }

    // ---------------------------------------------------------------- pull

    [Fact]
    public async Task Pull_inserts_new_rows_and_applies_lww()
    {
        var mine = new Client { LastName = "Local", FirstName = "Edit", Phone = "07700 900123" };
        await Clients.UpsertAsync(mine);

        var otherDevice = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

        // A losing (older) update for my row, plus a brand-new client.
        var stale = ClientPayload.From(mine);
        stale.LastName = "Stale";
        stale.UpdatedAt = mine.UpdatedAt - TimeSpan.FromHours(1);
        stale.UpdatedByDevice = otherDevice;
        stale.SyncSeq = 10;

        var fresh = new ClientPayload
        {
            Id = Guid.CreateVersion7(),
            SalonId = _tenant.SalonId,
            LastName = "Remote",
            FirstName = "New",
            UpdatedAt = _clock.UtcNow,
            UpdatedByDevice = otherDevice,
            SyncSeq = 11,
        };

        _transport.PullPages.Enqueue(new SyncPullBundle { Clients = [stale, fresh] });
        await Engine.PullToConvergenceAsync();

        await using var db = _db.CreateDbContext();
        var localMine = await db.Clients.SingleAsync(c => c.Id == mine.Id);
        Assert.Equal("Local", localMine.LastName); // stale update lost LWW

        Assert.NotNull(await db.Clients.SingleOrDefaultAsync(c => c.Id == fresh.Id));

        var state = await db.SyncState.SingleAsync();
        Assert.Equal(11, state.Cursor);
    }

    [Fact]
    public async Task Pull_equal_timestamps_break_ties_by_device_id()
    {
        var mine = new Client { LastName = "Mine", FirstName = "A" };
        await Clients.UpsertAsync(mine);

        // Same updated_at, higher device id → incoming wins deterministically.
        var higherDevice = Guid.Parse("ffffffff-0000-0000-0000-000000000001");
        var incoming = ClientPayload.From(mine);
        incoming.LastName = "Theirs";
        incoming.UpdatedByDevice = higherDevice;
        incoming.SyncSeq = 5;

        _transport.PullPages.Enqueue(new SyncPullBundle { Clients = [incoming] });
        await Engine.PullToConvergenceAsync();

        await using var db = _db.CreateDbContext();
        Assert.Equal("Theirs", (await db.Clients.SingleAsync(c => c.Id == mine.Id)).LastName);
    }

    [Fact]
    public async Task Deleted_service_record_wins_over_newer_local_edit()
    {
        var client = new Client { LastName = "Smith", FirstName = "Anna" };
        await Clients.UpsertAsync(client);

        var services = new ServiceRecordRepository(_db, new OutboxWriter(_clock), _clock, _device, _tenant);
        var record = new ServiceRecord
        {
            ClientId = client.Id,
            PerformedOn = new DateOnly(2026, 1, 1),
            ServiceDescription = "Massage",
            Price = 60m,
        };
        await services.UpsertAsync(record);

        // A tombstone older than the local edit still voids the treatment.
        var tombstone = ServiceRecordPayload.From(record);
        tombstone.DeletedAt = record.UpdatedAt - TimeSpan.FromHours(2);
        tombstone.UpdatedAt = record.UpdatedAt - TimeSpan.FromHours(2);
        tombstone.UpdatedByDevice = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
        tombstone.SyncSeq = 7;

        _transport.PullPages.Enqueue(new SyncPullBundle { Services = [tombstone] });
        await Engine.PullToConvergenceAsync();

        Assert.Empty(await services.GetForClientAsync(client.Id));
    }

    [Fact]
    public async Task Notes_merge_as_a_union_and_never_overwrite()
    {
        var client = new Client { LastName = "Smith", FirstName = "Anna" };
        await Clients.UpsertAsync(client);
        var note = new ClientNote { ClientId = client.Id, Body = "original" };
        await Notes.AddAsync(note);

        var tampered = ClientNotePayload.From(note);
        tampered.Body = "tampered";
        tampered.UpdatedAt = note.UpdatedAt + TimeSpan.FromHours(1);
        tampered.SyncSeq = 3;

        var theirs = new ClientNotePayload
        {
            Id = Guid.CreateVersion7(),
            SalonId = _tenant.SalonId,
            ClientId = client.Id,
            CreatedAt = _clock.UtcNow,
            Body = "their note",
            UpdatedAt = _clock.UtcNow,
            UpdatedByDevice = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),
            SyncSeq = 4,
        };

        _transport.PullPages.Enqueue(new SyncPullBundle { Notes = [tampered, theirs] });
        await Engine.PullToConvergenceAsync();

        var feed = await Notes.GetForClientAsync(client.Id);
        Assert.Equal(2, feed.Count);
        Assert.Contains(feed, n => n.Body == "original");
        Assert.Contains(feed, n => n.Body == "their note");
        Assert.DoesNotContain(feed, n => n.Body == "tampered");
    }

    [Fact]
    public async Task Consent_withdrawal_always_wins_even_when_lww_loses()
    {
        var client = new Client { LastName = "Smith", FirstName = "Anna" };
        await Clients.UpsertAsync(client);

        var consents = new ClientConsentRepository(_db, new OutboxWriter(_clock), _clock, _device, _tenant);
        var consent = new ClientConsent { ClientId = client.Id, Purpose = ConsentPurpose.Marketing };
        await consents.GrantAsync(consent);

        // Incoming withdrawal is OLDER than the local grant → loses LWW, but the
        // withdrawal must still stick.
        var withdrawal = ClientConsentPayload.From(consent);
        withdrawal.WithdrawnAt = consent.UpdatedAt - TimeSpan.FromMinutes(30);
        withdrawal.UpdatedAt = consent.UpdatedAt - TimeSpan.FromMinutes(30);
        withdrawal.UpdatedByDevice = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
        withdrawal.SyncSeq = 9;

        _transport.PullPages.Enqueue(new SyncPullBundle { Consents = [withdrawal] });
        await Engine.PullToConvergenceAsync();

        var stored = Assert.Single(await consents.GetForClientAsync(client.Id));
        Assert.NotNull(stored.WithdrawnAt);
    }

    [Fact]
    public async Task Pull_pages_until_short_and_overlaps_the_cursor()
    {
        // Full page (3 clients at limit=3) then a short one.
        ClientPayload NewClient(long seq) => new()
        {
            Id = Guid.CreateVersion7(),
            SalonId = _tenant.SalonId,
            LastName = $"C{seq}",
            FirstName = "X",
            UpdatedAt = _clock.UtcNow,
            UpdatedByDevice = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),
            SyncSeq = seq,
        };

        _transport.PullPages.Enqueue(new SyncPullBundle { Clients = [NewClient(1), NewClient(2), NewClient(3)] });
        _transport.PullPages.Enqueue(new SyncPullBundle { Clients = [NewClient(4)] });

        await Engine.PullToConvergenceAsync();

        Assert.Equal(2, _transport.PullCursors.Count);
        Assert.Equal(0, _transport.PullCursors[0]);
        // Second pull starts from cursor(3) - overlap window, floored at 0.
        Assert.Equal(Math.Max(0, 3 - _options.OverlapWindow), _transport.PullCursors[1]);

        await using var db = _db.CreateDbContext();
        Assert.Equal(4, await db.Clients.CountAsync());
        Assert.Equal(4, (await db.SyncState.SingleAsync()).Cursor);
    }

    [Fact]
    public async Task Redaction_purges_the_row_locally()
    {
        var client = new Client { LastName = "Erased", FirstName = "Elsewhere" };
        await Clients.UpsertAsync(client);

        _transport.PullPages.Enqueue(new SyncPullBundle
        {
            Redactions = [new RedactionPayload
            {
                Entity = SyncEntities.Client,
                EntityId = client.Id,
                RedactedAt = _clock.UtcNow,
                SyncSeq = 2,
            }],
        });

        await Engine.PullToConvergenceAsync();

        await using var db = _db.CreateDbContext();
        Assert.Empty(await db.Clients.ToListAsync());
    }

    // ------------------------------------------------------------- cursor math

    [Fact]
    public void Cursor_advances_to_min_of_full_arrays_not_overall_max()
    {
        var bundle = new SyncPullBundle
        {
            // clients full at limit 2 with max seq 5; notes short with max seq 9.
            Clients =
            [
                new ClientPayload { SyncSeq = 4 },
                new ClientPayload { SyncSeq = 5 },
            ],
            Notes = [new ClientNotePayload { SyncSeq = 9 }],
        };

        var cursor = SyncEngine.ComputeNewCursor(0, bundle, limit: 2, out var converged);

        // Advancing to 9 would skip client rows 6..8 on the next page.
        Assert.Equal(5, cursor);
        Assert.False(converged);
    }

    [Fact]
    public void Cursor_on_all_short_page_advances_to_overall_max_and_converges()
    {
        var bundle = new SyncPullBundle
        {
            Clients = [new ClientPayload { SyncSeq = 12 }],
            Services = [new ServiceRecordPayload { SyncSeq = 15 }],
        };

        var cursor = SyncEngine.ComputeNewCursor(10, bundle, limit: 500, out var converged);
        Assert.Equal(15, cursor);
        Assert.True(converged);
    }

    [Fact]
    public void Cursor_never_regresses_below_current()
    {
        var bundle = new SyncPullBundle { Clients = [new ClientPayload { SyncSeq = 3 }] };
        var cursor = SyncEngine.ComputeNewCursor(100, bundle, limit: 500, out _);
        Assert.Equal(100, cursor);
    }
}
