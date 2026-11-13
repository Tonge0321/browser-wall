using System.Text;
using System.Text.Json;
using BrowserWall.Core.Text;

namespace BrowserWall.Core.Config;

/// <summary>
/// Parses and validates the config file. Only reads the content; never writes any file.
/// </summary>
public static class ConfigParser
{
    public const string FieldBrowserPath = "browserPath";
    public const string FieldProfileDir = "profileDir";
    public const string FieldUrls = "urls";

    public const int MinUrls = 1;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Default browser user data folder: %LOCALAPPDATA%\BrowserWall\profile.</summary>
    public static string DefaultProfileDir(IConfigEnvironment env) =>
        Path.Combine(env.LocalAppData, "BrowserWall", "profile");

    /// <summary>Parses the raw bytes of the config file (with or without a UTF-8 BOM).</summary>
    public static ConfigParseResult Parse(byte[] bytes, IConfigEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(env);

        // Whole-file errors happen before "language" can be read: their language is auto.
        UiLanguage fileLang = UiLanguageRule.Resolve(null, env.UiCultureName);

        ReadOnlySpan<byte> span = bytes;
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
        {
            span = span[3..];
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(span);
        }
        catch (DecoderFallbackException)
        {
            return Fail(ConfigError.WholeFile, UiStrings.Get(fileLang, UiTextKey.ConfigNotUtf8), fileLang);
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            string where = ex.LineNumber is long line ? UiStrings.Format(fileLang, UiTextKey.ConfigJsonLine, line + 1) : string.Empty;
            return Fail(ConfigError.WholeFile, UiStrings.Format(fileLang, UiTextKey.ConfigNotJson, where), fileLang);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Fail(ConfigError.WholeFile, UiStrings.Get(fileLang, UiTextKey.ConfigRootNotObject), fileLang);
            }

            return Validate(doc.RootElement, env);
        }
    }

    private static ConfigParseResult Validate(JsonElement root, IConfigEnvironment env)
    {
        var errors = new List<ConfigError>();

        UiLanguage lang = ReadLanguage(root, env, errors);
        string? browserPath = ReadBrowserPath(root, env, errors, lang);
        string? profileDir = ReadProfileDir(root, env, errors, lang);
        List<string>? urls = ReadUrls(root, errors, lang);

        // The old hotkeyShrink field is no longer used: if present it is ignored, not an error.
        if (errors.Count > 0)
        {
            return ConfigParseResult.Fail(errors, lang);
        }

        return ConfigParseResult.Ok(new WallConfig(browserPath!, profileDir!, urls!), lang);
    }

    /// <summary>
    /// Reads "language" first: omitted → auto; one of the three valid strings → that value; anything else
    /// (other strings, other JSON types) → one error on the "language" field, and all messages use auto.
    /// </summary>
    private static UiLanguage ReadLanguage(JsonElement root, IConfigEnvironment env, List<ConfigError> errors)
    {
        UiLanguage auto = UiLanguageRule.Resolve(null, env.UiCultureName);
        if (!root.TryGetProperty(UiLanguageRule.FieldLanguage, out JsonElement el))
        {
            return auto;
        }

        if (el.ValueKind == JsonValueKind.String && el.GetString() is string value && UiLanguageRule.IsValidSetting(value))
        {
            return UiLanguageRule.Resolve(value, env.UiCultureName);
        }

        errors.Add(new ConfigError(UiLanguageRule.FieldLanguage, UiStrings.Get(auto, UiTextKey.ConfigLanguageInvalid)));
        return auto;
    }

    private static string? ReadBrowserPath(JsonElement root, IConfigEnvironment env, List<ConfigError> errors, UiLanguage lang)
    {
        if (!root.TryGetProperty(FieldBrowserPath, out JsonElement el))
        {
            errors.Add(new ConfigError(FieldBrowserPath, UiStrings.Get(lang, UiTextKey.ConfigMissingBrowserPath)));
            return null;
        }

        if (el.ValueKind != JsonValueKind.String)
        {
            errors.Add(new ConfigError(FieldBrowserPath, UiStrings.Get(lang, UiTextKey.ConfigMustBeString)));
            return null;
        }

        string path = el.GetString()!;
        if (path.Length == 0)
        {
            errors.Add(new ConfigError(FieldBrowserPath, UiStrings.Get(lang, UiTextKey.ConfigEmptyBrowserPath)));
            return null;
        }

        if (!IsFullyQualified(path))
        {
            errors.Add(new ConfigError(FieldBrowserPath, UiStrings.Format(lang, UiTextKey.ConfigMustBeAbsolute, path)));
            return null;
        }

        if (!env.FileExists(path))
        {
            errors.Add(new ConfigError(FieldBrowserPath, UiStrings.Format(lang, UiTextKey.ConfigExeNotFound, path)));
            return null;
        }

        return path;
    }

    private static string? ReadProfileDir(JsonElement root, IConfigEnvironment env, List<ConfigError> errors, UiLanguage lang)
    {
        if (!root.TryGetProperty(FieldProfileDir, out JsonElement el))
        {
            return DefaultProfileDir(env);
        }

        if (el.ValueKind != JsonValueKind.String)
        {
            errors.Add(new ConfigError(FieldProfileDir, UiStrings.Get(lang, UiTextKey.ConfigMustBeString)));
            return null;
        }

        string path = el.GetString()!;
        if (!IsFullyQualified(path))
        {
            errors.Add(new ConfigError(FieldProfileDir, UiStrings.Format(lang, UiTextKey.ConfigMustBeAbsolute, path)));
            return null;
        }

        if (path.Contains('"'))
        {
            errors.Add(new ConfigError(FieldProfileDir, UiStrings.Get(lang, UiTextKey.ConfigNoDoubleQuote)));
            return null;
        }

        return path;
    }

    private static List<string>? ReadUrls(JsonElement root, List<ConfigError> errors, UiLanguage lang)
    {
        if (!root.TryGetProperty(FieldUrls, out JsonElement el))
        {
            errors.Add(new ConfigError(FieldUrls, UiStrings.Get(lang, UiTextKey.ConfigMissingUrls)));
            return null;
        }

        if (el.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new ConfigError(FieldUrls, UiStrings.Get(lang, UiTextKey.ConfigUrlsMustBeArray)));
            return null;
        }

        int count = el.GetArrayLength();
        if (count < MinUrls || count > WallLimits.MaxCells)
        {
            errors.Add(new ConfigError(FieldUrls, UiStrings.Format(lang, UiTextKey.ConfigUrlCount, count, MinUrls, WallLimits.MaxCells)));
            return null;
        }

        var urls = new List<string>(count);
        bool ok = true;
        int index = 0;
        foreach (JsonElement item in el.EnumerateArray())
        {
            string field = $"{FieldUrls}[{index}]";
            if (item.ValueKind != JsonValueKind.String)
            {
                errors.Add(new ConfigError(field, UiStrings.Get(lang, UiTextKey.ConfigMustBeString)));
                ok = false;
            }
            else
            {
                string url = item.GetString()!;
                string? problem = UrlRule.Check(url, lang);
                if (problem is not null)
                {
                    errors.Add(new ConfigError(field, problem));
                    ok = false;
                }
                else
                {
                    urls.Add(url);
                }
            }

            index++;
        }

        return ok ? urls : null;
    }

    private static bool IsFullyQualified(string path) =>
        path.Length > 0 && Path.IsPathFullyQualified(path);

    private static ConfigParseResult Fail(string field, string message, UiLanguage lang) =>
        ConfigParseResult.Fail(new[] { new ConfigError(field, message) }, lang);
}
