namespace FinSight.Domain.Entities;

public enum EmploymentType
{
    Salaried,
    SelfEmployed,
    Retired,
    Unemployed
}

/// <summary>
/// Workflow status. Approved/Rejected are only ever set by a human reviewer,
/// never by the rule engine or the AI analysis.
/// </summary>
public enum LoanStatus
{
    Submitted,
    UnderReview,
    Approved,
    Rejected,
    Withdrawn
}
