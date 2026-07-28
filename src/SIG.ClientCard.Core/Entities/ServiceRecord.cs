namespace SIG.ClientCard.Core.Entities;

/// <summary>
/// One row of the Date/Service/Price grid. Append-mostly, rarely edited.
/// </summary>
public class ServiceRecord : SyncedEntity
{
    public Guid ClientId { get; set; }

    /// <summary>The card records a day, not a time.</summary>
    public DateOnly PerformedOn { get; set; }

    public string ServiceDescription { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public string CurrencyCode { get; set; } = "GBP";

    /// <summary>
    /// Nullable FK reserved for the future service catalogue. Present from v1 so
    /// it never has to be retrofitted across 50k free-text rows.
    /// </summary>
    public Guid? ServiceCatalogId { get; set; }

    /// <summary>Storage path of an attached photo for this treatment.</summary>
    public string? PhotoPath { get; set; }
}
