namespace BrowserWall.Core.Health;

/// <summary>Decision after a periodic check.</summary>
public enum NudgeDecision
{
    None,

    /// <summary>Nudge: widen by 1 pixel, restore after <see cref="NudgeTracker.NudgeRestoreDelay"/>.</summary>
    Nudge,
}

/// <summary>
/// "Nudge" state, keyed by window handle. Nudge only after two flat checks in a row; nudge once per flat episode;
/// after it recovers to not flat (periodic check or the recheck after a nudge), it takes two more flat checks to nudge again. Does not read the clock or touch windows.
/// </summary>
public sealed class NudgeTracker
{
    /// <summary>Interval of the periodic check.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(10);

    /// <summary>Delay before restoring the original size after a nudge.</summary>
    public static readonly TimeSpan NudgeRestoreDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>Delay before checking once more after a nudge.</summary>
    public static readonly TimeSpan RecheckDelay = TimeSpan.FromSeconds(2);

    /// <summary>Width added by a nudge (pixels).</summary>
    public const int NudgePixels = 1;

    /// <summary>How many flat checks in a row before nudging.</summary>
    public const int FlatCountToNudge = 2;

    private readonly Dictionary<long, (int Count, bool Nudged)> _states = new();

    /// <summary>
    /// Result of a periodic check: not flat → the streak resets and "nudged this episode" clears; flat → the streak grows by 1,
    /// and on reaching <see cref="FlatCountToNudge"/> without a nudge this episode → marks nudged and returns <see cref="NudgeDecision.Nudge"/>.
    /// </summary>
    public NudgeDecision Observe(long windowId, bool flat)
    {
        if (!flat)
        {
            _states.Remove(windowId);
            return NudgeDecision.None;
        }

        (int count, bool nudged) = _states.TryGetValue(windowId, out var s) ? s : (0, false);
        count = Math.Min(count + 1, FlatCountToNudge);
        if (count >= FlatCountToNudge && !nudged)
        {
            _states[windowId] = (count, true);
            return NudgeDecision.Nudge;
        }

        _states[windowId] = (count, nudged);
        return NudgeDecision.None;
    }

    /// <summary>
    /// The recheck <see cref="RecheckDelay"/> after a nudge; returns whether it recovered (for the log).
    /// Recovered → the flat episode ends (streak resets, "nudged" clears); still flat → state unchanged (no count added, no second nudge in the same episode).
    /// </summary>
    public bool Recheck(long windowId, bool flat)
    {
        if (!flat)
        {
            _states.Remove(windowId);
            return true;
        }

        return false;
    }

    /// <summary>
    /// <see cref="Observe"/> returned <see cref="NudgeDecision.Nudge"/> but no nudge actually happened (for example the wall was just minimized or the window just popped out):
    /// undoes "nudged this episode" and keeps the streak, so the next flat check still nudges; the episode's repair chance is not used up.
    /// </summary>
    public void CancelNudge(long windowId)
    {
        if (_states.TryGetValue(windowId, out var s) && s.Nudged)
        {
            _states[windowId] = (s.Count, false);
        }
    }

    /// <summary>Keeps state only for <paramref name="windowIds"/> (windows still managed).</summary>
    public void Retain(IEnumerable<long> windowIds)
    {
        ArgumentNullException.ThrowIfNull(windowIds);
        var keep = new HashSet<long>(windowIds);
        foreach (long id in _states.Keys.Where(id => !keep.Contains(id)).ToList())
        {
            _states.Remove(id);
        }
    }

    /// <summary>Windows that currently have state (for tests).</summary>
    public IReadOnlyCollection<long> TrackedIds => _states.Keys.ToList();
}
