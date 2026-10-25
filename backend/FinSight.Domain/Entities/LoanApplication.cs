namespace FinSight.Domain.Entities;

/// <summary>
/// A (synthetic) loan application as captured at intake.
/// MonthlyIncome, ExistingMonthlyDebt and EmploymentType are deliberately optional:
/// real intake data is often incomplete, and the rule engine flags what is missing
/// instead of the API refusing to record the application.
/// </summary>
public class LoanApplication
{
    public int Id { get; set; }
    public required string ApplicantName { get; set; }
    public decimal? MonthlyIncome { get; set; }
    public decimal? ExistingMonthlyDebt { get; set; }
    public decimal LoanAmount { get; set; }

    /// <summary>Nominal annual interest rate in percent, e.g. 10.50 means 10.5% p.a.</summary>
    public decimal AnnualInterestRate { get; set; }

    public int TenureMonths { get; set; }
    public EmploymentType? EmploymentType { get; set; }
    public LoanStatus Status { get; set; } = LoanStatus.Submitted;
    public DateTime CreatedAt { get; set; }

    public List<RuleAssessment> Assessments { get; set; } = [];
}
