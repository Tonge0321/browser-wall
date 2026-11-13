using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BrowserWall.Core.Health;
using BrowserWall.Core.Infra;

namespace BrowserWall.App;

/// <summary>
/// Removes and adds back taskbar buttons. Every call goes to a background queue (<see cref="BoundedWorker"/>, STA), and the
/// UI thread does not wait; each call waits at most 1 second from when it was sent, and on timeout it is skipped with one
/// diagnostics line (no title). Stuck calls are abandoned; while the stuck cap is reached, new calls are skipped
/// (<see cref="WorkOutcome.Skipped"/>, same as a timeout skip). Failures do not interrupt the program (taskbar buttons only affect appearance).
/// </summary>
internal sealed class Taskbar : IDisposable
{
    /// <summary>Deadline of each call.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    /// <summary>One ITaskbarList per worker thread (stuck threads are abandoned and replaced).</summary>
    [ThreadStatic]
    private static NativeMethods.ITaskbarList? _list;

    [ThreadStatic]
    private static bool _failed;

    private readonly BoundedWorker _worker;

    /// <summary>The last requested state per window (show or hide).</summary>
    private readonly LatestRequest<long, bool> _desired = new();

    /// <summary>Requests not done because the stuck cap was reached.</summary>
    private readonly MissedRequests<long, bool> _missed = new();

    private volatile bool _disposed;

    /// <param name="diagnostics">Logged to on timeout.</param>
    public Taskbar(DiagnosticsLog diagnostics)
    {
        _worker = new BoundedWorker(
            Timeout,
            concurrency: 1,
            maxAbandoned: BoundedWorker.MaxStuck,
            onTimeout: (_, tag) =>
            {
                if (tag is TaskbarTag t)
                {
                    diagnostics.Write(DiagnosticsFormat.TaskbarTimeout(DateTime.UtcNow, t.Cell, t.Show));
                }
            },
            configure: thread => thread.SetApartmentState(ApartmentState.STA),
            name: "Taskbar",
            onStuckCap: reached =>
            {
                diagnostics.Write(DiagnosticsFormat.StuckCap(DateTime.UtcNow, BackgroundWorkKind.Taskbar, reached));
                if (reached)
                {
                    _missed.CapReached();
                }
                else
                {
                    // Recovered: replay the last requested state for windows that still exist, once each.
                    Replay(_missed.Recovered(StillExists));
                }
            });
    }

    /// <summary>Removes the window's taskbar button (DeleteTab). <paramref name="cell"/> is the 1-based cell position (for the log), null when not on a cell.</summary>
    public void Hide(long windowId, int? cell = null) => Post(windowId, show: false, cell);

    /// <summary>Adds back the window's taskbar button (AddTab).</summary>
    public void Show(long windowId, int? cell = null) => Post(windowId, show: true, cell);

    /// <summary>
    /// When sending, records this window's last requested state and version (<see cref="LatestRequest{TKey, TState}"/>).
    /// Checked before running in the background: skipped when the last request is no longer this state (for example, hidden and then popped out to show);
    /// checked again after running: if a different newer request came meanwhile (a late old call finishing after a new one), the latest state is sent once more.
    /// </summary>
    private void Post(long windowId, bool show, int? cell)
    {
        long version = _desired.Request(windowId, show);
        _missed.Requested(windowId, version);
        Enqueue(windowId, show, cell);
    }

    /// <summary>
    /// Replay: always sends the last request as of the replay (if the pending state is outdated, the current latest wins),
    /// queued again through the normal send path without waiting; checked before running with <see cref="LatestRequest{TKey, TState}.TryBegin"/> as usual.
    /// </summary>
    private void Replay(IReadOnlyList<(long Key, bool State)> items)
    {
        foreach ((long id, bool recorded) in items)
        {
            Enqueue(id, _desired.TryGetLatest(id, out bool now) ? now : recorded, null);
        }
    }

    private static bool StillExists(long windowId) => NativeMethods.IsWindow(new IntPtr(windowId));

    private void Enqueue(long windowId, bool show, int? cell)
    {
        // Record whether this work really started: requests that ended without starting (skipped at the stuck cap, or timed out while queued) are registered for replay.
        var started = new StrongBox<bool>(false);
        Task<WorkOutcome> outcome = _worker.Post(
            $"{(show ? "show" : "hide")}:{windowId}",
            () =>
            {
                Volatile.Write(ref started.Value, true);
                if (!_desired.TryBegin(windowId, show, out long version))
                {
                    return;
                }

                Call(list =>
                {
                    if (show)
                    {
                        list.AddTab(new IntPtr(windowId));
                    }
                    else
                    {
                        list.DeleteTab(new IntPtr(windowId));
                    }
                });

                if (_desired.AfterRun(windowId, version, show, out bool latest))
                {
                    Enqueue(windowId, latest, cell);
                }
            },
            new TaskbarTag(cell, show));

        outcome.ContinueWith(
            t =>
            {
                if (_disposed)
                {
                    return; // Program exiting: the queue is closed, no more replays.
                }

                bool missed = t.Result == WorkOutcome.Skipped
                              || (t.Result == WorkOutcome.TimedOut && !Volatile.Read(ref started.Value));
                if (missed && _desired.TryGetLatest(windowId, out bool latest, out long version))
                {
                    Replay(_missed.Missed(windowId, latest, version, StillExists));
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    /// <summary>Completes with true when every sent call has a result; with false after <paramref name="max"/>. Non-blocking.</summary>
    public Task<bool> WhenIdle(TimeSpan max) => _worker.WhenIdle(max);

    public void Dispose()
    {
        _disposed = true;
        _worker.Dispose();
    }

    private static void Call(Action<NativeMethods.ITaskbarList> action)
    {
        NativeMethods.ITaskbarList? list = Get();
        if (list is null)
        {
            return;
        }

        try
        {
            action(list);
        }
        catch (COMException)
        {
            // The window no longer exists or the taskbar is temporarily unavailable: skip.
        }
    }

    private static NativeMethods.ITaskbarList? Get()
    {
        if (_list is not null || _failed)
        {
            return _list;
        }

        try
        {
            var list = (NativeMethods.ITaskbarList)new NativeMethods.TaskbarListClass();
            list.HrInit();
            _list = list;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            _failed = true;
        }

        return _list;
    }

    private sealed record TaskbarTag(int? Cell, bool Show);
}
