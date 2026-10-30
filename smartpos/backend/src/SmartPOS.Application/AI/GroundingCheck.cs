using System.Globalization;
using System.Text.RegularExpressions;

namespace SmartPOS.Application.AI;

/// <summary>
/// Catches invented figures. Every number in the model's answer must appear in a trusted source for this
/// question: the tool results, the server's own prompt, or the user's question. Numbers from the conversation
/// history do NOT count: the browser sends the history, so it could carry forged figures.
/// <para>
/// Matching is lenient about formatting (1,23,456 and 123456; 47538.25 written as 47,538.25 or rounded to 47,538)
/// but not about values. Small whole numbers (up to <see cref="SmallNumberLimit"/>) are allowed anywhere because
/// they are mostly list numbering ("1.", "2.") or wording ("top 5").
/// </para>
/// </summary>
public static partial class GroundingCheck
{
    public const int SmallNumberLimit = 10;

    [GeneratedRegex(@"\d[\d,]*(?:\.\d+)?")]
    private static partial Regex NumberPattern();

    /// <summary>The numbers in <paramref name="answer"/> that no trusted source supports, as written in the answer.</summary>
    public static IReadOnlyList<string> FindUnsupportedNumbers(string answer, IEnumerable<string> trustedSources)
    {
        var known = new HashSet<decimal>();
        foreach (var source in trustedSources)
            foreach (var value in Numbers(source))
            {
                known.Add(value);
                known.Add(Math.Round(value, 2, MidpointRounding.AwayFromZero));
                known.Add(Math.Round(value, 0, MidpointRounding.AwayFromZero)); // "₹47,538" for 47538.25
                known.Add(Math.Truncate(value));
            }

        var unsupported = new List<string>();
        foreach (Match match in NumberPattern().Matches(answer))
        {
            if (!TryParse(match.Value, out var value))
                continue;
            if (value == Math.Truncate(value) && value <= SmallNumberLimit)
                continue;
            if (!known.Contains(value) && !known.Contains(Math.Round(value, 2, MidpointRounding.AwayFromZero)))
                unsupported.Add(match.Value.TrimEnd(',', '.'));
        }
        return unsupported.Distinct().ToList();
    }

    private static IEnumerable<decimal> Numbers(string text)
    {
        foreach (Match match in NumberPattern().Matches(text))
            if (TryParse(match.Value, out var value))
                yield return value;
    }

    /// <summary>Reads "1,23,456.50", "47,538", "2026" and the like. A trailing comma or period is punctuation.</summary>
    private static bool TryParse(string raw, out decimal value) =>
        decimal.TryParse(raw.TrimEnd(',', '.').Replace(",", ""), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
}
