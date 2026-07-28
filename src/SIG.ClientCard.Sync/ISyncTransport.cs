using SIG.ClientCard.Core.SyncContracts;

namespace SIG.ClientCard.Sync;

/// <summary>
/// The wire. Implementations must distinguish transport failure (throw
/// <see cref="SyncTransportException"/> — the batch is retried) from validation
/// rejection (returned in <see cref="SyncPushResult.RejectedOpIds"/> — those ops
/// are parked in the dead letter, never retried).
/// </summary>
public interface ISyncTransport
{
    Task<SyncPushResult> PushAsync(IReadOnlyList<SyncOp> ops, CancellationToken ct = default);

    Task<SyncPullBundle> PullAsync(long cursor, int limit, CancellationToken ct = default);

    /// <summary>Server-side per-entity checksums for the periodic full reconciliation.</summary>
    Task<SyncChecksum> GetChecksumAsync(CancellationToken ct = default);
}

/// <summary>Transient failure: network down, 5xx, timeout. Safe to retry with backoff.</summary>
public sealed class SyncTransportException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// The server refused this client's schema version (client_too_old). Not
/// retryable — the app must be updated. Queued ops stay safely in the outbox.
/// </summary>
public sealed class SyncUpdateRequiredException(string message) : Exception(message);
