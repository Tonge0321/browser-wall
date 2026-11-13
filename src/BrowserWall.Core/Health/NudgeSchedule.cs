namespace BrowserWall.Core.Health;

/// <summary>What to do after a nudge.</summary>
public enum NudgeStep
{
    /// <summary>Restore the original size (<see cref="NudgeTracker.NudgeRestoreDelay"/> after the nudge).</summary>
    Restore,

    /// <summary>Check once more (<see cref="NudgeTracker.RecheckDelay"/> after the nudge).</summary>
    Recheck,
}

/// <summary>One due job.</summary>
public sealed record NudgeDue(long WindowId, NudgeStep Step, TimeSpan Due);

/// <summary>
/// Per-window due jobs after a nudge: timing starts when that window was nudged;
/// restore = start + 500 ms, recheck = start + 2 s. Stores times only, does not touch windows; safe to use from multiple threads (locks internally).
/// </summary>
public sealed class NudgeSchedule
{
    private readonly object _lock = new();
    private readonly List<NudgeDue> _items = new();

    /// <summary>Schedules restore and recheck for <paramref name="windowId"/> (cancelling that window's unfinished ones first).</summary>
    public void Add(long windowId, TimeSpan nudgedAt)
    {
        lock (_lock)
        {
            _items.RemoveAll(i => i.WindowId == windowId);
            _items.Add(new NudgeDue(windowId, NudgeStep.Restore, nudgedAt + NudgeTracker.NudgeRestoreDelay));
            _items.Add(new NudgeDue(windowId, NudgeStep.Recheck, nudgedAt + NudgeTracker.RecheckDelay));
        }
    }

    /// <summary>Cancels the window's unfinished jobs (the window is no longer on the wall).</summary>
    public void Remove(long windowId)
    {
        lock (_lock)
        {
            _items.RemoveAll(i => i.WindowId == windowId);
        }
    }

    /// <summary>Windows that still have unfinished jobs.</summary>
    public IReadOnlyCollection<long> PendingWindowIds
    {
        get
        {
            lock (_lock)
            {
                return _items.Select(i => i.WindowId).Distinct().ToList();
            }
        }
    }

    /// <summary>The earliest due time; null when there are no jobs.</summary>
    public TimeSpan? NextDue
    {
        get
        {
            lock (_lock)
            {
                return _items.Count == 0 ? null : _items.Min(i => i.Due);
            }
        }
    }

    /// <summary>Takes all due jobs (due ≤ <paramref name="now"/>), ordered by due time.</summary>
    public IReadOnlyList<NudgeDue> TakeDue(TimeSpan now)
    {
        lock (_lock)
        {
            List<NudgeDue> due = _items.Where(i => i.Due <= now).OrderBy(i => i.Due).ToList();
            foreach (NudgeDue d in due)
            {
                _items.Remove(d);
            }

            return due;
        }
    }

    /// <summary>
    /// Takes due jobs and dispatches them: restore calls <paramref name="onRestore"/>, recheck calls <paramref name="onRecheck"/>.
    /// Both callbacks must only send, never wait (App: asynchronous SetWindowPos, handing off to the job queue), so cells never delay each other. Returns the number dispatched.
    /// </summary>
    public int Pump(TimeSpan now, Action<long> onRestore, Action<long> onRecheck)
    {
        ArgumentNullException.ThrowIfNull(onRestore);
        ArgumentNullException.ThrowIfNull(onRecheck);
        IReadOnlyList<NudgeDue> due = TakeDue(now);
        foreach (NudgeDue d in due)
        {
            if (d.Step == NudgeStep.Restore)
            {
                onRestore(d.WindowId);
            }
            else
            {
                onRecheck(d.WindowId);
            }
        }

        return due.Count;
    }
}
