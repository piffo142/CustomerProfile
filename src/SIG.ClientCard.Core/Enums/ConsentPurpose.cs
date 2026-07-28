namespace SIG.ClientCard.Core.Enums;

/// <summary>
/// Free-text notes on a beauty therapy client will contain allergies, medications
/// and contraindications — UK GDPR Article 9 special category data. Consent is
/// recorded per purpose so it can be granted and withdrawn independently.
/// </summary>
public enum ConsentPurpose
{
    /// <summary>Holding the client record itself.</summary>
    RecordKeeping = 0,

    /// <summary>Health-related notes: allergies, medications, skin conditions.</summary>
    SpecialCategoryData = 1,

    Marketing = 2,

    /// <summary>Before/after photos attached to service records.</summary>
    Photography = 3,
}
