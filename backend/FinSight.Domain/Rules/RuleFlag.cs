namespace FinSight.Domain.Rules;

public enum FlagSeverity
{
    /// <summary>Worth noting; does not block review.</summary>
    Info,

    /// <summary>A reviewer should check or obtain more information.</summary>
    Warning,

    /// <summary>Breaches a product rule or makes the application impossible to evaluate.</summary>
    Critical
}

/// <summary>
/// One finding from the deterministic rule engine. Code is a stable machine-readable
/// identifier (for filtering and tests); Message is the human-readable explanation.
/// </summary>
public record RuleFlag(string Code, FlagSeverity Severity, string Message);

/// <summary>Stable flag codes. Keep these unchanged once used: stored assessments reference them.</summary>
public static class RuleCodes
{
    public const string MissingMonthlyIncome = "MISSING_MONTHLY_INCOME";
    public const string MissingExistingDebt = "MISSING_EXISTING_DEBT";
    public const string MissingEmploymentType = "MISSING_EMPLOYMENT_TYPE";
    public const string LoanAmountBelowMinimum = "LOAN_AMOUNT_BELOW_MIN";
    public const string LoanAmountAboveMaximum = "LOAN_AMOUNT_ABOVE_MAX";
    public const string TenureBelowMinimum = "TENURE_BELOW_MIN";
    public const string TenureAboveMaximum = "TENURE_ABOVE_MAX";
    public const string InterestRateOutOfRange = "INTEREST_RATE_OUT_OF_RANGE";
    public const string IncomeBelowMinimum = "INCOME_BELOW_MIN";
    public const string DebtToIncomeAboveMaximum = "DTI_ABOVE_MAX";
    public const string ExistingDebtExceedsIncome = "EXISTING_DEBT_EXCEEDS_INCOME";
    public const string UnemployedWithIncome = "UNEMPLOYED_WITH_INCOME";
    public const string EmiNotCalculable = "EMI_NOT_CALCULABLE";
}
