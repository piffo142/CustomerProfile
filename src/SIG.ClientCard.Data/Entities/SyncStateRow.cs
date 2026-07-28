namespace SIG.ClientCard.Data.Entities;

/// <summary>
/// Single-row table holding the pull cursor. The cursor is only ever advanced
/// in the same transaction that applied the pulled bundle — cursor commit and
/// data apply must be atomic or a crash mid-apply loses data silently.
/// </summary>
public class SyncStateRow
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public long Cursor { get; set; }
    public DateTimeOffset? LastPullAt { get; set; }
    public DateTimeOffset? LastPushAt { get; set; }
    public DateTimeOffset? LastReconcileAt { get; set; }
}
