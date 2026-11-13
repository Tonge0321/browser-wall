using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Threading;
using BrowserWall.Core.Health;
using BrowserWall.Core.Infra;
using BrowserWall.Core.Session;

namespace BrowserWall.App;

/// <summary>The address bar read from one managed window this round: <see cref="Value"/> is null when unreadable or not read because it has focus.</summary>
internal readonly record struct AddressBarReading(long WindowId, string? Value, bool Focused);

/// <summary>
/// Reads every managed window's address bar with UI Automation every 2 seconds.
/// Runs on a separate background thread; UI Automation is never called on the UI thread, and exiting does not wait for it.
/// Fixed-tick schedule (<see cref="ReadSchedule"/>): an overrun takes the next tick, with no extra wait and no overlap.
/// The address bar is identified by <see cref="AddressBarPick"/>: not inside web content (Document), under a toolbar or
/// with a known address bar name, exactly one. The value is not read while it has keyboard focus. Each window is read
/// separately with its own deadline (one stuck window does not block other cells), and each result is handed back to the
/// UI thread with <see cref="Dispatcher.BeginInvoke(Delegate, object[])"/>.
/// </summary>
internal sealed class AddressBarReader
{
    private readonly Dispatcher _dispatcher;
    private readonly Action<IReadOnlyList<AddressBarReading>> _onRound;
    private readonly object _lock = new();
    private readonly Dictionary<long, AutomationElement> _cache = new();
    private readonly ManualResetEventSlim _stop = new(false);

    /// <summary>
    /// Each window's read goes to a work queue with deadlines (16 slots, 5 seconds each; the first identification of the
    /// address bar takes about 1–4 seconds). Timed-out reads are always abandoned and treated as unreadable (the old URL is
    /// kept). Abandoned reads do not take a slot, and at most <see cref="BoundedWorker.MaxStuck"/> may still be stuck; so at
    /// most that many actually run at once, and the rest wait in the queue.
    /// </summary>
    private readonly BoundedWorker _readers;

    /// <summary>Deadline of one read per window.</summary>
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Windows whose previous read has not returned yet.</summary>
    private readonly HashSet<long> _inFlight = new();
    private List<long> _windowIds = new();

    /// <param name="diagnostics">One line each when the stuck cap is reached and when it recovers (no titles or URLs).</param>
    public AddressBarReader(
        Dispatcher dispatcher, Action<IReadOnlyList<AddressBarReading>> onRound, DiagnosticsLog diagnostics)
    {
        _dispatcher = dispatcher;
        _onRound = onRound;
        _readers = new BoundedWorker(
            ReadTimeout,
            Core.WallLimits.MaxCells,
            BoundedWorker.MaxStuck,
            name: "AddressBarReader",
            onStuckCap: reached =>
                diagnostics.Write(DiagnosticsFormat.StuckCap(DateTime.UtcNow, BackgroundWorkKind.AddressBar, reached)));
    }

