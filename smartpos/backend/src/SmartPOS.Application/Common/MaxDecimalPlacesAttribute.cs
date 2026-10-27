using System.ComponentModel.DataAnnotations;

namespace SmartPOS.Application.Common;

/// <summary>Rejects decimals with more fractional digits than the database column stores.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class MaxDecimalPlacesAttribute(int places) : ValidationAttribute
{
    public int Places { get; } = places;

    public override bool IsValid(object? value) =>
        value is not decimal d || decimal.Round(d, Places) == d;

    public override string FormatErrorMessage(string name) =>
        $"{name} must have at most {Places} decimal places.";
}
