using BrowserWall.Core.Text;

namespace BrowserWall.Core.Config;

/// <summary>Why the config file could not be created or read.</summary>
public enum ConfigFileFailureKind
{
    /// <summary>No permission to access the file or its folder.</summary>
    NoAccess,

    /// <summary>The file cannot be read or written, for example because another program is using it or the disk failed.</summary>
    Unavailable,
}

/// <summary>
/// Fixed, localized explanations for config file create or read failures. The system's own error message is never shown.
/// </summary>
public static class ConfigFileFailure
{
    /// <summary>Access-denied errors are <see cref="ConfigFileFailureKind.NoAccess"/>; every other error is <see cref="ConfigFileFailureKind.Unavailable"/>.</summary>
    public static ConfigFileFailureKind Classify(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return ex is UnauthorizedAccessException ? ConfigFileFailureKind.NoAccess : ConfigFileFailureKind.Unavailable;
    }

    /// <summary>The full message: "could not create" or "could not read", followed by the fixed explanation for <paramref name="kind"/>.</summary>
    public static string Message(UiLanguage lang, bool creating, ConfigFileFailureKind kind)
    {
        string reason = UiStrings.Get(lang, kind == ConfigFileFailureKind.NoAccess ? UiTextKey.ConfigNoAccess : UiTextKey.ConfigFileBusy);
        return UiStrings.Format(lang, creating ? UiTextKey.ConfigCreateFailed : UiTextKey.ConfigReadFailed, reason);
    }
}
