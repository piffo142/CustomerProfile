namespace SIG.ClientCard.Sync;

public sealed class SyncOptions
{
    /// <summary>Max ops per push batch.</summary>
    public int PushBatchSize { get; init; } = 100;

    /// <summary>Row limit per entity per pull page.</summary>
    public int PullLimit { get; init; } = 500;

    /// <summary>
    /// Sequence values are allocated at statement time, not commit time, so a
    /// long transaction can commit below an already-consumed cursor. Pulling
    /// from cursor - overlap re-delivers that window; idempotent upserts make
    /// the re-delivery harmless.
    /// </summary>
    public long OverlapWindow { get; init; } = 1000;

    public TimeSpan BackoffBase { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan BackoffCap { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How often the full checksum reconciliation runs after a converged pull.
    /// It asserts the cursor never silently lost rows; a mismatch resets the
    /// cursor and re-pulls from zero.
    /// </summary>
    public TimeSpan ReconcileInterval { get; init; } = TimeSpan.FromHours(24);
}
