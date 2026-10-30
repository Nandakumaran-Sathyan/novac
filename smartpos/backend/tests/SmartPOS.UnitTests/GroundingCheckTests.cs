using SmartPOS.Application.AI;

namespace SmartPOS.UnitTests;

public class GroundingCheckTests
{
    private const string ToolJson = """{"tool":"GetYesterdaySales","data":{"from":"2026-10-29","totalSales":10592.46,"bills":36,"averageBill":294.24,"products":[{"name":"Cola 500 ml","netSales":47538.25}]}}""";

    private static IReadOnlyList<string> Check(string answer, params string[] sources) => GroundingCheck.FindUnsupportedNumbers(answer, sources);

    [Theory]
    [InlineData("Yesterday you sold ₹10,592.46 across 36 bills.")]
    [InlineData("Yesterday you sold ₹10592.46 across **36** bills, averaging ₹294.24.")]
    [InlineData("Cola earned ₹47,538.25 (about ₹47,538).")]                    // rounded to whole rupees
    [InlineData("Top 5: 1. Cola 500 ml")]                                     // small numbers and product names
    [InlineData("Figures for 29 October 2026.")]                              // date parts are in the tool result
    [InlineData("No sales were recorded.")]
    public void Figures_found_in_the_tool_results_pass(string answer) => Assert.Empty(Check(answer, ToolJson));

    [Theory]
    [InlineData("Yesterday you sold ₹12,500 across 36 bills.", "12,500")]
    [InlineData("You sold 45 bills.", "45")]
    [InlineData("Sales grew 37%.", "37")]
    [InlineData("That is ₹10,592.47.", "10,592.47")]                         // off by one paisa is still wrong
    [InlineData("Revenue: ₹1,23,456.", "1,23,456")]
    public void Figures_not_in_the_tool_results_are_reported(string answer, string expected) =>
        Assert.Contains(expected, Check(answer, ToolJson));

    [Fact]
    public void Indian_and_western_grouping_of_the_same_value_both_match()
    {
        Assert.Empty(Check("₹1,23,456.50", """{"v":123456.5}"""));
        Assert.Empty(Check("₹123,456.50", """{"v":123456.5}"""));
    }

    [Fact]
    public void With_no_sources_only_small_numbers_are_allowed()
    {
        Assert.Empty(Check("I can show your top 5 products for the last 7 days."));
        Assert.Equal(["1,250"], Check("Sales were 1,250 today."));
    }

    [Fact]
    public void Trailing_punctuation_is_not_part_of_the_number()
    {
        Assert.Empty(Check("You had 36 bills.", ToolJson));
        Assert.Empty(Check("Bills: 36, average 294.24.", ToolJson));
    }
}
