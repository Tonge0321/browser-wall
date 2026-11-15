using System.Globalization;
using System.IO;
using BrowserWall.Core.Config;
using BrowserWall.Core.Text;

namespace BrowserWall.App;

/// <summary>The real file system environment.</summary>
internal sealed class RealConfigEnvironment : IConfigEnvironment
{
    public string LocalAppData { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public bool FileExists(string path) => File.Exists(path);

    public string UiCultureName { get; } = CultureInfo.CurrentUICulture.Name;
}

/// <summary>Result of loading the config file.</summary>
internal sealed record ConfigLoadResult(string ConfigPath, bool CreatedDefault, ConfigParseResult Parse);

/// <summary>
/// Reads the config file and, when it does not exist, creates the default one. The only write location is %LOCALAPPDATA%\BrowserWall\.
/// </summary>
internal static class ConfigLoader
{
    public static string ConfigDirectory(IConfigEnvironment env) => Path.Combine(env.LocalAppData, "BrowserWall");

    public static string ConfigPath(IConfigEnvironment env) => Path.Combine(ConfigDirectory(env), "config.json");

    public static ConfigLoadResult Load(IConfigEnvironment env)
    {
        string path = ConfigPath(env);
        bool created = false;

        // Nothing has been parsed when creating or reading fails: messages use the auto language.
        UiLanguage autoLang = UiLanguageRule.Resolve(null, env.UiCultureName);

        if (!File.Exists(path))
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectory(env));
                byte[] content = ConfigDefaults.CreateBytes(env);

                // CreateNew: fails if the file appears in between, so the user's file is never overwritten.
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stream.Write(content, 0, content.Length);
                created = true;
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another process just created the file: read that one as usual.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new ConfigLoadResult(path, false, ConfigParseResult.Fail(
                    new[] { new ConfigError(ConfigError.WholeFile, ConfigFileFailure.Message(autoLang, true, ConfigFileFailure.Classify(ex))) },
                    autoLang));
            }
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ConfigLoadResult(path, created, ConfigParseResult.Fail(
                new[] { new ConfigError(ConfigError.WholeFile, ConfigFileFailure.Message(autoLang, false, ConfigFileFailure.Classify(ex))) },
                autoLang));
        }

        return new ConfigLoadResult(path, created, ConfigParser.Parse(bytes, env));
    }
}
