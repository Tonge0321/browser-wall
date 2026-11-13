using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BrowserWall.Core.Launch;

/// <summary>
/// "Always show full URLs": sets <c>omnibox.prevent_url_elisions = true</c> in <c>&lt;profileDir&gt;\Default\Preferences</c>,
/// keeping everything else as is. Only computes the content to write; does not touch files.
/// </summary>
public static class PreferencesPatch
{
    public const string Section = "omnibox";

    public const string Key = "prevent_url_elisions";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Returns the content to write (UTF-8 without BOM, not indented); null means do not write.
    /// File missing (<paramref name="existing"/> is null) → only this setting; not valid JSON, top level not an object, or
    /// <c>omnibox</c> not an object → do not write; already true → do not write; otherwise → set only this one, keep the rest.
    /// </summary>
    public static byte[]? Apply(byte[]? existing)
    {
        if (existing is null)
        {
            var fresh = new JsonObject { [Section] = new JsonObject { [Key] = true } };
            return Encoding.UTF8.GetBytes(fresh.ToJsonString(WriteOptions));
        }

        if (!TryParse(existing, out JsonNode? root) || root is not JsonObject obj)
        {
            return null;
        }

        JsonObject section;
        if (obj.TryGetPropertyValue(Section, out JsonNode? node))
        {
            if (node is not JsonObject existingSection)
            {
                return null;
            }

            section = existingSection;
            if (section.TryGetPropertyValue(Key, out JsonNode? value)
                && value is JsonValue v
                && v.TryGetValue(out bool already)
                && already)
            {
                return null;
            }
        }
        else
        {
            section = new JsonObject();
            obj[Section] = section;
        }

        section[Key] = true;
        return Encoding.UTF8.GetBytes(obj.ToJsonString(WriteOptions));
    }

    /// <summary>Whether the content is valid JSON (UTF-8, BOM allowed; same parsing as <see cref="Apply"/>). Condition for backing up the preferences.</summary>
    public static bool IsValidJson(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return TryParse(content, out _);
    }

    private static bool TryParse(byte[] content, out JsonNode? root)
    {
        try
        {
            string text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(content);
            if (text.Length > 0 && text[0] == '﻿')
            {
                text = text[1..];
            }

            root = JsonNode.Parse(text);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException or ArgumentException)
        {
            root = null;
            return false;
        }
    }
}
