using BrowserWall.Core.Config;
using BrowserWall.Core.State;

namespace BrowserWall.Core.Session;

/// <summary>
/// Rules for recording the last seen URL. Every <see cref="RecordInterval"/> the App reads the address bar of each managed
/// window and passes the value to <see cref="Record"/>; when a new URL is recorded it returns true and the App then calls
/// <see cref="SessionTracker.Sync"/>. Does not read the clock: the App passes in the (monotonic) time.
/// </summary>
public sealed class LastSeenUrl
{
    /// <summary>Interval for reading the address bar; also the minimum interval between URL changes of the same cell.</summary>
    public static readonly TimeSpan RecordInterval = TimeSpan.FromSeconds(2);

    private const string SchemeSeparator = "://";

    private readonly Dictionary<long, TimeSpan> _changedAt = new();

    /// <summary>
    /// Value read from the address bar → URL to record; null means do not record this time (keep the old value).
    /// Focused (the user is typing), unreadable, empty (new tab), other schemes such as <c>chrome://</c>, or not URL-like → null;
    /// starts with http/https → as is; no <c>://</c> and no whitespace → prepend <c>https://</c> (fallback when the
    /// "always show full URLs" preference did not take effect).
    /// </summary>
    public static string? ToRecord(string? value, bool addressBarFocused)
    {
        if (addressBarFocused || value is null)
        {
            return null;
        }

        string text = value.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (UrlRule.IsAllowed(text))
        {
            return text;
        }

        if (text.Contains(SchemeSeparator, StringComparison.Ordinal)
            || HasOtherScheme(text)
            || text.Any(char.IsWhiteSpace))
        {
            return null;
        }

        return UrlRule.Https + text;
    }

    /// <summary>
    /// Records the value read for one cell. URL to record is null, not a managed window, wall closing, same as now, or this
    /// cell changed its URL less than <see cref="RecordInterval"/> ago → false (try again next round); otherwise updates the
    /// cell URL and returns true.
    /// </summary>
    public bool Record(WallState state, long windowId, string? value, bool addressBarFocused, TimeSpan now)
    {
        ArgumentNullException.ThrowIfNull(state);

        var managed = new HashSet<long>(state.WindowIds);
        foreach (long gone in _changedAt.Keys.Where(id => !managed.Contains(id)).ToList())
        {
            _changedAt.Remove(gone);
        }

        string? url = ToRecord(value, addressBarFocused);
        if (url is null || state.IsExiting || !managed.Contains(windowId))
        {
            return false;
        }

        WallSlot slot = state.Slots.First(s => s.Kind == WallSlotKind.Window && s.WindowId == windowId);
        if (string.Equals(slot.Url, url, StringComparison.Ordinal))
        {
            return false;
        }

        if (_changedAt.TryGetValue(windowId, out TimeSpan last) && now - last < RecordInterval)
        {
            return false;
        }

        if (!state.SetWindowUrl(windowId, url))
        {
            return false;
        }

        _changedAt[windowId] = now;
        return true;
    }

    /// <summary>
    /// Starts with "letters + colon + non-digit" (for example <c>about:blank</c>, <c>data:</c>, <c>javascript:</c>): another
    /// scheme, not a URL with its scheme omitted. A digit after the colon (for example the port in <c>localhost:8080</c>) does not count.
    /// </summary>
    private static bool HasOtherScheme(string text)
    {
        int colon = text.IndexOf(':');
        if (colon <= 0)
        {
            return false;
        }

        for (int i = 0; i < colon; i++)
        {
            char ch = text[i];
            if (!((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z')))
            {
                return false;
            }
        }

        return colon + 1 >= text.Length || !char.IsAsciiDigit(text[colon + 1]);
    }
}
