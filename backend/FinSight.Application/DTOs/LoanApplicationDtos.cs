using System.ComponentModel.DataAnnotations;
using FinSight.Application.Validators;
using FinSight.Domain.Entities;

namespace FinSight.Application.DTOs;

/// <summary>
/// Intake request. Validation here is structural: values must be well-formed and
/// physically sensible. Product-specific limits (e.g. a minimum loan of INR 50,000) are
/// deliberately NOT enforced here. They become assessment flags, so an out-of-policy
/// application is still recorded and visible to a reviewer.
///
/// Required numbers are declared nullable together with [Required]: a non-nullable decimal
/// would silently default to 0 when the field is omitted from the JSON.
/// </summary>
public class CreateLoanApplicationRequest
{
    [Required, StringLength(100, MinimumLength = 2)]
    [RegularExpression(@"^[\p{L}][\p{L} .'\-]*$", ErrorMessage = "ApplicantName may contain only letters, spaces, apostrophes, periods and hyphens.")]
    public string? ApplicantName { get; set; }

    [Range(typeof(decimal), "0", "100000000"), MaxDecimalPlaces(2)]
    public decimal? MonthlyIncome { get; set; }

    [Range(typeof(decimal), "0", "100000000"), MaxDecimalPlaces(2)]
    public decimal? ExistingMonthlyDebt { get; set; }

    [Required, Range(typeof(decimal), "1", "1000000000"), MaxDecimalPlaces(2)]
    public decimal? LoanAmount { get; set; }

    [Required, Range(typeof(decimal), "0", "100"), MaxDecimalPlaces(2)]
    public decimal? AnnualInterestRate { get; set; }

    [Required, Range(1, 600)]
    public int? TenureMonths { get; set; }

    public EmploymentType? EmploymentType { get; set; }
}

public class LoanApplicationQuery
{
    [StringLength(100)]
    public string? Search { get; set; }

    public LoanStatus? Status { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}

/// <summary>Row in the dashboard list. Review-flag fields are null until the application has been assessed.</summary>
public record LoanApplicationSummaryDto(
    int Id,
    string ApplicantName,
    decimal LoanAmount,
    decimal? MonthlyIncome,
    LoanStatus Status,
    DateTime CreatedAt,
    int? ReviewFlagCount,
    int? CriticalFlagCount);

public record LoanApplicationDetailDto(
    int Id,
    string ApplicantName,
    decimal? MonthlyIncome,
    decimal? ExistingMonthlyDebt,
    decimal LoanAmount,
    decimal AnnualInterestRate,
    int TenureMonths,
    EmploymentType? EmploymentType,
    LoanStatus Status,
    DateTime CreatedAt,
    AssessmentDto? LatestAssessment);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
