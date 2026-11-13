using System.Diagnostics;
using BrowserWall.Core.Health;
using BrowserWall.Core.Infra;
using BrowserWall.Core.Session;

namespace BrowserWall.App;

/// <summary>
/// Cells whose content turned a single flat color. Capturing and judging run on background threads, never on the UI
/// thread, never blocking the wall; exiting does not wait for them.
/// <list type="bullet">
/// <item>Periodic check thread: one round every 10 seconds (fixed ticks), skipped while the wall is minimized; captures go to a work queue with deadlines (one cell at a time, 2 seconds).</item>
/// <item>Actions on windows (widen, restore) are not done in the background: they go to the UI thread (<c>requestNudge</c> / <c>requestRestore</c>),
/// where the wall decides at that moment from the current state and current wall (<see cref="NudgeGate"/>), never from a remembered old size.</item>
/// <item>Every nudge has a token (<see cref="NudgeTokens"/>): sending, restoring and rechecking all verify it is still the same nudge; if not sent, it is cancelled without using up the repair chance.</item>
/// <item>Due-work thread: each window starts from the moment it was nudged; restore after 500 ms, recheck after 2 seconds; dispatch only, never wait.</item>
/// <item>Recheck captures and periodic captures share one work queue (16 + 1 slots, 2 seconds each; at most 4 stuck in total);
/// a recheck has its own deadline, and a late result neither updates the state nor logs a result; while the stuck cap is reached, whole rounds are skipped and rechecks log "not checked".</item>
/// <item>Diagnostics only log time, cell position, color and result; no titles or URLs are read, and no screen content is stored.</item>
/// </list>
/// </summary>
internal sealed class FlatChecker
{
    /// <summary>Deadline of one capture; on timeout it is logged and abandoned.</summary>
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(2);

    /// <summary>A round longer than this writes one line.</summary>
    private static readonly TimeSpan SlowRoundLimit = TimeSpan.FromSeconds(1);

    private readonly DiagnosticsLog _log;
    private readonly Action<long, long> _requestNudge;
    private readonly Action<long, long> _requestRestore;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _stateLock = new();
    private readonly object _trackerLock = new();
    private readonly NudgeTracker _tracker = new();
    private readonly NudgeTokens _tokens = new();
    private readonly NudgeSchedule _schedule = new();
    private readonly AutoResetEvent _dueSignal = new(false);
    private readonly ManualResetEventSlim _stop = new(false);
    private readonly BoundedWorker _capture;

    private List<(long WindowId, int Cell)> _cells = new();
    private bool _active;
    private bool _stopped;
    private long _epoch;

    /// <param name="requestNudge">Asks the wall to widen on the UI thread (window, token); send only, no wait.</param>
    /// <param name="requestRestore">Asks the wall to restore from the current wall on the UI thread (window, token); send only, no wait.</param>
    public FlatChecker(DiagnosticsLog log, Action<long, long> requestNudge, Action<long, long> requestRestore)
    {
        _log = log;
        _requestNudge = requestNudge;
        _requestRestore = requestRestore;
        // Periodic captures and rechecks share one queue: at most BoundedWorker.MaxStuck stuck in total for this kind of work.
        // Slots = one recheck per cell + one periodic capture; how many actually run at once is limited by the stuck cap.
        _capture = new BoundedWorker(
            CaptureTimeout,
            Core.WallLimits.MaxCells + 1,
            BoundedWorker.MaxStuck,
            (_, tag) =>
            {
                if (tag is RecheckTag t)
                {
                    OnRecheckTimeout(t);
                }
                else
                {
                    _log.Write(DiagnosticsFormat.CaptureTimeout(DateTime.UtcNow, tag as int?));
                }
            },
            name: "FlatCapture",
            onStuckCap: reached =>
                _log.Write(DiagnosticsFormat.StuckCap(DateTime.UtcNow, BackgroundWorkKind.FlatCheck, reached)));
    }

    public void Start()
    {
        new Thread(RoundLoop) { IsBackground = true, Name = "FlatChecker.Round" }.Start();
        new Thread(DueLoop) { IsBackground = true, Name = "FlatChecker.Due" }.Start();
    }

    /// <summary>Wall closing: stop all work without waiting for any thread.</summary>
    public void Stop()
    {
        lock (_stateLock)
        {
            _stopped = true;
            _active = false;
            _epoch++;
        }

        _stop.Set();
        _dueSignal.Set();
        _capture.Dispose();
    }

