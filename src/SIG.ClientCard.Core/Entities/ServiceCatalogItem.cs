namespace SIG.ClientCard.Core.Entities;

/// <summary>
/// The service catalogue behind the picker on the add-service screen. Learned
/// automatically from free-text entries so a salon builds its menu just by
/// using the app. Conflict policy: LWW, same as the client header.
/// </summary>
public class ServiceCatalogItem : SyncedEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Price pre-filled by the picker; the therapist can still override per visit.</summary>
    public decimal DefaultPrice { get; set; }

    public string CurrencyCode { get; set; } = "GBP";
}
