using SmartPOS.Application.Common;

namespace SmartPOS.UnitTests;

public class SearchPatternTests
{
    [Theory]
    [InlineData("milk", "%milk%")]
    [InlineData("  milk ", "%milk%")]
    [InlineData("Cola 500ML", "%cola 500ml%")] // lower-cased: PostgreSQL LIKE is case-sensitive
    [InlineData("50%", "%50!%%")]
    [InlineData("a_b", "%a!_b%")]
    [InlineData("wow!", "%wow!!%")]
    public void Escapes_like_wildcards(string term, string expected) =>
        Assert.Equal(expected, SearchPattern.Contains(term));

    [Theory]
    [InlineData(1.5, true)]
    [InlineData(1.25, true)]
    [InlineData(1.255, false)]
    public void MaxDecimalPlaces_validates_scale(decimal value, bool valid) =>
        Assert.Equal(valid, new MaxDecimalPlacesAttribute(2).IsValid(value));
}
