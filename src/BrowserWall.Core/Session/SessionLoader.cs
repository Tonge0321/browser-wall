using System.Text;
using System.Text.Json;
using BrowserWall.Core.Config;

namespace BrowserWall.Core.Session;

/// <summary>Where the startup URLs come from.</summary>
public enum SessionSource
{
    /// <summary>The last page list.</summary>
    Session,

    /// <summary>The list file does not exist; use the config file.</summary>
    ConfigMissing,

    /// <summary>The list is an empty array; use the config file.</summary>
    ConfigEmpty,

    /// <summary>The list cannot be read or is invalid; use the config file and tell the user.</summary>
    ConfigInvalid,
}

/// <summary>URLs to open at startup and where they come from.</summary>
public sealed record SessionLoadResult(IReadOnlyList<string> Urls, SessionSource Source)
{
    /// <summary>Whether to show a dialog saying the last page list could not be read and the config file is used instead.</summary>
    public bool ShowInvalidNotice => Source == SessionSource.ConfigInvalid;
}

/// <summary>Decides which URLs to open at startup. Read-only: never writes or deletes the list file.</summary>
public static class SessionLoader
{
    public const string FileName = "session.json";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>List file location: %LOCALAPPDATA%\BrowserWall\session.json.</summary>
    public static string FilePath(IConfigEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);
        return Path.Combine(env.LocalAppData, "BrowserWall", FileName);
    }

    public static SessionLoadResult Load(ISessionStore store, IReadOnlyList<string> configUrls)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(configUrls);

        SessionReadStatus status = store.Read(out byte[]? bytes);
        if (status == SessionReadStatus.Missing)
        {
            return new SessionLoadResult(configUrls, SessionSource.ConfigMissing);
        }

        if (status != SessionReadStatus.Read || bytes is null)
        {
            return new SessionLoadResult(configUrls, SessionSource.ConfigInvalid);
        }

        List<string>? urls = Parse(bytes);
        if (urls is null)
        {
            return new SessionLoadResult(configUrls, SessionSource.ConfigInvalid);
        }

        if (urls.Count == 0)
        {
            return new SessionLoadResult(configUrls, SessionSource.ConfigEmpty);
        }

        return new SessionLoadResult(urls, SessionSource.Session);
    }

    /// <summary>
    /// Parses the list content: UTF-8 (with or without BOM), a JSON array of 0 to <see cref="WallLimits.MaxCells"/> strings,
    /// each following <see cref="UrlRule"/>. Returns null when invalid.
    /// </summary>
    public static List<string>? Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

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
            return null;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(text);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() > WallLimits.MaxCells)
            {
                return null;
            }

            var urls = new List<string>();
            foreach (JsonElement item in root.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                string url = item.GetString()!;
                if (!UrlRule.IsAllowed(url))
                {
                    return null;
                }

                urls.Add(url);
            }

            return urls;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
