namespace SIG.ClientCard.Data.Entities;

/// <summary>
/// The device outbox. Rows are written in the same SQLite transaction as the
/// domain change — anything less and a crash between the two loses the change
/// permanently.
/// </summary>
public class SyncOutboxEntry
{
    /// <summary>UUIDv7 — the idempotency key for the op.</summary>
    public Guid OpId { get; set; }

    public string Entity { get; set; } = string.Empty;
    public Guid EntityId { get; set; }

    /// <summary>upsert | delete.</summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>JSON snapshot of the row at write time.</summary>
    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }

    /// <summary>When the next retry is allowed. Exponential backoff with jitter, capped at 5 minutes.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }
}
