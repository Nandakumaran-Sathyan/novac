using System.Text.RegularExpressions;
using SmartPOS.Application.Customers;

namespace SmartPOS.UnitTests;

public class PhoneRulesTests
{
    [Theory]
    [InlineData("9876543210")]
    [InlineData("6000000000")]
    [InlineData("7012345678")]
    [InlineData("8123456789")]
    [InlineData("98765 43210")]
    [InlineData("98765-43210")]
    [InlineData("+919876543210")]
    [InlineData("+91 98765 43210")]
    [InlineData("+91-98765-43210")]
    [InlineData("919876543210")]
    [InlineData("09876543210")]
    [InlineData("9198765432")]          // a 10-digit number that happens to start with 91
    public void Accepts_indian_mobile_numbers(string phone) => Assert.Matches(PhoneRules.Pattern, phone);

    [Theory]
    [InlineData("1234567890")]          // mobiles start with 6-9
    [InlineData("5876543210")]
    [InlineData("0876543210")]
    [InlineData("987654321")]           // 9 digits
    [InlineData("98765432101")]         // 11 digits, no 0 in front
    [InlineData("+1 415 555 0101")]     // another country
    [InlineData("+92 98765 43210")]
    [InlineData("044 2434 5678")]       // landline
    [InlineData("98765  43210")]        // two separators in a row
    [InlineData("(98765) 43210")]
    [InlineData("९८७६५४३२१०")]           // Devanagari digits
    [InlineData("")]
    public void Refuses_anything_else(string phone) => Assert.DoesNotMatch(new Regex(PhoneRules.Pattern), phone);

    [Theory]
    [InlineData("9876543210", "9876543210")]
    [InlineData(" 98765 43210 ", "9876543210")]
    [InlineData("+91 98765-43210", "9876543210")]
    [InlineData("919876543210", "9876543210")]
    [InlineData("09876543210", "9876543210")]
    [InlineData("9198765432", "9198765432")]
    [InlineData("+91 98765", "98765")]  // a partial number typed into search
    public void Normalizes_to_the_bare_ten_digits(string input, string expected) =>
        Assert.Equal(expected, PhoneRules.Normalize(input));
}