    /// <summary>Starts reading (background thread).</summary>
    public void Start()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "AddressBarReader" };
        thread.Start();
    }

    /// <summary>The UI thread passes in the current managed windows after the state changes.</summary>
    public void SetWindows(IEnumerable<long> windowIds)
    {
        List<long> copy = windowIds.ToList();
        lock (_lock)
        {
            _windowIds = copy;
        }
    }

    /// <summary>Wall closing: set the stop flag and stop the work queue, without waiting for any thread to end.</summary>
    public void Stop()
    {
        _stop.Set();
        _readers.Dispose();
    }

    private void Run()
    {
        var clock = Stopwatch.StartNew();
        TimeSpan start = clock.Elapsed;
        while (!_stop.IsSet)
        {
            TimeSpan due = ReadSchedule.NextDue(start, clock.Elapsed, LastSeenUrl.RecordInterval);
            TimeSpan wait = due - clock.Elapsed;
            if (wait > TimeSpan.Zero && _stop.Wait(wait))
            {
                return;
            }

            List<long> ids;
            lock (_lock)
            {
                ids = _windowIds.ToList();
            }

            lock (_cache)
            {
                foreach (long gone in _cache.Keys.Where(id => !ids.Contains(id)).ToList())
                {
                    _cache.Remove(gone);
                }
            }

            // Stuck reads reached the cap: nothing is dispatched this whole round; after the stuck ones return, the next round reads as usual.
            if (_readers.IsAtStuckCap)
            {
                continue;
            }

            // Each window is read separately with its own deadline: one stuck window does not block other cells' results.
            // Each cell is handed back to the UI thread as soon as it is read; a window whose previous read has not returned is skipped this round (a stuck window takes at most one thread).
            // This thread only dispatches and never waits for any read result; the next 2-second tick dispatches the other cells as usual.
            foreach (long id in ids)
            {
                lock (_inFlight)
                {
                    if (!_inFlight.Add(id))
                    {
                        continue;
                    }
                }

                var started = new StrongBox<bool>(false);

                // Deadline of this read (no later than the work queue's): a result returned after it is always dropped, not handed back, and does not update the list.
                TimeSpan deadline = clock.Elapsed + ReadTimeout;
                Task<WorkOutcome> task = _readers.Post($"read:{id}", () =>
                {
                    started.Value = true;
                    try
                    {
                        AddressBarReading reading = ReadOne(id);
                        if (!_stop.IsSet && clock.Elapsed < deadline)
                        {
                            _dispatcher.BeginInvoke(_onRound, (IReadOnlyList<AddressBarReading>)new[] { reading });
                        }
                    }
                    finally
                    {
                        lock (_inFlight)
                        {
                            _inFlight.Remove(id);
                        }
                    }
                });

                // Reads that timed out in the queue and never started, and reads not dispatched because of the stuck cap (Skipped), do not run the finally above:
                // when the result arrives, clear its "reading" mark so the next round reads as usual.
                // Reads that started and got stuck keep the mark (no extra thread for the same window) and clear it themselves on return. This result is not awaited.
                task.ContinueWith(
                    t =>
                    {
                        if ((t.Result is WorkOutcome.TimedOut or WorkOutcome.Skipped) && !started.Value)
                        {
                            lock (_inFlight)
                            {
                                _inFlight.Remove(id);
                            }
                        }
                    },
                    TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion);
            }
        }
    }

    /// <summary>Reads one window: checks focus first and does not read the value while focused; any error counts as unreadable and clears the cache.</summary>
    private AddressBarReading ReadOne(long id)
    {
        try
        {
            AutomationElement? bar;
            lock (_cache)
            {
                _cache.TryGetValue(id, out bar);
            }

            if (bar is null)
            {
                bar = Find(new IntPtr(id));
                if (bar is null)
                {
                    return new AddressBarReading(id, null, false);
                }

                lock (_cache)
                {
                    _cache[id] = bar;
                }
            }

            if (bar.Current.HasKeyboardFocus)
            {
                return new AddressBarReading(id, null, true);
            }

            var pattern = (ValuePattern)bar.GetCurrentPattern(ValuePattern.Pattern);
            return new AddressBarReading(id, pattern.Current.Value, false);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException
                                       or ArgumentException or TimeoutException)
        {
            lock (_cache)
            {
                _cache.Remove(id);
            }

            return new AddressBarReading(id, null, false);
        }
    }

    /// <summary>
    /// Walks down from the window level by level (control view) without entering Document (web content); collects a description of each Edit for <see cref="AddressBarPick"/>.
    /// </summary>
    private static AutomationElement? Find(IntPtr hwnd)
    {
        AutomationElement root = AutomationElement.FromHandle(hwnd);
        TreeWalker walker = TreeWalker.ControlViewWalker;
        var edits = new List<AutomationElement>();
        var described = new List<AddressBarCandidate>();
        var stack = new Stack<(AutomationElement Element, bool UnderToolBar)>();
        stack.Push((root, false));

        while (stack.Count > 0)
        {
            (AutomationElement element, bool underToolBar) = stack.Pop();
            ControlType type = element.Current.ControlType;
            if (type == ControlType.Document)
            {
                continue; // Web content: do not enter
            }

            if (type == ControlType.Edit)
            {
                bool hasValue = element.TryGetCurrentPattern(ValuePattern.Pattern, out _);
                edits.Add(element);
                described.Add(new AddressBarCandidate(true, hasValue, false, underToolBar, element.Current.Name));
            }

            bool childUnderToolBar = underToolBar || type == ControlType.ToolBar;
            var children = new List<AutomationElement>();
            for (AutomationElement? child = walker.GetFirstChild(element); child is not null; child = walker.GetNextSibling(child))
            {
                children.Add(child);
            }

            // Process in the original order (last in, first out, so push in reverse).
            for (int i = children.Count - 1; i >= 0; i--)
            {
                stack.Push((children[i], childUnderToolBar));
            }
        }

        int index = AddressBarPick.Choose(described);
        return index >= 0 ? edits[index] : null;
    }
}
