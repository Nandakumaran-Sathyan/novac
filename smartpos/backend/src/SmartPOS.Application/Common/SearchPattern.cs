namespace SmartPOS.Application.Common;

public static class SearchPattern
{
    /// <summary>
    /// '!' rather than the usual '\': MySQL treats a backslash inside a string literal as an escape,
    /// so the provider's generated <c>ESCAPE '\'</c> clause is a syntax error on MySQL.
    /// </summary>
    public const string EscapeCharacter = "!";

    /// <summary>
    /// Builds a "contains" LIKE pattern, escaping the LIKE wildcards in user input. Use with
    /// <c>EF.Functions.Like(column, pattern, SearchPattern.EscapeCharacter)</c>.
    /// </summary>
    public static string Contains(string term)
    {
        var escaped = term.Trim()
            .Replace("!", "!!")
            .Replace("%", "!%")
            .Replace("_", "!_");
        return $"%{escaped}%";
    }
}
