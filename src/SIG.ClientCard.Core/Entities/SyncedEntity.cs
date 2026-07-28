namespace SIG.ClientCard.Core.Entities;

/// <summary>
/// Base for every row that participates in sync. The change-tracking columns
/// mirror the server schema: <see cref="UpdatedAt"/> is for conflict resolution
/// only (never a cursor), <see cref="SyncSeq"/> is the server-assigned cursor
/// and is 0 until the row has round-tripped.
/// </summary>
public abstract class SyncedEntity
{
    public Guid Id { get; set; }

    /// <summary>Tenant discriminator. Present on every synced row.</summary>
    public Guid SalonId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Device that produced the last write. Echo suppression and LWW tiebreak.</summary>
    public Guid UpdatedByDevice { get; set; }

    /// <summary>Tombstone. Without it, deletes never propagate.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Server-assigned monotonic cursor from the shared sequence.</summary>
    public long SyncSeq { get; set; }
}
