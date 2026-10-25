namespace FinSight.Domain.Entities;

/// <summary>
/// An immutable snapshot of one deterministic rule evaluation. Each run of
/// POST /assess adds a new row, so the history shows how an application's
/// flags changed as data or product rules changed.
/// </summary>
public class RuleAssessment
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }

    /// <summary>Null when the EMI could not be calculated from the stored inputs.</summary>
    public decimal? Emi { get; set; }

    /// <summary>(existing debt + new EMI) / monthly income. Null when income or debt is unknown.</summary>
    public decimal? DebtToIncomeRatio { get; set; }

    /// <summary>Serialized list of <see cref="Rules.RuleFlag"/>.</summary>
    public required string FlagsJson { get; set; }

    public DateTime CreatedAt { get; set; }

    public LoanApplication? Application { get; set; }
}
