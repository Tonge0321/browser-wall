using BrowserWall.Core.Layout;

namespace BrowserWall.Core.Detect;

/// <summary>A top-level window with its screen position and size.</summary>
public sealed record DragOutWindow(WindowInfo Info, CellRect Bounds);

/// <summary>
/// The situation at one poll (provided by the App).
/// <see cref="Now"/>: monotonic time; <see cref="AwaitedIds"/>: windows being waited for by a launch or "+ Add page" (found, not yet registered);
/// <see cref="CanAddNow"/>: the App passes <c>WallState.CanAutoAdd</c>; <see cref="FreeSlots"/>: the App passes <c>WallState.FreeSlotCount</c>;
/// <see cref="ExistingIds"/>: handles of all current top-level windows (including invisible ones), used to drop the "seen" record of windows that no longer exist.
/// </summary>
public sealed record DragOutSnapshot(
    TimeSpan Now,
    IReadOnlyList<DragOutWindow> Windows,
    bool LeftButtonDown,
    uint? WallProcessId,
    IReadOnlyCollection<long> ManagedIds,
    IReadOnlyCollection<long> AwaitedIds,
    bool CanAddNow,
    int FreeSlots,
    IReadOnlyCollection<long> ExistingIds);

/// <summary>Kind of decision.</summary>
public enum DragOutDecisionKind
{
    /// <summary>Add to the wall (the App calls <c>WallState.AutoAddWindow</c>).</summary>
    Add,

    /// <summary>Wall is full: the window stays where it is and "wall is full" is shown for 3 seconds.</summary>
    Full,
}

/// <summary>Decision for one dragged-out window; each window gets at most one.</summary>
public sealed record DragOutDecision(long WindowId, DragOutDecisionKind Kind);

/// <summary>
/// Decides when a tab dragged out of the browser joins the wall automatically. Touches no windows; decides only from
/// the situation the App provides.
/// <list type="number">
/// <item>A window seen for the first time (and visible) is a dragged-out window and starts waiting only if it belongs to
/// the wall's browser process, has class Chrome_WidgetWin_1, is not managed or awaited, and the left mouse button is
/// down at that moment; otherwise it is never added automatically. The process match is required.</item>
/// <item>While waiting: dropped when it disappears, becomes invisible or becomes managed; the timer restarts while the
/// button is held or when the position or size changes; it is stable only after the button is released and the position
/// and size stay unchanged for <see cref="StableTime"/> (time while held does not count).</item>
/// <item>Stable but cannot be added right now (<see cref="DragOutSnapshot.CanAddNow"/> is false): keeps waiting, not dropped.</item>
/// <item>Candidates that become stable together are taken in the order they were first detected, each using one of
/// <see cref="DragOutSnapshot.FreeSlots"/>: Add while capacity remains, Full afterwards.</item>
/// </list>
/// </summary>
public sealed class DragOutWatcher
{
    /// <summary>How long the position and size must stay unchanged.</summary>
    public static readonly TimeSpan StableTime = TimeSpan.FromSeconds(1);

    /// <summary>Maximum polling interval; the single value the App polls with.</summary>
    public static readonly TimeSpan MaxPollInterval = TimeSpan.FromMilliseconds(100);

    private readonly HashSet<long> _seen;
    private readonly List<Candidate> _waiting = new();

    /// <param name="existingIds">Windows that exist when watching starts; none of them count as new windows.</param>
    public DragOutWatcher(IEnumerable<long> existingIds)
    {
        ArgumentNullException.ThrowIfNull(existingIds);
        _seen = new HashSet<long>(existingIds);
    }

    /// <summary>Windows already seen (for tests).</summary>
    public IReadOnlyCollection<long> SeenIds => _seen.ToList();

    /// <summary>Dragged-out windows waiting to become stable, in the order they were first detected.</summary>
    public IReadOnlyCollection<long> WaitingIds => _waiting.Select(c => c.Id).ToList();

    public IReadOnlyList<DragOutDecision> Poll(DragOutSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Windows);
        ArgumentNullException.ThrowIfNull(snapshot.ManagedIds);
        ArgumentNullException.ThrowIfNull(snapshot.AwaitedIds);
        ArgumentNullException.ThrowIfNull(snapshot.ExistingIds);

        // 0. Drop the "seen" record of windows that no longer exist, so a reused handle is judged as a new window.
        //    Windows that still exist but are invisible this time are kept.
        var existing = new HashSet<long>(snapshot.ExistingIds);
        _seen.RemoveWhere(id => !existing.Contains(id));

        var managed = new HashSet<long>(snapshot.ManagedIds);
        var awaited = new HashSet<long>(snapshot.AwaitedIds);
        var current = new Dictionary<long, DragOutWindow>();
        foreach (DragOutWindow w in snapshot.Windows)
        {
            current.TryAdd(w.Info.Id, w);
        }

        // 1. Waiting windows: drop them or update the stability timer.
        for (int i = _waiting.Count - 1; i >= 0; i--)
        {
            Candidate c = _waiting[i];
            if (!current.TryGetValue(c.Id, out DragOutWindow? w) || !w.Info.Visible || managed.Contains(c.Id))
            {
                _waiting.RemoveAt(i);
                continue;
            }

            bool moved = w.Bounds != c.Bounds;
            c.Bounds = w.Bounds;
            if (snapshot.LeftButtonDown)
            {
                // Time while the button is held does not count.
                c.StableSince = null;
            }
            else if (moved)
            {
                // Position or size changed after release: restart timing from now.
                c.StableSince = snapshot.Now;
            }
            else
            {
                c.StableSince ??= snapshot.Now;
            }
        }

        // 2. Visible windows seen for the first time: decide whether they are dragged-out windows.
        foreach (DragOutWindow w in snapshot.Windows)
        {
            WindowInfo info = w.Info;
            if (!info.Visible || _seen.Contains(info.Id))
            {
                continue;
            }

            _seen.Add(info.Id);
            if (snapshot.LeftButtonDown
                && snapshot.WallProcessId is uint wallPid
                && info.ProcessId == wallPid
                && string.Equals(info.ClassName, WindowDetector.BrowserWindowClass, StringComparison.Ordinal)
                && !managed.Contains(info.Id)
                && !awaited.Contains(info.Id))
            {
                _waiting.Add(new Candidate(info.Id, w.Bounds));
            }
        }

        // 3. Candidates that are stable and can be added now use up capacity in order.
        if (!snapshot.CanAddNow)
        {
            return Array.Empty<DragOutDecision>();
        }

        var decisions = new List<DragOutDecision>();
        int free = Math.Max(0, snapshot.FreeSlots);
        for (int i = 0; i < _waiting.Count;)
        {
            Candidate c = _waiting[i];
            if (c.StableSince is TimeSpan since && snapshot.Now - since >= StableTime)
            {
                if (free > 0)
                {
                    decisions.Add(new DragOutDecision(c.Id, DragOutDecisionKind.Add));
                    free--;
                }
                else
                {
                    decisions.Add(new DragOutDecision(c.Id, DragOutDecisionKind.Full));
                }

                _waiting.RemoveAt(i);
            }
            else
            {
                i++;
            }
        }

        return decisions;
    }

    private sealed class Candidate
    {
        public Candidate(long id, CellRect bounds)
        {
            Id = id;
            Bounds = bounds;
        }

        public long Id { get; }

        public CellRect Bounds { get; set; }

        /// <summary>When the button was released with position and size unchanged; null while held, reset to the current time when position or size changes after release.</summary>
        public TimeSpan? StableSince { get; set; }
    }
}
