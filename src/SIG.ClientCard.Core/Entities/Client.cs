using SIG.ClientCard.Core.Enums;

namespace SIG.ClientCard.Core.Entities;

/// <summary>
/// The card header: mutable, low write frequency. Notes are deliberately NOT a
/// column here — they are append-only <see cref="ClientNote"/> rows.
/// </summary>
public class Client : SyncedEntity
{
    public string LastName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;

    /// <summary>Single multi-line field; not normalised until geo is needed.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>E.164 normalised number used for display and dialling.</summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>Raw phone input as typed, kept as a shadow of <see cref="Phone"/>.</summary>
    public string PhoneRaw { get; set; } = string.Empty;

    public string? Email { get; set; }

    public AcquisitionSource AcquisitionSource { get; set; }

    /// <summary>Referrer name, or the free text captured for "Other".</summary>
    public string? AcquisitionDetail { get; set; }

    // Fields the paper card omits but the app must not.
    public DateOnly? DateOfBirth { get; set; }
    public bool MedicalFlag { get; set; }
    public string? GpDetails { get; set; }
    public DateOnly? PatchTestOn { get; set; }
    public PatchTestResult PatchTestResult { get; set; }

    /// <summary>Storage path of a photo of the original paper card (migration path).</summary>
    public string? CardPhotoPath { get; set; }

    public List<ClientNote> Notes { get; set; } = [];
    public List<ServiceRecord> Services { get; set; } = [];
    public List<ClientConsent> Consents { get; set; } = [];

    public string DisplayName => $"{LastName}, {FirstName}".Trim(' ', ',');
}
