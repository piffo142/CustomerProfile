namespace SIG.ClientCard.Core;

/// <summary>
/// Minimal E.164 normalisation. Phone is stored normalised with the raw input
/// kept in a shadow column, so this can be swapped for libphonenumber later
/// without a data migration.
/// </summary>
public static class PhoneNumber
{
    /// <param name="raw">Whatever the user typed.</param>
    /// <param name="defaultCountryCode">Dialling code applied to national-format numbers. UK by default.</param>
    /// <returns>An E.164-shaped string, or the trimmed input when it cannot be normalised.</returns>
    public static string NormalizeE164(string raw, string defaultCountryCode = "44")
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Trim();
        var hasPlus = trimmed.StartsWith('+');
        var digits = new string(trimmed.Where(char.IsAsciiDigit).ToArray());

        if (digits.Length == 0)
        {
            return trimmed;
        }

        if (hasPlus)
        {
            return "+" + digits;
        }

        // "00" international prefix.
        if (digits.StartsWith("00") && digits.Length > 4)
        {
            return "+" + digits[2..];
        }

        // National format: drop the trunk zero, prepend the default country code.
        if (digits.StartsWith('0'))
        {
            return "+" + defaultCountryCode + digits[1..];
        }

        // Already looks like country-code-first (e.g. "44 7700 900123").
        if (digits.StartsWith(defaultCountryCode) && digits.Length > 10)
        {
            return "+" + digits;
        }

        return "+" + defaultCountryCode + digits;
    }
}
