using BrowserWall.Core.Text;

namespace BrowserWall.Core.Config;

/// <summary>Validated config content.</summary>
public sealed record WallConfig(
    string BrowserPath,
    string ProfileDir,
    IReadOnlyList<string> Urls);

/// <summary>
/// Config parse result: <see cref="Config"/> on success, all errors on failure.
/// <see cref="Language"/> is the UI language decided at startup (success or failure); error messages are already in it.
/// </summary>
public sealed class ConfigParseResult
{
    private ConfigParseResult(WallConfig? config, IReadOnlyList<ConfigError> errors, UiLanguage language)
    {
        Config = config;
        Errors = errors;
        Language = language;
    }

    public WallConfig? Config { get; }

    public IReadOnlyList<ConfigError> Errors { get; }

    public UiLanguage Language { get; }

    public bool Success => Config is not null;

    public static ConfigParseResult Ok(WallConfig config, UiLanguage language) => new(config, Array.Empty<ConfigError>(), language);

    public static ConfigParseResult Fail(IReadOnlyList<ConfigError> errors, UiLanguage language)
    {
        if (errors.Count == 0)
        {
            throw new ArgumentException("A failed result needs at least one error.", nameof(errors));
        }

        return new ConfigParseResult(null, errors, language);
    }
}
