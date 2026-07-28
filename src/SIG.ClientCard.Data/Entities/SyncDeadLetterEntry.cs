namespace SIG.ClientCard.Data.Entities;

/// <summary>
/// Ops the server rejected (validation, not transport) or that exhausted their
/// retries. Never retried automatically; surfaced as a badge in the UI.
/// </summary>
public class SyncDeadLetterEntry
{
    public Guid OpId { get; set; }
    public string Entity { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset ParkedAt { get; set; }
}
