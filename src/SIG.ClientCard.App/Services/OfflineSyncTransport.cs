using SIG.ClientCard.Core.SyncContracts;
using SIG.ClientCard.Sync;

namespace SIG.ClientCard.App.Services;

/// <summary>
/// Phase 0 stand-in: there is no server yet. Pushes report a transport failure
/// (so ops stay queued, untouched), pulls return nothing.
/// </summary>
public sealed class OfflineSyncTransport : ISyncTransport
{
    public Task<SyncPushResult> PushAsync(IReadOnlyList<SyncOp> ops, CancellationToken ct = default)
        => throw new SyncTransportException("sync not configured (Phase 0, local only)");

    public Task<SyncPullBundle> PullAsync(long cursor, int limit, CancellationToken ct = default)
        => Task.FromResult(new SyncPullBundle());
}
