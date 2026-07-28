namespace SIG.ClientCard.Core.SyncContracts;

/// <summary>
/// Server-side aggregate used by the periodic full reconciliation: row count
/// and an order-insensitive-safe hash (md5 of canonical ids, uuid-ordered) per
/// entity. Any mismatch with the local computation means the cursor silently
/// lost or duplicated rows — the client then resets its cursor and re-pulls.
/// </summary>
public sealed class SyncChecksum
{
    public EntityChecksum Clients { get; set; } = new();
    public EntityChecksum Catalog { get; set; } = new();
    public EntityChecksum Services { get; set; } = new();
    public EntityChecksum Notes { get; set; } = new();
    public EntityChecksum Consents { get; set; } = new();
}

public sealed class EntityChecksum
{
    public long Count { get; set; }
    public long MaxSeq { get; set; }
    public string IdsHash { get; set; } = "";
}