    /// <summary>The UI thread passes in the current managed windows and cell positions (1-based) before acting on windows.</summary>
    public void SetWindows(IReadOnlyList<(long WindowId, int Cell)> cells)
    {
        lock (_stateLock)
        {
            _cells = cells.ToList();
        }
    }

    /// <summary>True while the wall is neither minimized nor closed; the generation number increases by 1 when checking becomes impossible.</summary>
    public void SetActive(bool active)
    {
        lock (_stateLock)
        {
            if (_stopped)
            {
                return;
            }

            if (_active && !active)
            {
                _epoch++;
            }

            _active = active;
        }
    }

    /// <summary>(UI thread) Whether this nudge is still valid.</summary>
    public bool IsCurrentNudge(long windowId, long token) => _tokens.IsCurrent(windowId, token);

    /// <summary>(UI thread) The widen was sent: schedule the restore and recheck from now, and log "nudge".</summary>
    public void NudgeSent(long windowId, long token, int? cell)
    {
        if (!_tokens.MarkSent(windowId, token))
        {
            return;
        }

        _schedule.Add(windowId, _clock.Elapsed);
        _dueSignal.Set();
        _log.Write(DiagnosticsFormat.Nudge(DateTime.UtcNow, cell, _tokens.ColorOf(windowId)));
    }

    /// <summary>(UI thread) Cannot nudge right now: cancel this nudge and undo "nudged in this episode", without using up the repair chance.</summary>
    public void NudgeNotSent(long windowId, long token)
    {
        lock (_trackerLock)
        {
            if (_tokens.Cancel(windowId, token))
            {
                _tracker.CancelNudge(windowId);
            }
        }
    }

    private bool TryContext(long windowId, out int cell, out long epoch)
    {
        lock (_stateLock)
        {
            cell = 0;
            epoch = _epoch;
            if (!_active || _stopped)
            {
                return false;
            }

            foreach ((long id, int c) in _cells)
            {
                if (id == windowId)
                {
                    cell = c;
                    return true;
                }
            }

            return false;
        }
    }

    private bool IsActive()
    {
        lock (_stateLock)
        {
            return _active && !_stopped;
        }
    }

    /// <summary>After a capture: the wall's generation is unchanged (not minimized or closed meanwhile) and the window is still managed. The caller holds _stateLock.</summary>
    private bool StillValidLocked(long epoch, long windowId) =>
        _active && !_stopped && _epoch == epoch && _cells.Any(c => c.WindowId == windowId);

    private int? CellOf(long windowId)
    {
        lock (_stateLock)
        {
            foreach ((long id, int c) in _cells)
            {
                if (id == windowId)
                {
                    return c;
                }
            }

            return null;
        }
    }

    private void RoundLoop()
    {
        TimeSpan start = _clock.Elapsed;
        while (!_stop.IsSet)
        {
            TimeSpan due = ReadSchedule.NextDue(start, _clock.Elapsed, NudgeTracker.CheckInterval);
            TimeSpan wait = due - _clock.Elapsed;
            if (wait > TimeSpan.Zero && _stop.Wait(wait))
            {
                return;
            }

            List<(long WindowId, int Cell)> cells;
            lock (_stateLock)
            {
                if (!_active || _stopped)
                {
                    continue; // Check only while the wall is not minimized
                }

                cells = _cells.ToList();
            }

            // Stuck captures reached the cap: this whole round is skipped; after the stuck ones return, the next round runs as usual.
            if (_capture.IsAtStuckCap)
            {
                continue;
            }

            ForgetRemoved(cells.Select(c => c.WindowId).ToHashSet());
            var round = Stopwatch.StartNew();
            foreach ((long id, _) in cells)
            {
                if (_stop.IsSet)
                {
                    return;
                }

                if (!TryContext(id, out _, out long epoch))
                {
                    if (!IsActive())
                    {
                        break; // Minimized or closed in the middle of a round: stop this round
                    }

                    continue;
                }

                List<Rgb>? samples = CaptureVia(_capture, id);
                if (samples is not null && !Observe(id, samples, epoch) && !IsActive())
                {
                    break;
                }
            }

            if (round.Elapsed > SlowRoundLimit)
            {
                _log.Write(DiagnosticsFormat.SlowRound(DateTime.UtcNow, round.Elapsed));
            }
        }
    }

