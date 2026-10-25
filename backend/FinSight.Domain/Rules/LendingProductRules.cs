namespace FinSight.Domain.Rules;

/// <summary>
/// Configurable limits for a (synthetic) personal-loan product. Bound from the
/// "LendingProductRules" section of appsettings.json, so limits can change
/// without a code change. Defaults here are used by tests and as fallbacks.
/// </summary>
public class LendingProductRules
{
    public const string SectionName = "LendingProductRules";

    public decimal MinLoanAmount { get; set; } = 50_000m;
    public decimal MaxLoanAmount { get; set; } = 2_500_000m;
    public int MinTenureMonths { get; set; } = 12;
    public int MaxTenureMonths { get; set; } = 84;
    public decimal MinAnnualInterestRate { get; set; } = 9.5m;
    public decimal MaxAnnualInterestRate { get; set; } = 24m;
    public decimal MinMonthlyIncome { get; set; } = 20_000m;

    /// <summary>Maximum post-loan debt-to-income ratio, e.g. 0.50 = 50% of monthly income.</summary>
    public decimal MaxDebtToIncomeRatio { get; set; } = 0.50m;
}
