using System.ComponentModel.DataAnnotations;
using FinSight.Application.DTOs;
using FinSight.Domain.Rules;

namespace FinSight.Tests.Validation;

/// <summary>
/// Exercises the same DataAnnotations rules that [ApiController] runs before an action executes.
/// </summary>
public class CreateLoanApplicationRequestTests
{
    private static CreateLoanApplicationRequest ValidRequest() => new()
    {
        ApplicantName = "Test Applicant",
        MonthlyIncome = 85_000m,
        ExistingMonthlyDebt = 6_000m,
        LoanAmount = 500_000m,
        AnnualInterestRate = 11.25m,
        TenureMonths = 48
    };

    private static List<string> InvalidMembers(CreateLoanApplicationRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results.SelectMany(r => r.MemberNames).ToList();
    }

    [Fact]
    public void ValidRequest_PassesValidation()
    {
        Assert.Empty(InvalidMembers(ValidRequest()));
    }

    [Fact]
    public void OptionalFieldsMayBeOmitted()
    {
        var request = ValidRequest();
        request.MonthlyIncome = null;
        request.ExistingMonthlyDebt = null;
        request.EmploymentType = null;

        Assert.Empty(InvalidMembers(request));
    }

    [Fact]
    public void RequiredFieldsMissing_AreRejected()
    {
        var request = new CreateLoanApplicationRequest();

        var invalid = InvalidMembers(request);

        Assert.Contains(nameof(request.ApplicantName), invalid);
        Assert.Contains(nameof(request.LoanAmount), invalid);
        Assert.Contains(nameof(request.AnnualInterestRate), invalid);
        Assert.Contains(nameof(request.TenureMonths), invalid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("<script>")]
    [InlineData("Robert'); DROP TABLE--")]
    [InlineData(" Leading space")]
    public void InvalidApplicantName_IsRejected(string name)
    {
        var request = ValidRequest();
        request.ApplicantName = name;

        Assert.Contains(nameof(request.ApplicantName), InvalidMembers(request));
    }

    [Theory]
    [InlineData("Ananya Rao")]
    [InlineData("D'Souza-Pereira")]
    [InlineData("K. Subramanian")]
    [InlineData("Zoë Müller")]
    public void ValidApplicantName_IsAccepted(string name)
    {
        var request = ValidRequest();
        request.ApplicantName = name;

        Assert.Empty(InvalidMembers(request));
    }

    [Fact]
    public void NegativeAndOutOfRangeNumbers_AreRejected()
    {
        var request = ValidRequest();
        request.MonthlyIncome = -1m;
        request.ExistingMonthlyDebt = -1m;
        request.LoanAmount = 0m;
        request.AnnualInterestRate = 100.01m;
        request.TenureMonths = 601;

        var invalid = InvalidMembers(request);

        Assert.Equal(5, invalid.Distinct().Count());
    }

    [Fact]
    public void MoreThanTwoDecimalPlaces_IsRejected_InsteadOfSilentlyRounded()
    {
        var request = ValidRequest();
        request.MonthlyIncome = 1000.555m;
        request.AnnualInterestRate = 10.125m;

        var invalid = InvalidMembers(request);

        Assert.Contains(nameof(request.MonthlyIncome), invalid);
        Assert.Contains(nameof(request.AnnualInterestRate), invalid);
    }

    [Fact]
    public void ProductLimitsAreNotIntakeErrors()
    {
        // Above the product maximum, but structurally valid: it is accepted and flagged at assessment.
        var request = ValidRequest();
        request.LoanAmount = new LendingProductRules().MaxLoanAmount * 2;

        Assert.Empty(InvalidMembers(request));
    }
}
