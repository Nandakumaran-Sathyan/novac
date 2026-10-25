using System.ComponentModel.DataAnnotations;

namespace FinSight.Application.Validators;

/// <summary>
/// Rejects decimals with more fractional digits than the database column stores.
/// Without this, SQL Server would silently round 1000.555 to 1000.56: a quiet data change.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MaxDecimalPlacesAttribute(int places) : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not decimal number)
            return ValidationResult.Success; // null is handled by [Required]

        return decimal.Round(number, places) == number
            ? ValidationResult.Success
            : new ValidationResult(
                $"{validationContext.DisplayName} must have at most {places} decimal places.",
                [validationContext.MemberName!]);
    }
}
