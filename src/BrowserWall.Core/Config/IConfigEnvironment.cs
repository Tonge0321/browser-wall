namespace BrowserWall.Core.Config;

/// <summary>
/// External information needed to validate the config file and create the default one. The App provides the real
/// file system; tests provide a fake.
/// </summary>
public interface IConfigEnvironment
{
    /// <summary>Absolute path of %LOCALAPPDATA%.</summary>
    string LocalAppData { get; }

    /// <summary>Whether a file exists at the given path.</summary>
    bool FileExists(string path);

    /// <summary>Windows display language name (<c>CultureInfo.CurrentUICulture.Name</c>), used when <c>language</c> is omitted or <c>"auto"</c>.</summary>
    string UiCultureName { get; }
}
