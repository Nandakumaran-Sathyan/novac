using FinSight.Application.DTOs;
using FinSight.Domain.Entities;
using FinSight.Domain.Rules;
using FinSight.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinSight.Application.Services;

/// <summary>
/// Orchestrates a deterministic assessment: load → evaluate (pure domain logic) → persist.
/// Keeping the evaluator free of EF Core means the rules are unit-tested without a database.
/// </summary>
public class AssessmentService(FinSightDbContext db, LoanRuleEvaluator evaluator, TimeProvider clock)
{
    /// <returns>The stored assessment, or null when the application does not exist.</returns>
    public async Task<AssessmentDto?> AssessAsync(int applicationId, CancellationToken ct)
    {
        var application = await db.LoanApplications.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (application is null)
            return null;

        var result = evaluator.Evaluate(application);

        var assessment = new RuleAssessment
        {
            ApplicationId = application.Id,
            Emi = result.Emi,
            DebtToIncomeRatio = result.DebtToIncomeRatio,
            FlagsJson = FlagsJsonSerializer.Serialize(result.Flags),
            CreatedAt = clock.GetUtcNow().UtcDateTime
        };

        db.RuleAssessments.Add(assessment);
        await db.SaveChangesAsync(ct);

        return AssessmentDto.FromEntity(assessment);
    }
}
