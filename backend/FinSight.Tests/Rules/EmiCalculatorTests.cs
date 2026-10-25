using FinSight.Domain.Rules;

namespace FinSight.Tests.Rules;

public class EmiCalculatorTests
{
    // Expected values were computed independently with Python's decimal module at 60-digit
    // precision (round half up), not with the code under test.
    [Theory]
    [InlineData(100_000, 10, 12, 8_791.59)]
    [InlineData(1_000_000, 12, 120, 14_347.09)]
    [InlineData(500_000, 8.5, 60, 10_258.27)]
    [InlineData(600_000, 10.75, 48, 15_434.57)]
    [InlineData(800_000, 13, 36, 26_955.16)]
    [InlineData(450_000, 11.5, 36, 14_839.20)]
    public void Calculate_MatchesReferenceValues(decimal principal, decimal rate, int tenure, decimal expected)
    {
        Assert.Equal(expected, EmiCalculator.Calculate(principal, rate, tenure));
    }

    [Fact]
    public void Calculate_ZeroInterest_IsPrincipalDividedByTenure()
    {
        Assert.Equal(10_416.67m, EmiCalculator.Calculate(250_000m, 0m, 24));
    }

    [Fact]
    public void Calculate_ZeroInterest_DividesExactlyWhenPossible()
    {
        Assert.Equal(10_000m, EmiCalculator.Calculate(120_000m, 0m, 12));
    }

    [Fact]
    public void Calculate_SingleMonthTenure_RepaysPrincipalPlusOneMonthInterest()
    {
        Assert.Equal(121_200.00m, EmiCalculator.Calculate(120_000m, 12m, 1));
    }

    [Fact]
    public void Calculate_RoundsMidpointAwayFromZero_NotBankersRounding()
    {
        // 9 / 8 = 1.125 exactly. Banker's rounding (the .NET default) would give 1.12.
        Assert.Equal(1.13m, EmiCalculator.Calculate(9m, 0m, 8));
    }

    [Fact]
    public void Calculate_ResultHasAtMostTwoDecimalPlaces()
    {
        var emi = EmiCalculator.Calculate(333_333m, 13.37m, 77);
        Assert.Equal(Math.Round(emi, 2), emi);
    }

    [Fact]
    public void Calculate_AtExtremeSupportedBounds_DoesNotOverflow()
    {
        // 100% p.a. over 600 months: (1 + r)^n ≈ 7.5e20, still well inside decimal's range.
        Assert.Equal(83_333_333.33m, EmiCalculator.Calculate(1_000_000_000m, 100m, 600));
    }

    [Fact]
    public void Calculate_TotalRepaidIsAtLeastPrincipal()
    {
        const decimal principal = 750_000m;
        var emi = EmiCalculator.Calculate(principal, 11m, 60);
        Assert.True(emi * 60 > principal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100_000)]
    public void Calculate_NonPositivePrincipal_Throws(decimal principal)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => EmiCalculator.Calculate(principal, 10m, 12));
        Assert.Equal("principal", ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-12)]
    [InlineData(601)]
    public void Calculate_InvalidTenure_Throws(int tenure)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => EmiCalculator.Calculate(100_000m, 10m, tenure));
        Assert.Equal("tenureMonths", ex.ParamName);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public void Calculate_InvalidRate_Throws(decimal rate)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => EmiCalculator.Calculate(100_000m, rate, 12));
        Assert.Equal("annualRatePercent", ex.ParamName);
    }
}
