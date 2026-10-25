using FinSight.Domain.Entities;
using FinSight.Domain.Rules;

namespace FinSight.Tests.Rules;

public class LoanRuleEvaluatorTests
{
    private readonly LoanRuleEvaluator _evaluator = new(new LendingProductRules());

    /// <summary>A complete, in-policy application. Each test changes only what it is testing.</summary>
    private static LoanApplication CleanApplication() => new()
    {
        ApplicantName = "Test Applicant",
        MonthlyIncome = 95_000m,
        ExistingMonthlyDebt = 8_000m,
        LoanAmount = 600_000m,
        AnnualInterestRate = 10.75m,
        TenureMonths = 48,
        EmploymentType = EmploymentType.Salaried
    };

    private static IEnumerable<string> Codes(RuleEvaluationResult result) => result.Flags.Select(f => f.Code);

    [Fact]
    public void CleanApplication_HasNoFlags_AndCalculatesEmiAndDti()
    {
        var result = _evaluator.Evaluate(CleanApplication());

        Assert.Empty(result.Flags);
        Assert.Equal(15_434.57m, result.Emi);
        Assert.Equal(0.2467m, result.DebtToIncomeRatio); // (8,000 + 15,434.57) / 95,000
    }

    [Fact]
    public void MissingIncome_IsCritical_AndDtiIsNotCalculated()
    {
        var app = CleanApplication();
        app.MonthlyIncome = null;

        var result = _evaluator.Evaluate(app);

        var flag = Assert.Single(result.Flags);
        Assert.Equal(RuleCodes.MissingMonthlyIncome, flag.Code);
        Assert.Equal(FlagSeverity.Critical, flag.Severity);
        Assert.Null(result.DebtToIncomeRatio);
        Assert.NotNull(result.Emi); // EMI does not depend on income
    }

    [Fact]
    public void MissingExistingDebt_IsWarning_AndDtiIsNotGuessed()
    {
        var app = CleanApplication();
        app.ExistingMonthlyDebt = null;

        var result = _evaluator.Evaluate(app);

        Assert.Equal([RuleCodes.MissingExistingDebt], Codes(result));
        Assert.Equal(FlagSeverity.Warning, result.Flags[0].Severity);
        Assert.Null(result.DebtToIncomeRatio); // must not silently assume zero debt
    }

    [Fact]
    public void MissingEmploymentType_IsFlagged()
    {
        var app = CleanApplication();
        app.EmploymentType = null;

        Assert.Equal([RuleCodes.MissingEmploymentType], Codes(_evaluator.Evaluate(app)));
    }

    [Fact]
    public void HighDebtToIncome_IsCritical_AndMessageExplainsTheNumbers()
    {
        var app = CleanApplication();
        app.MonthlyIncome = 48_000m;
        app.ExistingMonthlyDebt = 18_000m;
        app.LoanAmount = 800_000m;
        app.AnnualInterestRate = 13m;
        app.TenureMonths = 36;

        var result = _evaluator.Evaluate(app);

        Assert.Equal(0.9366m, result.DebtToIncomeRatio); // (18,000 + 26,955.16) / 48,000
        var flag = Assert.Single(result.Flags);
        Assert.Equal(RuleCodes.DebtToIncomeAboveMaximum, flag.Code);
        Assert.Equal(FlagSeverity.Critical, flag.Severity);
        Assert.Contains("93.66%", flag.Message);
        Assert.Contains("50%", flag.Message);
    }

    [Fact]
    public void DebtToIncomeExactlyAtLimit_IsNotFlagged()
    {
        // EMI at 0% = 600,000 / 48 = 12,500. (37,500 + 12,500) / 100,000 = 0.50 exactly.
        var app = CleanApplication();
        app.MonthlyIncome = 100_000m;
        app.ExistingMonthlyDebt = 37_500m;
        app.AnnualInterestRate = 0m;

        var rules = new LendingProductRules { MinAnnualInterestRate = 0m };
        var result = new LoanRuleEvaluator(rules).Evaluate(app);

        Assert.Equal(0.5m, result.DebtToIncomeRatio);
        Assert.Empty(result.Flags);
    }

    [Theory]
    [InlineData(49_999.99, RuleCodes.LoanAmountBelowMinimum)]
    [InlineData(2_500_000.01, RuleCodes.LoanAmountAboveMaximum)]
    public void LoanAmountOutsideProductLimits_IsCritical(decimal amount, string expectedCode)
    {
        var app = CleanApplication();
        app.LoanAmount = amount;
        app.MonthlyIncome = 1_000_000m; // keep DTI in range so only the amount rule fires

        var result = _evaluator.Evaluate(app);

        var flag = Assert.Single(result.Flags);
        Assert.Equal(expectedCode, flag.Code);
        Assert.Equal(FlagSeverity.Critical, flag.Severity);
    }

    [Theory]
    [InlineData(50_000)]
    [InlineData(2_500_000)]
    public void LoanAmountAtProductLimits_IsAllowed(decimal amount)
    {
        var app = CleanApplication();
        app.LoanAmount = amount;
        app.MonthlyIncome = 1_000_000m;

        Assert.Empty(_evaluator.Evaluate(app).Flags);
    }

