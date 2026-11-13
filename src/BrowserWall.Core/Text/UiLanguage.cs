namespace BrowserWall.Core.Text;

/// <summary>Language of the text shown to the user.</summary>
public enum UiLanguage
{
    ZhTw,
    En,
}

/// <summary>
/// How the UI language is decided: <c>"zh-TW"</c> and <c>"en"</c> force a language;
/// omitted or <c>"auto"</c> follows the Windows display language (names starting with <c>zh</c> → Traditional Chinese, otherwise English).
/// Decided once at startup.
/// </summary>
public static class UiLanguageRule
{
    /// <summary>Config field name.</summary>
    public const string FieldLanguage = "language";

    public const string Auto = "auto";
    public const string ZhTw = "zh-TW";
    public const string En = "en";

    /// <summary>
    /// <paramref name="setting"/>: config value (null when omitted); <paramref name="uiCultureName"/>: <c>CultureInfo.CurrentUICulture.Name</c>.
    /// Any other <paramref name="setting"/> is treated as auto.
    /// </summary>
    public static UiLanguage Resolve(string? setting, string? uiCultureName)
    {
        if (setting == ZhTw)
        {
            return UiLanguage.ZhTw;
        }

        if (setting == En)
        {
            return UiLanguage.En;
        }

        return uiCultureName is not null && uiCultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? UiLanguage.ZhTw
            : UiLanguage.En;
    }

    /// <summary>Only the three values, compared exactly (case-sensitive).</summary>
    public static bool IsValidSetting(string value) =>
        value == Auto || value == ZhTw || value == En;
}
