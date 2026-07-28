using Microsoft.EntityFrameworkCore;
using SIG.ClientCard.Core.Entities;
using SIG.ClientCard.Core.SyncContracts;
using SIG.ClientCard.Data.Infrastructure;
using SIG.ClientCard.Data.Repositories;
using SIG.ClientCard.Sync;
using SIG.ClientCard.Tests.TestInfra;

namespace SIG.ClientCard.Tests;

public class ReconcileAndDeadLetterTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly FakeClock _clock = new();
    private readonly FakeDevice _device = new();
    private readonly FakeTenant _tenant = new();
    private readonly FakeTransport _transport = new();
    private readonly SyncOptions _options = new();

    private SyncEngine Engine => new(_db, _transport, _clock, _options);
    private ClientRepository Clients => new(_db, new OutboxWriter(_clock), _clock, _device, _tenant);

    public void Dispose() => _db.Dispose();

    private static EntityChecksum ChecksumFor(params Guid[] ids)
        => new() { Count = ids.Length, IdsHash = SyncEngine.HashIds([.. ids]) };

    [Fact]
    public async Task Reconcile_matching_checksums_confirms_and_stamps_time()
    {
        var client = new Client { LastName = "Smith", FirstName = "Anna" };
        await Clients.UpsertAsync(client);

        _transport.Checksum = new SyncChecksum { Clients = ChecksumFor(client.Id) };

        Assert.True(await Engine.ReconcileAsync());

        await using var db = _db.CreateDbContext();
        Assert.NotNull((await db.SyncState.SingleAsync()).LastReconcileAt);
    }

    [Fact]
    public async Task Reconcile_mismatch_resets_cursor_and_repulls_missing_rows()
    {
        // Local has one client; the server says there are two — a row the
        // cursor silently skipped. Reconcile must reset and re-pull it.
        var mine = new Client { LastName = "Smith", FirstName = "Anna" };
        await Clients.UpsertAsync(mine);

        var missingId = Guid.CreateVersion7();
        _transport.Checksum = new SyncChecksum { Clients = ChecksumFor(mine.Id, missingId) };
        _transport.PullPages.Enqueue(new SyncPullBundle
        {
            Clients =
            [
                ClientPayload.From(mine),
                new ClientPayload
                {
                    Id = missingId,
                    SalonId = _tenant.SalonId,
                    LastName = "Skipped",
                    FirstName = "Row",
                    UpdatedAt = _clock.UtcNow,
                    UpdatedByDevice = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),
                    SyncSeq = 42,
                },
            ],
        });

        Assert.False(await Engine.ReconcileAsync());

        // The re-pull started from zero (cursor was reset before pulling).
        Assert.Equal(0, _transport.PullCursors[^1]);

        await using var db = _db.CreateDbContext();
        Assert.Equal(2, await db.Clients.CountAsync());
        Assert.NotNull(await db.Clients.SingleOrDefaultAsync(c => c.Id == missingId));
    }

    [Fact]
    public void HashIds_is_order_insensitive_and_canonical()
    {
        var a = Guid.Parse("019fa902-0000-7000-8000-000000000001");
        var b = Guid.Parse("019fa902-0000-7000-8000-000000000002");

        Assert.Equal(SyncEngine.HashIds([a, b]), SyncEngine.HashIds([b, a]));
        Assert.Equal("", SyncEngine.HashIds([]));
        // md5 of the joined canonical form — stable across platforms.
        Assert.Equal(32, SyncEngine.HashIds([a]).Length);
    }

    [Fact]
    public async Task Rejected_op_can_be_requeued_and_then_pushes()
    {
        var client = new Client { LastName = "Smith", FirstName = "Anna" };
        await Clients.UpsertAsync(client);

        Guid opId;
        await using (var db = _db.CreateDbContext())
        {
            opId = (await db.Outbox.SingleAsync()).OpId;
        }

        _transport.RejectOpIds.Add(opId);
        await Engine.PushPendingAsync();

        await using (var db = _db.CreateDbContext())
        {
            Assert.Single(await db.DeadLetters.ToListAsync());
            Assert.Empty(await db.Outbox.ToListAsync());
        }

        // Server-side issue fixed: requeue and push again (idempotency key kept).
        _transport.RejectOpIds.Clear();
        await Engine.RequeueDeadLetterAsync(opId);
        await Engine.PushPendingAsync();

        await using (var db2 = _db.CreateDbContext())
        {
            Assert.Empty(await db2.DeadLetters.ToListAsync());
            Assert.Empty(await db2.Outbox.ToListAsync());
        }

        Assert.Equal(opId, _transport.PushedBatches[^1].Single().OpId);
    }

    [Fact]
    public async Task Discarded_op_is_gone_but_local_row_survives()
    {
        var client = new Client { LastName = "Smith", FirstName = "Anna" };
        await Clients.UpsertAsync(client);

        Guid opId;
        await using (var db = _db.CreateDbContext())
        {
            opId = (await db.Outbox.SingleAsync()).OpId;
        }

        _transport.RejectOpIds.Add(opId);
        await Engine.PushPendingAsync();
        await Engine.DiscardDeadLetterAsync(opId);

        await using (var db = _db.CreateDbContext())
        {
            Assert.Empty(await db.DeadLetters.ToListAsync());
            Assert.Empty(await db.Outbox.ToListAsync());
            Assert.Single(await db.Clients.ToListAsync());
        }
    }
}
