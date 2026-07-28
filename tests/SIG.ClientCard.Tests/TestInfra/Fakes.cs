using SIG.ClientCard.Core.Abstractions;
using SIG.ClientCard.Core.SyncContracts;
using SIG.ClientCard.Sync;

namespace SIG.ClientCard.Tests.TestInfra;

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => UtcNow += by;
}

public sealed class FakeDevice(Guid? id = null) : IDeviceIdentity
{
    public Guid DeviceId { get; } = id ?? Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
}

public sealed class FakeTenant : ITenantContext
{
    public Guid SalonId { get; } = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
}

public sealed class FakeTransport : ISyncTransport
{
    public List<IReadOnlyList<SyncOp>> PushedBatches { get; } = [];
    public Queue<SyncTransportException> PushFailures { get; } = new();
    public HashSet<Guid> RejectOpIds { get; } = [];
    public Queue<SyncPullBundle> PullPages { get; } = new();
    public List<long> PullCursors { get; } = [];

    public Task<SyncPushResult> PushAsync(IReadOnlyList<SyncOp> ops, CancellationToken ct = default)
    {
        if (PushFailures.Count > 0)
        {
            throw PushFailures.Dequeue();
        }

        PushedBatches.Add(ops);
        var result = new SyncPushResult();
        result.RejectedOpIds.AddRange(ops.Where(o => RejectOpIds.Contains(o.OpId)).Select(o => o.OpId));
        return Task.FromResult(result);
    }

    public Task<SyncPullBundle> PullAsync(long cursor, int limit, CancellationToken ct = default)
    {
        PullCursors.Add(cursor);
        return Task.FromResult(PullPages.Count > 0 ? PullPages.Dequeue() : new SyncPullBundle());
    }

    public SyncChecksum? Checksum { get; set; }

    public Task<SyncChecksum> GetChecksumAsync(CancellationToken ct = default)
        => Checksum is not null
            ? Task.FromResult(Checksum)
            : throw new SyncTransportException("checksum unavailable");
}
