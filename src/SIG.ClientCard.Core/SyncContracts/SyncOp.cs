namespace SIG.ClientCard.Core.SyncContracts;

/// <summary>
/// One queued write. <see cref="OpId"/> is a UUIDv7 and is the idempotency key:
/// the server records applied op ids and skips replays, which is what makes
/// at-least-once delivery safe.
/// </summary>
public sealed record SyncOp(
    Guid OpId,
    string Entity,
    Guid EntityId,
    string Operation,
    string PayloadJson,
    DateTimeOffset CreatedAt);