    /// <summary>
    /// Judges and updates the state; to nudge, issues a token and asks the wall to nudge on the UI thread (send only, no wait); the log is written last.
    /// Generation and managed checks and the state update happen under the same lock. Returns false when this capture is stale (not counted as an observation).
    /// </summary>
    private bool Observe(long id, List<Rgb> samples, long epoch)
    {
        bool flat = FlatCheck.IsUniform(samples, out Rgb color);
        long? token = null;
        lock (_stateLock)
        {
            if (!StillValidLocked(epoch, id))
            {
                return false;
            }

            lock (_trackerLock)
            {
                NudgeDecision decision = _tracker.Observe(id, flat);
                if (!flat)
                {
                    _tokens.EndEpisode(id); // This flat episode ended: cancel unfinished nudges
                }
                else if (decision == NudgeDecision.Nudge)
                {
                    token = _tokens.Issue(id, color);
                }
            }
        }

        if (flat)
        {
            _log.Write(DiagnosticsFormat.Flat(DateTime.UtcNow, CellOf(id), color));
        }

        // Whether a nudge is allowed is decided again on the UI thread when it runs, so logging here first never causes a stale action.
        if (token is long t)
        {
            _requestNudge(id, t);
        }

        return true;
    }

    private void DueLoop()
    {
        while (!_stop.IsSet)
        {
            TimeSpan? next = _schedule.NextDue;
            if (next is not TimeSpan due)
            {
                _dueSignal.WaitOne();
                continue;
            }

            TimeSpan wait = due - _clock.Elapsed;
            if (wait > TimeSpan.Zero)
            {
                _dueSignal.WaitOne(wait);
                continue;
            }

            // Dispatch only, never wait: restores go to the UI thread, rechecks to the work queue.
            _schedule.Pump(_clock.Elapsed, Restore, StartRecheck);
        }
    }

    /// <summary>Restore after 500 ms: asks the wall to place it again from the current wall on the UI thread (only for the same nudge).</summary>
    private void Restore(long id)
    {
        if (_tokens.SentToken(id) is long token)
        {
            _requestRestore(id, token);
        }
    }

