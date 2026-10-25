namespace FinSight.Domain.Rules;

/// <summary>
/// Equated Monthly Instalment (EMI) for a fully amortising loan:
///
///     EMI = P × r × (1 + r)^n / ((1 + r)^n − 1)
///
/// where P is the principal, r the monthly rate (annual % / 12 / 100) and n the
/// tenure in months. At 0% interest the formula divides by zero, so EMI = P / n.
///
/// Everything uses <see cref="decimal"/>: double cannot represent values like 0.1
/// exactly, and those errors compound over hundreds of periods.
/// </summary>
public static class EmiCalculator
{
    // Upper bounds keep (1 + r)^n well inside decimal's range (~7.9e28).
    public const int MaxTenureMonths = 600;
    public const decimal MaxAnnualRatePercent = 100m;

    public static decimal Calculate(decimal principal, decimal annualRatePercent, int tenureMonths)
    {
        if (principal <= 0)
            throw new ArgumentOutOfRangeException(nameof(principal), principal, "Principal must be greater than zero.");
        if (tenureMonths <= 0 || tenureMonths > MaxTenureMonths)
            throw new ArgumentOutOfRangeException(nameof(tenureMonths), tenureMonths, $"Tenure must be between 1 and {MaxTenureMonths} months.");
        if (annualRatePercent < 0 || annualRatePercent > MaxAnnualRatePercent)
            throw new ArgumentOutOfRangeException(nameof(annualRatePercent), annualRatePercent, $"Annual rate must be between 0 and {MaxAnnualRatePercent}%.");

        if (annualRatePercent == 0)
            return RoundMoney(principal / tenureMonths);

        var monthlyRate = annualRatePercent / 12m / 100m;
        var growth = Power(1m + monthlyRate, tenureMonths);
        var emi = principal * monthlyRate * growth / (growth - 1m);

        // Round only once, at the end. Rounding intermediate values introduces drift.
        return RoundMoney(emi);
    }

    /// <summary>
    /// Rounds to paise using "round half away from zero", the convention people expect
    /// for currency. .NET's default (banker's rounding) would turn 0.125 into 0.12.
    /// </summary>
    public static decimal RoundMoney(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    // Math.Pow only accepts double, so use exponentiation by squaring to stay in decimal.
    private static decimal Power(decimal value, int exponent)
    {
        var result = 1m;
        while (exponent > 0)
        {
            if ((exponent & 1) == 1)
                result *= value;
            exponent >>= 1;
            // Skip the final squaring: it is unused and could overflow for long tenures.
            if (exponent > 0)
                value *= value;
        }
        return result;
    }
}
