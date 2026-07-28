using SIG.ClientCard.Core.Enums;

namespace SIG.ClientCard.Core.Entities;

/// <summary>
/// Consent record for GDPR purposes. Conflict policy: withdrawal always wins,
/// never last-writer-wins — that is a legal requirement, not a preference.
/// </summary>
public class ClientConsent : SyncedEntity
{
    public Guid ClientId { get; set; }
    public ConsentPurpose Purpose { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset? WithdrawnAt { get; set; }
    public Guid? CapturedByUserId { get; set; }

    /// <summary>Reference to a signature image blob in storage, when captured.</summary>
    public string? SignatureBlobRef { get; set; }

    public bool IsActive => WithdrawnAt is null;
}