    /// <summary>Recheck after 2 seconds: records this recheck's deadline, hands it to the work queue and returns (no wait).</summary>
    private void StartRecheck(long id)
    {
        if (_tokens.SentToken(id) is not long token
            || !_tokens.BeginRecheck(id, token, _clock.Elapsed + CaptureTimeout))
        {
            return;
        }

        var tag = new RecheckTag(id, token, CellOf(id));
        Task<WorkOutcome> task = _capture.Post($"recheck:{id}", () => Recheck(id, token), tag);

        // Not dispatched because of the stuck cap: handled as "not checked" (cancel this nudge, log "not checked"). This result is not awaited.
        task.ContinueWith(
            t =>
            {
                if (t.Result == WorkOutcome.Skipped)
                {
                    OnRecheckSkipped(tag);
                }
            },
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    /// <summary>The recheck was not dispatched (stuck cap reached): cancel this nudge and log "not checked".</summary>
    private void OnRecheckSkipped(RecheckTag tag)
    {
        Rgb color = _tokens.ColorOf(tag.WindowId);
        if (_tokens.TryExpireRecheck(tag.WindowId, tag.Token))
        {
            _log.Write(DiagnosticsFormat.NudgeResult(DateTime.UtcNow, tag.Cell, color, NudgeOutcome.NotChecked));
        }
    }

    /// <summary>The recheck capture timed out: cancel this nudge and log the capture timeout and "not checked" (state unchanged); no action if a result was already accepted.</summary>
    private void OnRecheckTimeout(RecheckTag tag)
    {
        Rgb color = _tokens.ColorOf(tag.WindowId);
        if (_tokens.TryExpireRecheck(tag.WindowId, tag.Token))
        {
            _log.Write(DiagnosticsFormat.CaptureTimeout(DateTime.UtcNow, tag.Cell));
            _log.Write(DiagnosticsFormat.NudgeResult(DateTime.UtcNow, tag.Cell, color, NudgeOutcome.NotChecked));
        }
    }

    private void Recheck(long id, long token)
    {
        Rgb color = _tokens.ColorOf(id);
        if (!TryContext(id, out int cell, out long epoch) || !_tokens.IsCurrent(id, token))
        {
            if (_tokens.TryExpireRecheck(id, token))
            {
                _log.Write(DiagnosticsFormat.NudgeResult(DateTime.UtcNow, CellOf(id), color, NudgeOutcome.NotChecked));
            }

            return;
        }

        List<Rgb>? samples = Capture(new IntPtr(id));
        bool? recovered = null;
        bool notChecked = false;
        lock (_stateLock)
        {
            lock (_trackerLock)
            {
                if (samples is null || !StillValidLocked(epoch, id))
                {
                    notChecked = _tokens.TryExpireRecheck(id, token);
                }
                else if (_tokens.TryFinishRecheck(id, token, _clock.Elapsed))
                {
                    // Update the state only within the deadline and for the same nudge (timed-out or late results are always ignored).
                    bool flat = FlatCheck.IsUniform(samples, out color);
                    recovered = _tracker.Recheck(id, flat);
                }
            }
        }

        if (recovered is bool r)
        {
            _log.Write(DiagnosticsFormat.NudgeResult(
                DateTime.UtcNow, cell, color, r ? NudgeOutcome.Recovered : NudgeOutcome.NotRecovered));
        }
        else if (notChecked)
        {
            _log.Write(DiagnosticsFormat.NudgeResult(DateTime.UtcNow, cell, color, NudgeOutcome.NotChecked));
        }
    }

    private void ForgetRemoved(HashSet<long> current)
    {
        lock (_trackerLock)
        {
            _tracker.Retain(current);
            _tokens.Retain(current);
        }

        foreach (long id in _schedule.PendingWindowIds.Where(id => !current.Contains(id)).ToList())
        {
            _schedule.Remove(id);
        }
    }

    /// <summary>
    /// Captures through the work queue, waiting at most until the deadline; returns null on timeout (already reported for logging) or failure.
    /// When the work starts, it checks again that the wall is not minimized or closed and the window is still managed; otherwise it does not capture.
    /// </summary>
    private List<Rgb>? CaptureVia(BoundedWorker worker, long id)
    {
        var holder = new Holder();
        Task<WorkOutcome> task = worker.Post(
            $"capture:{id}",
            () => holder.Samples = TryContext(id, out _, out _) ? Capture(new IntPtr(id)) : null,
            CellOf(id));
        try
        {
            return task.GetAwaiter().GetResult() == WorkOutcome.Completed ? holder.Samples : null;
        }
        catch (Exception ex) when (ex is TaskCanceledException or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Captures with PrintWindow (PW_RENDERFULLCONTENT) and samples colors on a 10×10 grid; the image is released right after use and never saved.
    /// Window gone, minimized, zero size, or capture failed → null. Read only; the window is not changed.
    /// </summary>
    private static List<Rgb>? Capture(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd) || NativeMethods.IsIconic(hwnd)
            || !NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT r))
        {
            return null;
        }

        int w = r.Right - r.Left;
        int h = r.Bottom - r.Top;
        IReadOnlyList<(int X, int Y)> points = FlatCheck.SamplePoints(w, h);
        if (points.Count == 0)
        {
            return null;
        }

        IntPtr screen = NativeMethods.GetDC(IntPtr.Zero);
        IntPtr dc = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr old = IntPtr.Zero;
        try
        {
            if (screen == IntPtr.Zero)
            {
                return null;
            }

            dc = NativeMethods.CreateCompatibleDC(screen);
            bitmap = NativeMethods.CreateCompatibleBitmap(screen, w, h);
            if (dc == IntPtr.Zero || bitmap == IntPtr.Zero)
            {
                return null;
            }

            old = NativeMethods.SelectObject(dc, bitmap);
            if (!NativeMethods.PrintWindow(hwnd, dc, NativeMethods.PW_RENDERFULLCONTENT))
            {
                return null;
            }

            var samples = new List<Rgb>(points.Count);
            foreach ((int x, int y) in points)
            {
                uint c = NativeMethods.GetPixel(dc, x, y);
                if (c == NativeMethods.CLR_INVALID)
                {
                    return null;
                }

                samples.Add(new Rgb((byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF)));
            }

            return samples;
        }
        finally
        {
            if (dc != IntPtr.Zero && old != IntPtr.Zero)
            {
                NativeMethods.SelectObject(dc, old);
            }

            if (bitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(bitmap);
            }

            if (dc != IntPtr.Zero)
            {
                NativeMethods.DeleteDC(dc);
            }

            if (screen != IntPtr.Zero)
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }

    private sealed class Holder
    {
        public List<Rgb>? Samples { get; set; }
    }

    private sealed record RecheckTag(long WindowId, long Token, int? Cell);
}
