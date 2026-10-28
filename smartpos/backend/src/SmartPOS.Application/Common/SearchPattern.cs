namespace SmartPOS.Application.Common;

public static class SearchPattern
{
    /// <summary>'!' rather than '\': it needs no special handling in any SQL string literal.</summary>
    public const string EscapeCharacter = "!";

    /// <summary>
    /// Builds a lower-case "contains" LIKE pattern, escaping the LIKE wildcards in user input. Searches
    /// are case-insensitive on every database (PostgreSQL's LIKE is case-sensitive), so use it against a
    /// lower-cased column: <c>EF.Functions.Like(column.ToLower(), pattern, SearchPattern.EscapeCharacter)</c>.
    /// </summary>
    public static string Contains(string term)
    {
        var escaped = term.Trim().ToLowerInvariant()
            .Replace("!", "!!")
            .Replace("%", "!%")
            .Replace("_", "!_");
        return $"%{escaped}%";
    }
}
