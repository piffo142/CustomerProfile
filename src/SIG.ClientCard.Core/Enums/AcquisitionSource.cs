namespace SIG.ClientCard.Core.Enums;

/// <summary>
/// "How did you hear about us" is a source + detail pair. The card's Location
/// checkbox has no companion line, so it degenerates to source-only.
/// </summary>
public enum AcquisitionSource
{
    Unknown = 0,
    Referral = 1,
    Location = 2,
    Other = 3,
}
