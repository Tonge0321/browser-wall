using System.Text;
using System.Text.Json;

namespace BrowserWall.Core.Config;

/// <summary>Default content to create when the config file does not exist. Only produces text; the App writes the file.</summary>
public static class ConfigDefaults
{
    public const string DefaultUrl = "https://www.google.com";

    /// <summary>Browser executables to look for, in search order.</summary>
    public static IReadOnlyList<string> BrowserCandidates(IConfigEnvironment env) => new[]
    {
        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        Path.Combine(env.LocalAppData, "Google", "Chrome", "Application", "chrome.exe"),
        @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe",
    };

    /// <summary>The first browser executable that exists; an empty string when none exists.</summary>
    public static string FindBrowser(IConfigEnvironment env)
    {
        foreach (string candidate in BrowserCandidates(env))
        {
            if (env.FileExists(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    /// <summary>JSON text of the default config file (indented).</summary>
    public static string CreateJson(IConfigEnvironment env)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            writer.WriteStartObject();
            writer.WriteString(ConfigParser.FieldBrowserPath, FindBrowser(env));
            writer.WriteString(ConfigParser.FieldProfileDir, ConfigParser.DefaultProfileDir(env));
            writer.WriteStartArray(ConfigParser.FieldUrls);
            writer.WriteStringValue(DefaultUrl);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Bytes of the default config file: UTF-8 without BOM.</summary>
    public static byte[] CreateBytes(IConfigEnvironment env) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(CreateJson(env));
}
