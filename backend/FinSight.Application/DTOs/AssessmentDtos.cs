using System.Text.Json;
using System.Text.Json.Serialization;
using FinSight.Domain.Entities;
using FinSight.Domain.Rules;

namespace FinSight.Application.DTOs;

public record RuleFlagDto(string Code, FlagSeverity Severity, string Message);

public record AssessmentDto(
    int Id,
    int ApplicationId,
    decimal? Emi,
    decimal? DebtToIncomeRatio,
    IReadOnlyList<RuleFlagDto> Flags,
    DateTime CreatedAt)
{
    public static AssessmentDto FromEntity(RuleAssessment entity) => new(
        entity.Id,
        entity.ApplicationId,
        entity.Emi,
        entity.DebtToIncomeRatio,
        FlagsJsonSerializer.Deserialize(entity.FlagsJson),
        entity.CreatedAt);
}

/// <summary>Single place that defines how flags are stored in RuleAssessments.FlagsJson.</summary>
public static class FlagsJsonSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(IEnumerable<RuleFlag> flags) =>
        JsonSerializer.Serialize(flags.Select(f => new RuleFlagDto(f.Code, f.Severity, f.Message)), Options);

    public static IReadOnlyList<RuleFlagDto> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<RuleFlagDto>>(json, Options) ?? [];
}
