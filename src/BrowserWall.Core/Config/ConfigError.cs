namespace BrowserWall.Core.Config;

/// <summary>One config file error: which field and what is wrong. The App builds the displayed line in the UI language.</summary>
public sealed record ConfigError(string Field, string Message)
{
    /// <summary>
    /// Field marker for errors that do not belong to a single field (for example, invalid JSON);
    /// replaced with the string table's <c>ConfigWholeFile</c> when displayed.
    /// </summary>
    public const string WholeFile = "*";
}
