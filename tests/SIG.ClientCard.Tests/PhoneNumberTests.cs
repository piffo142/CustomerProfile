using SIG.ClientCard.Core;

namespace SIG.ClientCard.Tests;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("07700 900123", "+447700900123")]
    [InlineData("+44 7700 900123", "+447700900123")]
    [InlineData("0044 7700 900123", "+447700900123")]
    [InlineData("(07700) 900-123", "+447700900123")]
    [InlineData("+1 555 0100", "+15550100")]
    public void Normalises_common_uk_formats(string raw, string expected)
        => Assert.Equal(expected, PhoneNumber.NormalizeE164(raw));

    [Fact]
    public void Empty_input_stays_empty()
        => Assert.Equal(string.Empty, PhoneNumber.NormalizeE164("  "));

    [Fact]
    public void Non_numeric_input_is_returned_trimmed()
        => Assert.Equal("n/a", PhoneNumber.NormalizeE164(" n/a "));

    [Theory]
    [InlineData("0")]
    [InlineData("00")]
    [InlineData("999")]
    public void Inputs_too_short_to_be_numbers_are_kept_as_typed(string raw)
        => Assert.Equal(raw, PhoneNumber.NormalizeE164(raw));
}
