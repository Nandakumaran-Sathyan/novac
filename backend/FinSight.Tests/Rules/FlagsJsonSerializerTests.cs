using FinSight.Application.DTOs;
using FinSight.Domain.Rules;

namespace FinSight.Tests.Rules;

public class FlagsJsonSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesFlags_AndStoresSeverityAsText()
    {
        RuleFlag[] flags =
        [
            new(RuleCodes.MissingMonthlyIncome, FlagSeverity.Critical, "Income missing."),
            new(RuleCodes.InterestRateOutOfRange, FlagSeverity.Warning, "Rate \"26%\" out of range.")
        ];

        var json = FlagsJsonSerializer.Serialize(flags);
        var restored = FlagsJsonSerializer.Deserialize(json);

        Assert.Contains("\"severity\":\"Critical\"", json); // readable in SQL, robust to enum reordering
        Assert.Equal(flags.Select(f => new RuleFlagDto(f.Code, f.Severity, f.Message)), restored);
    }

    [Fact]
    public void EmptyFlagList_SerializesToValidJsonArray()
    {
        Assert.Equal("[]", FlagsJsonSerializer.Serialize([]));
        Assert.Empty(FlagsJsonSerializer.Deserialize("[]"));
    }
}
