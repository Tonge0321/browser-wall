using BrowserWall.Core.Text;

namespace BrowserWall.Core.Config;

/// <summary>
/// URL rule: starts with <c>http://</c> or <c>https://</c> (letters case-insensitive).
/// Shared by the config file, the last page list and the last seen URL (<see cref="Session.LastSeenUrl"/>).
/// URLs are passed to the browser as separate arguments (<see cref="Launch.BrowserArguments"/>), so spaces or quotes
/// cannot break the launch arguments.
/// </summary>
public static class UrlRule
{
    public const string Http = "http://";
    public const string Https = "https://";

    /// <summary>Whether <paramref name="url"/> follows the rule.</summary>
    public static bool IsAllowed(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return url.StartsWith(Http, StringComparison.OrdinalIgnoreCase)
            || url.StartsWith(Https, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns null when valid; otherwise a plain explanation in <paramref name="lang"/>.</summary>
    public static string? Check(string url, UiLanguage lang) =>
        IsAllowed(url) ? null : UiStrings.Format(lang, UiTextKey.UrlMustStartWithHttp, url);
}
