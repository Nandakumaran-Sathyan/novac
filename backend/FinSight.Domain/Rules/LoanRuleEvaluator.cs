using System.Globalization;
using FinSight.Domain.Entities;

namespace FinSight.Domain.Rules;

public record RuleEvaluationResult(decimal? Emi, decimal? DebtToIncomeRatio, IReadOnlyList<RuleFlag> Flags);

/// <summary>
/// Deterministic, side-effect-free rule evaluation: the same application and the same
/// rules always produce the same result. It produces flags for a human reviewer and
/// deliberately never returns an approve/reject decision.
/// </summary>
public class LoanRuleEvaluator(LendingProductRules rules)
{
    private static readonly CultureInfo Fmt = CultureInfo.InvariantCulture;

    public RuleEvaluationResult Evaluate(LoanApplication application)
    {
        var flags = new List<RuleFlag>();

        CheckMissingInformation(application, flags);
        CheckProductLimits(application, flags);
        var emi = TryCalculateEmi(application, flags);
        CheckIncomeConsistency(application, flags);
        var dti = CalculateDebtToIncome(application, emi, flags);

        return new RuleEvaluationResult(emi, dti, flags);
    }

    private static void CheckMissingInformation(LoanApplication app, List<RuleFlag> flags)
    {
        if (app.MonthlyIncome is null)
            flags.Add(new(RuleCodes.MissingMonthlyIncome, FlagSeverity.Critical,
                "Monthly income is missing, so affordability (debt-to-income) cannot be assessed."));

        if (app.ExistingMonthlyDebt is null)
            flags.Add(new(RuleCodes.MissingExistingDebt, FlagSeverity.Warning,
                "Existing monthly debt is not declared. Confirm with the applicant (enter 0 if none); debt-to-income was not calculated."));

        if (app.EmploymentType is null)
            flags.Add(new(RuleCodes.MissingEmploymentType, FlagSeverity.Warning,
                "Employment type is missing."));
    }

    private void CheckProductLimits(LoanApplication app, List<RuleFlag> flags)
    {
        if (app.LoanAmount < rules.MinLoanAmount)
            flags.Add(new(RuleCodes.LoanAmountBelowMinimum, FlagSeverity.Critical,
                $"Requested amount INR {Money(app.LoanAmount)} is below the product minimum of INR {Money(rules.MinLoanAmount)}."));
        else if (app.LoanAmount > rules.MaxLoanAmount)
            flags.Add(new(RuleCodes.LoanAmountAboveMaximum, FlagSeverity.Critical,
                $"Requested amount INR {Money(app.LoanAmount)} exceeds the product maximum of INR {Money(rules.MaxLoanAmount)}."));

        if (app.TenureMonths < rules.MinTenureMonths)
            flags.Add(new(RuleCodes.TenureBelowMinimum, FlagSeverity.Critical,
                $"Tenure of {app.TenureMonths} months is below the product minimum of {rules.MinTenureMonths} months."));
        else if (app.TenureMonths > rules.MaxTenureMonths)
            flags.Add(new(RuleCodes.TenureAboveMaximum, FlagSeverity.Critical,
                $"Tenure of {app.TenureMonths} months exceeds the product maximum of {rules.MaxTenureMonths} months."));

        if (app.AnnualInterestRate < rules.MinAnnualInterestRate || app.AnnualInterestRate > rules.MaxAnnualInterestRate)
            flags.Add(new(RuleCodes.InterestRateOutOfRange, FlagSeverity.Warning,
                $"Interest rate {app.AnnualInterestRate.ToString("0.##", Fmt)}% is outside the product range of " +
                $"{rules.MinAnnualInterestRate.ToString("0.##", Fmt)}%–{rules.MaxAnnualInterestRate.ToString("0.##", Fmt)}%."));
    }

    private static decimal? TryCalculateEmi(LoanApplication app, List<RuleFlag> flags)
    {
        try
        {
            return EmiCalculator.Calculate(app.LoanAmount, app.AnnualInterestRate, app.TenureMonths);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            // Intake validation should prevent this, but stored or imported data may still be bad.
            flags.Add(new(RuleCodes.EmiNotCalculable, FlagSeverity.Critical,
                $"EMI could not be calculated: {ex.Message.Split(" (Parameter")[0]}"));
            return null;
        }
    }

    private void CheckIncomeConsistency(LoanApplication app, List<RuleFlag> flags)
    {
        if (app.MonthlyIncome is not { } income)
            return;

        if (income < rules.MinMonthlyIncome)
            flags.Add(new(RuleCodes.IncomeBelowMinimum, FlagSeverity.Critical,
                $"Monthly income INR {Money(income)} is below the product minimum of INR {Money(rules.MinMonthlyIncome)}."));

        if (app.ExistingMonthlyDebt is { } debt && debt >= income)
            flags.Add(new(RuleCodes.ExistingDebtExceedsIncome, FlagSeverity.Critical,
                $"Declared existing monthly debt (INR {Money(debt)}) is equal to or greater than monthly income (INR {Money(income)}). Verify both figures."));

        if (app.EmploymentType == EmploymentType.Unemployed && income > 0)
            flags.Add(new(RuleCodes.UnemployedWithIncome, FlagSeverity.Warning,
                "Applicant is marked as unemployed but declares a monthly income. Verify the source of income."));
    }

    /// <summary>
    /// Post-loan DTI (similar to FOIR in Indian lending): (existing EMIs + new EMI) / gross monthly income.
    /// Returned as a ratio rounded to 4 decimal places (0.4321 = 43.21%).
    /// </summary>
    private decimal? CalculateDebtToIncome(LoanApplication app, decimal? emi, List<RuleFlag> flags)
    {
        if (emi is null || app.MonthlyIncome is not { } income || income <= 0 || app.ExistingMonthlyDebt is not { } debt)
            return null;

        var dti = Math.Round((debt + emi.Value) / income, 4, MidpointRounding.AwayFromZero);

        if (dti > rules.MaxDebtToIncomeRatio)
            flags.Add(new(RuleCodes.DebtToIncomeAboveMaximum, FlagSeverity.Critical,
                $"Debt-to-income after this loan would be {Percent(dti)}, above the product limit of {Percent(rules.MaxDebtToIncomeRatio)} " +
                $"(existing debt INR {Money(debt)} + new EMI INR {Money(emi.Value)} on income INR {Money(income)})."));

        return dti;
    }

    private static string Money(decimal value) => value.ToString("#,##0.##", Fmt);
    private static string Percent(decimal ratio) => (ratio * 100m).ToString("0.##", Fmt) + "%";
}
