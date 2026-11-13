namespace BrowserWall.Core.Infra;

/// <summary>
/// Requests that were not carried out because background work hit the stuck cap (for example hiding or showing a taskbar button); after recovery,
/// each target that still exists gets its "last requested state" replayed once.
/// <para>
/// The pending list and the "at cap" flag share one lock, so nothing is lost regardless of the order of registrations and recovery notices:
/// requests registered while at the cap wait for the next <see cref="Recovered"/>; requests registered after the recovery notice arrived (or before the cap notice arrived)
/// are replayed once immediately per target after that recovery, and only if skipped again do they wait for the next recovery (no endless send-and-skip loop).
/// </para>
/// <para>
/// Each request carries a version (the value returned by <see cref="LatestRequest{TKey, TState}.Request"/>): an older registration never overwrites a newer pending one,
/// and is not added after a newer request (a late old report cannot turn the latest "show" back into an old "hide").
/// </para>
/// Does not touch Windows APIs; safe to use from multiple threads.
/// </summary>
public sealed class MissedRequests<TKey, TState>
    where TKey : notnull
{
    private readonly object _lock = new();
    private readonly Dictionary<TKey, (TState State, long Version)> _pending = new();

    /// <summary>Version of the newest request sent for each target.</summary>
    private readonly Dictionary<TKey, long> _requested = new();

    /// <summary>Recovery count at each target's last immediate replay.</summary>
    private readonly Dictionary<TKey, long> _replayedAt = new();

    private bool _capped;
    private long _recoveries;

    /// <summary>Current number of pending replays (for tests).</summary>
    public int PendingCount
    {
        get
        {
            lock (_lock)
            {
                return _pending.Count;
            }
        }
    }

    /// <summary>
    /// A new request was sent (version <paramref name="version"/>): records the newest version and clears this target's pending replay if it is not newer
    /// (the new request does the work itself; if it is skipped too, it registers again with its own version).
    /// </summary>
    public void Requested(TKey key, long version)
    {
        lock (_lock)
        {
            if (!_requested.TryGetValue(key, out long known) || version > known)
            {
                _requested[key] = version;
            }

            if (_pending.TryGetValue(key, out var p) && p.Version <= version)
            {
                _pending.Remove(key);
            }
        }
    }

    /// <summary>Received the "stuck cap reached" notice.</summary>
    public void CapReached()
    {
        lock (_lock)
        {
            _capped = true;
        }
    }

    /// <summary>
    /// Received the "resumed" notice: marks not at cap, increments the recovery count, takes and clears all pending replays; those where <paramref name="stillExists"/> is false are dropped.
    /// </summary>
    public IReadOnlyList<(TKey Key, TState State)> Recovered(Func<TKey, bool> stillExists)
    {
        ArgumentNullException.ThrowIfNull(stillExists);
        List<(TKey, TState)> taken;
        lock (_lock)
        {
            _capped = false;
            _recoveries++;
            taken = _pending.Select(p => (p.Key, p.Value.State)).ToList();
            _pending.Clear();
            _replayedAt.Clear();
        }

        return taken.Where(t => stillExists(t.Item1)).ToList();
    }

    /// <summary>
    /// Registers a request that was not carried out: <paramref name="latest"/> and <paramref name="version"/> are the target's last requested state and version at that moment.
    /// If a newer request was already sent, or a newer version is already pending → not registered (the newer one handles itself); returns empty.
    /// Otherwise, by the current state: at cap → kept for the next recovery, returns empty; not at cap and this target has not been replayed
    /// since this recovery → returned for immediate replay (dropped if the target no longer exists); already replayed since this recovery → kept for the next recovery, returns empty.
    /// </summary>
    public IReadOnlyList<(TKey Key, TState State)> Missed(TKey key, TState latest, long version, Func<TKey, bool> stillExists)
    {
        ArgumentNullException.ThrowIfNull(stillExists);
        lock (_lock)
        {
            if ((_requested.TryGetValue(key, out long newest) && version < newest)
                || (_pending.TryGetValue(key, out var existing) && existing.Version > version))
            {
                return Array.Empty<(TKey, TState)>();
            }

            if (_capped || (_replayedAt.TryGetValue(key, out long at) && at == _recoveries))
            {
                _pending[key] = (latest, version);
                return Array.Empty<(TKey, TState)>();
            }

            _pending.Remove(key);
            _replayedAt[key] = _recoveries;
        }

        return stillExists(key) ? new[] { (key, latest) } : Array.Empty<(TKey, TState)>();
    }
}
