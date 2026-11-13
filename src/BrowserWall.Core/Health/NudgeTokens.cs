namespace BrowserWall.Core.Health;

/// <summary>
/// A token for each nudge: issued when the nudge is decided; sending, restoring and rechecking all carry it,
/// and each checks at run time that it is still the same nudge, giving up otherwise. The recheck has its own deadline: results returning after it are ignored (no state update).
/// Stores data only; does not read the clock or touch windows; safe to use from multiple threads (locks internally).
/// </summary>
public sealed class NudgeTokens
{
    private readonly object _lock = new();
    private readonly Dictionary<long, Entry> _entries = new();
    private long _serial;

    /// <summary>A nudge was decided: issues a new token (the window's old one is voided).</summary>
    public long Issue(long windowId, Rgb color)
    {
        lock (_lock)
        {
            long token = ++_serial;
            _entries[windowId] = new Entry(token, color);
            return token;
        }
    }

    public bool IsCurrent(long windowId, long token)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(windowId, out Entry? e) && e.Token == token;
        }
    }

    /// <summary>The nudge was actually sent; false if it is not the same nudge.</summary>
    public bool MarkSent(long windowId, long token)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(windowId, out Entry? e) || e.Token != token)
            {
                return false;
            }

            e.Sent = true;
            return true;
        }
    }

    /// <summary>The nudge was not sent (wall just minimized, window just popped out, etc.): voids it; returns whether the same nudge was voided.</summary>
    public bool Cancel(long windowId, long token)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(windowId, out Entry? e) && e.Token == token && !e.Sent)
            {
                _entries.Remove(windowId);
                return true;
            }

            return false;
        }
    }

    /// <summary>Token of the nudge that was sent (for restore and recheck); null if none.</summary>
    public long? SentToken(long windowId)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(windowId, out Entry? e) && e.Sent ? e.Token : null;
        }
    }

    public Rgb ColorOf(long windowId)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(windowId, out Entry? e) ? e.Color : default;
        }
    }

    /// <summary>Starts the recheck: records its deadline (only for the same nudge, once sent).</summary>
    public bool BeginRecheck(long windowId, long token, TimeSpan deadline)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(windowId, out Entry? e) || e.Token != token || !e.Sent)
            {
                return false;
            }

            e.RecheckDeadline = deadline;
            return true;
        }
    }

    /// <summary>
    /// Call when a recheck has a result, before updating state: accepted (and the nudge ends) only if it is the same nudge, the recheck has started, and <paramref name="now"/> is before the deadline;
    /// otherwise (voided on timeout, late, or replaced) returns false and the caller must not update state or log a result.
    /// </summary>
    public bool TryFinishRecheck(long windowId, long token, TimeSpan now)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(windowId, out Entry? e)
                && e.Token == token
                && e.RecheckDeadline is TimeSpan deadline
                && now < deadline)
            {
                _entries.Remove(windowId);
                return true;
            }

            return false;
        }
    }

    /// <summary>The recheck timed out or could not run: voids the nudge; returns whether the same nudge was voided (so the caller logs "not checked" only once).</summary>
    public bool TryExpireRecheck(long windowId, long token)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(windowId, out Entry? e) && e.Token == token)
            {
                _entries.Remove(windowId);
                return true;
            }

            return false;
        }
    }

    /// <summary>The flat episode ended (recovered to not flat): voids the window's nudge.</summary>
    public void EndEpisode(long windowId)
    {
        lock (_lock)
        {
            _entries.Remove(windowId);
        }
    }

    /// <summary>Keeps entries only for windows that are still managed.</summary>
    public void Retain(IEnumerable<long> windowIds)
    {
        ArgumentNullException.ThrowIfNull(windowIds);
        var keep = new HashSet<long>(windowIds);
        lock (_lock)
        {
            foreach (long id in _entries.Keys.Where(id => !keep.Contains(id)).ToList())
            {
                _entries.Remove(id);
            }
        }
    }

    private sealed class Entry
    {
        public Entry(long token, Rgb color)
        {
            Token = token;
            Color = color;
        }

        public long Token { get; }

        public Rgb Color { get; }

        public bool Sent { get; set; }

        public TimeSpan? RecheckDeadline { get; set; }
    }
}
