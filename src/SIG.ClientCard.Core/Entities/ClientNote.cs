namespace SIG.ClientCard.Core.Entities;

/// <summary>
/// Append-only. Two therapists editing a mutable notes column on separate
/// devices under last-writer-wins silently destroys clinical history; as
/// immutable rows the merge is a union and conflict resolution disappears.
/// Rendered as a reverse-chronological feed. Never updated after insert —
/// erasure is the only delete path.
/// </summary>
public class ClientNote : SyncedEntity
{
    public Guid ClientId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? AuthorUserId { get; set; }
    public string Body { get; set; } = string.Empty;
}
