namespace BrowserWall.Core.Infra;

/// <summary>
/// The "last requested state" and version of each target (for example a taskbar button: hide or show).
/// Before running in the background, <see cref="TryBegin"/> checks the latest request is still this state; after it returns, <see cref="AfterRun"/> checks:
/// if a newer request arrived meanwhile with a different state (for example a late old "hide" finishing after a new "show"), it returns the latest state so the caller sends it once more.
/// Does not touch any Windows API; safe to use from multiple threads (locks internally).
/// </summary>
public sealed class LatestRequest<TKey, TState>
    where TKey : notnull
{
    private readonly object _lock = new();
    private readonly Dictionary<TKey, (TState State, long Version)> _latest = new();
    private long _version;

    /// <summary>Records a new request and returns its version.</summary>
    public long Request(TKey key, TState state)
    {
        lock (_lock)
        {
            long v = ++_version;
            _latest[key] = (state, v);
            return v;
        }
    }

    /// <summary>The target's current last requested state; false when there has been no request.</summary>
    public bool TryGetLatest(TKey key, out TState state) => TryGetLatest(key, out state, out _);

    /// <summary>The target's current last requested state and version (read together under one lock); false when there has been no request.</summary>
    public bool TryGetLatest(TKey key, out TState state, out long version)
    {
        lock (_lock)
        {
            if (_latest.TryGetValue(key, out var l))
            {
                state = l.State;
                version = l.Version;
                return true;
            }

            state = default!;
            version = 0;
            return false;
        }
    }

    /// <summary>
    /// Before running (queued jobs of the same kind may have been merged): run only if the latest requested state equals <paramref name="expected"/>, and get the version to run.
    /// </summary>
    public bool TryBegin(TKey key, TState expected, out long version)
    {
        lock (_lock)
        {
            version = 0;
            if (!_latest.TryGetValue(key, out var l) || !EqualityComparer<TState>.Default.Equals(l.State, expected))
            {
                return false;
            }

            version = l.Version;
            return true;
        }
    }

    /// <summary>
    /// After running returns (whether or not it was abandoned on timeout): if a newer request arrived meanwhile and the latest state differs from what was just applied,
    /// returns true and the latest state (the caller sends it again); otherwise false.
    /// </summary>
    public bool AfterRun(TKey key, long version, TState applied, out TState latest)
    {
        lock (_lock)
        {
            latest = applied;
            if (!_latest.TryGetValue(key, out var l) || l.Version == version)
            {
                return false;
            }

            latest = l.State;
            return !EqualityComparer<TState>.Default.Equals(l.State, applied);
        }
    }
}
