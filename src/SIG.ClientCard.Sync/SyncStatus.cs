namespace SIG.ClientCard.Sync;

/// <summary>
/// What the UI shows: pending-op count and last-sync time. Users tolerate
/// delay; they do not tolerate uncertainty.
/// </summary>
public sealed record SyncStatus(
    int PendingOps,
    int DeadLetteredOps,
    DateTimeOffset? LastPushAt,
    DateTimeOffset? LastPullAt);
