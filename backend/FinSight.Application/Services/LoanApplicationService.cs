using FinSight.Application.DTOs;
using FinSight.Domain.Entities;
using FinSight.Domain.Rules;
using FinSight.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinSight.Application.Services;

public class LoanApplicationService(FinSightDbContext db, TimeProvider clock)
{
    public async Task<PagedResult<LoanApplicationSummaryDto>> ListAsync(LoanApplicationQuery query, CancellationToken ct)
    {
        var applications = db.LoanApplications.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // SQL Server's default collation is case-insensitive, so Contains → LIKE '%term%' is too.
            var term = query.Search.Trim();
            applications = applications.Where(a => a.ApplicantName.Contains(term));
        }

        if (query.Status is { } status)
            applications = applications.Where(a => a.Status == status);

        var totalCount = await applications.CountAsync(ct);

        // Project in SQL: only the columns the list needs, plus the latest assessment's flags.
        var rows = await applications
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(a => new
            {
                a.Id, a.ApplicantName, a.LoanAmount, a.MonthlyIncome, a.Status, a.CreatedAt,
                LatestFlagsJson = a.Assessments
                    .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
                    .Select(r => r.FlagsJson)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var items = rows.Select(r =>
        {
            var flags = r.LatestFlagsJson is null ? null : FlagsJsonSerializer.Deserialize(r.LatestFlagsJson);
            return new LoanApplicationSummaryDto(
                r.Id, r.ApplicantName, r.LoanAmount, r.MonthlyIncome, r.Status, r.CreatedAt,
                ReviewFlagCount: flags?.Count,
                CriticalFlagCount: flags?.Count(f => f.Severity == FlagSeverity.Critical));
        }).ToList();

        return new PagedResult<LoanApplicationSummaryDto>(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<LoanApplicationDetailDto?> GetAsync(int id, CancellationToken ct)
    {
        var application = await db.LoanApplications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (application is null)
            return null;

        var latest = await db.RuleAssessments.AsNoTracking()
            .Where(r => r.ApplicationId == id)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct);

        return ToDetail(application, latest);
    }

    /// <remarks>Assumes <paramref name="request"/> has passed DataAnnotations validation ([ApiController] does this).</remarks>
    public async Task<LoanApplicationDetailDto> CreateAsync(CreateLoanApplicationRequest request, CancellationToken ct)
    {
        var application = new LoanApplication
        {
            ApplicantName = request.ApplicantName!.Trim(),
            MonthlyIncome = request.MonthlyIncome,
            ExistingMonthlyDebt = request.ExistingMonthlyDebt,
            LoanAmount = request.LoanAmount!.Value,
            AnnualInterestRate = request.AnnualInterestRate!.Value,
            TenureMonths = request.TenureMonths!.Value,
            EmploymentType = request.EmploymentType,
            Status = LoanStatus.Submitted, // never client-controlled
            CreatedAt = clock.GetUtcNow().UtcDateTime
        };

        db.LoanApplications.Add(application);
        await db.SaveChangesAsync(ct);

        return ToDetail(application, latestAssessment: null);
    }

    private static LoanApplicationDetailDto ToDetail(LoanApplication a, RuleAssessment? latestAssessment) => new(
        a.Id, a.ApplicantName, a.MonthlyIncome, a.ExistingMonthlyDebt, a.LoanAmount, a.AnnualInterestRate,
        a.TenureMonths, a.EmploymentType, a.Status, a.CreatedAt,
        latestAssessment is null ? null : AssessmentDto.FromEntity(latestAssessment));
}