    [Theory]
    [InlineData(11, RuleCodes.TenureBelowMinimum)]
    [InlineData(85, RuleCodes.TenureAboveMaximum)]
    public void TenureOutsideProductLimits_IsCritical(int tenure, string expectedCode)
    {
        var app = CleanApplication();
        app.TenureMonths = tenure;
        app.MonthlyIncome = 1_000_000m;

        Assert.Equal([expectedCode], Codes(_evaluator.Evaluate(app)));
    }

    [Theory]
    [InlineData(9.49)]
    [InlineData(24.01)]
    public void InterestRateOutsideProductRange_IsWarning(decimal rate)
    {
        var app = CleanApplication();
        app.AnnualInterestRate = rate;

        var flag = Assert.Single(_evaluator.Evaluate(app).Flags);
        Assert.Equal(RuleCodes.InterestRateOutOfRange, flag.Code);
        Assert.Equal(FlagSeverity.Warning, flag.Severity);
    }

    [Fact]
    public void IncomeBelowMinimum_IsCritical()
    {
        var app = CleanApplication();
        app.MonthlyIncome = 18_000m;
        app.ExistingMonthlyDebt = 0m;
        app.LoanAmount = 50_000m;

        Assert.Contains(RuleCodes.IncomeBelowMinimum, Codes(_evaluator.Evaluate(app)));
    }

    [Fact]
    public void ExistingDebtAtOrAboveIncome_IsFlaggedAsInconsistent()
    {
        var app = CleanApplication();
        app.MonthlyIncome = 40_000m;
        app.ExistingMonthlyDebt = 42_000m;

        var codes = Codes(_evaluator.Evaluate(app)).ToList();

        Assert.Contains(RuleCodes.ExistingDebtExceedsIncome, codes);
        Assert.Contains(RuleCodes.DebtToIncomeAboveMaximum, codes);
    }

    [Fact]
    public void UnemployedApplicantDeclaringIncome_IsWarning()
    {
        var app = CleanApplication();
        app.EmploymentType = EmploymentType.Unemployed;

        var flag = Assert.Single(_evaluator.Evaluate(app).Flags);
        Assert.Equal(RuleCodes.UnemployedWithIncome, flag.Code);
        Assert.Equal(FlagSeverity.Warning, flag.Severity);
    }

    [Fact]
    public void ZeroIncome_DoesNotDivideByZero()
    {
        var app = CleanApplication();
        app.MonthlyIncome = 0m;
        app.ExistingMonthlyDebt = 0m;

        var result = _evaluator.Evaluate(app);

        Assert.Null(result.DebtToIncomeRatio);
        Assert.Contains(RuleCodes.IncomeBelowMinimum, Codes(result));
        Assert.Contains(RuleCodes.ExistingDebtExceedsIncome, Codes(result)); // 0 >= 0
    }

    [Fact]
    public void InvalidStoredInputs_ProduceFlag_InsteadOfThrowing()
    {
        // Intake validation blocks this, but legacy or imported rows might not have been validated.
        var app = CleanApplication();
        app.TenureMonths = 0;

        var result = _evaluator.Evaluate(app);

        Assert.Null(result.Emi);
        Assert.Null(result.DebtToIncomeRatio);
        Assert.Contains(RuleCodes.EmiNotCalculable, Codes(result));
        Assert.DoesNotContain("(Parameter", result.Flags.Single(f => f.Code == RuleCodes.EmiNotCalculable).Message);
    }

    [Fact]
    public void MultipleProblems_AreAllReported()
    {
        var app = CleanApplication();
        app.MonthlyIncome = null;
        app.EmploymentType = null;
        app.LoanAmount = 3_500_000m;
        app.TenureMonths = 120;

        var codes = Codes(_evaluator.Evaluate(app)).ToHashSet();

        Assert.Equal(
            new HashSet<string>
            {
                RuleCodes.MissingMonthlyIncome, RuleCodes.MissingEmploymentType,
                RuleCodes.LoanAmountAboveMaximum, RuleCodes.TenureAboveMaximum
            },
            codes);
    }

    [Fact]
    public void Rules_AreConfigurable()
    {
        var app = CleanApplication(); // DTI 24.67%
        var strict = new LoanRuleEvaluator(new LendingProductRules { MaxDebtToIncomeRatio = 0.20m, MaxLoanAmount = 500_000m });

        var codes = Codes(strict.Evaluate(app)).ToList();

        Assert.Contains(RuleCodes.DebtToIncomeAboveMaximum, codes);
        Assert.Contains(RuleCodes.LoanAmountAboveMaximum, codes);
    }

    [Fact]
    public void Evaluate_IsDeterministic()
    {
        var app = CleanApplication();
        app.MonthlyIncome = null;

        var first = _evaluator.Evaluate(app);
        var second = _evaluator.Evaluate(app);

        Assert.Equal(first.Emi, second.Emi);
        Assert.Equal(first.Flags, second.Flags); // records compare by value
    }
}
