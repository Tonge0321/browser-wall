using BrowserWall.Core.Detect;
using BrowserWall.Core.Layout;
using BrowserWall.Core.Session;
using BrowserWall.Core.State;

namespace BrowserWall.Tests;

public class DragOutWatcherTests
{
    private const uint WallPid = 7;
    private const string Chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

    private static readonly CellRect RectA = new(100, 100, 800, 600);
    private static readonly CellRect RectB = new(140, 120, 800, 600);

    private static DragOutWindow Win(long id, CellRect rect, uint? pid = WallPid, string cls = WindowDetector.BrowserWindowClass, bool visible = true) =>
        new(new WindowInfo(id, Chrome, cls, "Tab", visible, pid), rect);

    private static DragOutSnapshot Snap(
        double seconds,
        bool down,
        DragOutWindow[] windows,
        long[]? managed = null,
        long[]? awaited = null,
        bool canAdd = true,
        int free = 10,
        uint? wallPid = WallPid,
        long[]? existing = null) =>
        new(TimeSpan.FromSeconds(seconds), windows, down, wallPid, managed ?? Array.Empty<long>(), awaited ?? Array.Empty<long>(), canAdd, free,
            existing ?? windows.Select(w => w.Info.Id).ToArray());

    private static DragOutSnapshot Snap(WallState state, double seconds, bool down, params DragOutWindow[] windows) =>
        new(TimeSpan.FromSeconds(seconds), windows, down, WallPid, state.WindowIds.ToList(), Array.Empty<long>(), state.CanAutoAdd, state.FreeSlotCount,
            windows.Select(w => w.Info.Id).ToArray());

    [Fact(DisplayName = "AutoAdd_OnlyWhenFirstSeenWithButtonDown")]
    public void AutoAdd_OnlyWhenFirstSeenWithButtonDown()
    {
        var watcher = new DragOutWatcher(new long[] { 1 });

        // Left button down when first seen, owned by the wall's browser process: a dragged-out window, starts waiting.
        Assert.Empty(watcher.Poll(Snap(0, down: true, new[] { Win(1, RectA), Win(10, RectA) })));
        Assert.Equal(new long[] { 10 }, watcher.WaitingIds);

        // Left button not down when first seen (popup, Ctrl+N): never added, even after a later press, release and settling.
        // (Window 1, present before startup, is listed every time because it keeps existing; if it were not listed it would be
        // treated as gone and its "seen" record cleared.)
        Assert.Empty(watcher.Poll(Snap(0.1, down: false, new[] { Win(1, RectA), Win(10, RectA), Win(20, RectA) })));
        Assert.DoesNotContain(20L, watcher.WaitingIds);
        Assert.Empty(watcher.Poll(Snap(0.2, down: true, new[] { Win(1, RectA), Win(10, RectA), Win(20, RectA) })));

        // Appears while the left button is down but fails another condition: other process, unknown process, other class, managed, awaited, present before startup.
        DragOutWindow[] others =
        {
            Win(10, RectA), Win(20, RectA), Win(1, RectA),
            Win(30, RectA, pid: 99), Win(31, RectA, pid: null), Win(32, RectA, cls: "Other"),
            Win(33, RectA), Win(34, RectA),
        };
        Assert.Empty(watcher.Poll(Snap(0.3, down: true, others, managed: new long[] { 33 }, awaited: new long[] { 34 })));
        Assert.Equal(new long[] { 10 }, watcher.WaitingIds);

        // All released and stable: only 10 is added.
        var all = new List<DragOutDecision>();
        for (double t = 0.4; t <= 3; t += 0.1)
        {
            all.AddRange(watcher.Poll(Snap(t, down: false, others, managed: new long[] { 33 }, awaited: new long[] { 34 })));
        }

        Assert.Equal(new[] { new DragOutDecision(10, DragOutDecisionKind.Add) }, all);

        // Invisible does not count as first seen; the mouse is checked when it becomes visible.
        var hidden = new DragOutWatcher(Array.Empty<long>());
        Assert.Empty(hidden.Poll(Snap(0, down: false, new[] { Win(40, RectA, visible: false) })));
        Assert.Empty(hidden.Poll(Snap(0.1, down: true, new[] { Win(40, RectA) })));
        Assert.Equal(new long[] { 40 }, hidden.WaitingIds);

        // No wall browser process: no effect.
        var noWall = new DragOutWatcher(Array.Empty<long>());
        Assert.Empty(noWall.Poll(Snap(0, down: true, new[] { Win(50, RectA) }, wallPid: null)));
        Assert.Empty(noWall.WaitingIds);
    }

    [Fact(DisplayName = "AutoAdd_WaitsForReleaseAndOneSecondStable")]
    public void AutoAdd_WaitsForReleaseAndOneSecondStable()
    {
        var watcher = new DragOutWatcher(Array.Empty<long>());
        Assert.Empty(watcher.Poll(Snap(0, down: true, new[] { Win(10, RectA) })));

        // Held still for 2 seconds: time while held does not count.
        Assert.Empty(watcher.Poll(Snap(1, down: true, new[] { Win(10, RectA) })));
        Assert.Empty(watcher.Poll(Snap(2, down: true, new[] { Win(10, RectA) })));

        // After release (2.1 s) the window still moves (to RectB at 2.5 s): timing restarts.
        Assert.Empty(watcher.Poll(Snap(2.1, down: false, new[] { Win(10, RectA) })));
        Assert.Empty(watcher.Poll(Snap(2.5, down: false, new[] { Win(10, RectB) })));
        Assert.Empty(watcher.Poll(Snap(3.4, down: false, new[] { Win(10, RectB) })));   // 0.9 s

        IReadOnlyList<DragOutDecision> decided = watcher.Poll(Snap(3.5, down: false, new[] { Win(10, RectB) }));
        Assert.Equal(new[] { new DragOutDecision(10, DragOutDecisionKind.Add) }, decided);
        Assert.Empty(watcher.WaitingIds);

        // Returned only once.
        Assert.Empty(watcher.Poll(Snap(5, down: false, new[] { Win(10, RectB) })));

        // Dragged out in expanded mode: after adding, the expanded window stays expanded; no expand or foreground action is returned.
        var state = new WallState();
        state.AddWindow(1, "https://example.com/1");
        state.AddWindow(2, "https://example.com/2");
        state.ClickCell(1);
        Assert.Equal(
            new[] { WallAction.Park(10), WallAction.HideTaskbarButton(10), WallAction.Relayout },
            state.AutoAddWindow(10));
        Assert.Equal(WallMode.Expanded, state.Mode);
        Assert.Equal((long?)2, state.ExpandedWindowId);
        Assert.Null(state.Slots[^1].SessionEntry);
        Assert.Equal(string.Empty, state.Slots[^1].Url);
        Assert.Empty(state.AutoAddWindow(10));

        // Wall minimized while stable: no decision returned, the candidate is kept; added only after the restore and its actions are done.
        var grid = new WallState();
        grid.AddWindow(1, "https://example.com/1");
        var w2 = new DragOutWatcher(new long[] { 1 });
        DragOutWindow[] wins = { Win(1, RectA), Win(20, RectA) };
        Assert.Empty(w2.Poll(Snap(grid, 0, true, wins)));
        Assert.Empty(w2.Poll(Snap(grid, 0.1, false, wins)));
        grid.WallMinimized();
        Assert.False(grid.CanAutoAdd);
        Assert.Empty(w2.Poll(Snap(grid, 1.5, false, wins)));
        Assert.Empty(w2.Poll(Snap(grid, 5, false, wins)));
        Assert.Equal(new long[] { 20 }, w2.WaitingIds);

        grid.WallRestored();
        Assert.True(grid.RestorePending);
        Assert.Empty(w2.Poll(Snap(grid, 5.1, false, wins)));
        grid.SettleRestore();
        IReadOnlyList<DragOutDecision> afterRestore = w2.Poll(Snap(grid, 5.2, false, wins));
        Assert.Equal(new[] { new DragOutDecision(20, DragOutDecisionKind.Add) }, afterRestore);
        Assert.NotEmpty(grid.AutoAddWindow(20));
        Assert.Equal(new long[] { 1, 20 }, grid.WindowIds);
    }

    [Fact(DisplayName = "AutoAdd_SkipsWhenFull")]
    public void AutoAdd_SkipsWhenFull()
    {
        // Wall full: Full is returned once stable, not Add.
        var watcher = new DragOutWatcher(Array.Empty<long>());
        Assert.Empty(watcher.Poll(Snap(0, down: true, new[] { Win(10, RectA) }, free: 0)));
        Assert.Empty(watcher.Poll(Snap(0.1, down: false, new[] { Win(10, RectA) }, free: 0)));
        Assert.Equal(
            new[] { new DragOutDecision(10, DragOutDecisionKind.Full) },
            watcher.Poll(Snap(1.1, down: false, new[] { Win(10, RectA) }, free: 0)));
        Assert.Empty(watcher.WaitingIds);

        // The state machine with 16 cells does not add it.
        var full = new WallState();
        for (long id = 1; id <= 16; id++)
        {
            full.AddWindow(id, $"https://example.com/{id}");
        }

        Assert.Empty(full.AutoAddWindow(10));
        Assert.Equal(16, full.Slots.Count);

        // 15 cells, two candidates stable at the same poll: the first detected is added, the other gets Full.
        var state = new WallState();
        for (long id = 1; id <= 15; id++)
        {
            state.AddWindow(id, $"https://example.com/{id}");
        }

        var two = new DragOutWatcher(state.WindowIds);
        Assert.Empty(two.Poll(Snap(state, 0, true, Win(30, RectA))));
        Assert.Empty(two.Poll(Snap(state, 0.05, true, Win(30, RectA), Win(31, RectB))));
        Assert.Empty(two.Poll(Snap(state, 0.1, false, Win(30, RectA), Win(31, RectB))));
        IReadOnlyList<DragOutDecision> decisions = two.Poll(Snap(state, 1.1, false, Win(30, RectA), Win(31, RectB)));
        Assert.Equal(
            new[] { new DragOutDecision(30, DragOutDecisionKind.Add), new DragOutDecision(31, DragOutDecisionKind.Full) },
            decisions);

        foreach (DragOutDecision d in decisions.Where(d => d.Kind == DragOutDecisionKind.Add))
        {
            Assert.NotEmpty(state.AutoAddWindow(d.WindowId));
        }

        Assert.Equal(16, state.Slots.Count);
        Assert.DoesNotContain(31L, state.WindowIds);
    }

    [Fact(DisplayName = "AutoAdd_WindowGoneWhileWaitingIsDropped")]
    public void AutoAdd_WindowGoneWhileWaitingIsDropped()
    {
        // Disappears while waiting (for example, the tab was dragged back into another window): dropped, no decision returned.
        var watcher = new DragOutWatcher(Array.Empty<long>());
        Assert.Empty(watcher.Poll(Snap(0, down: true, new[] { Win(10, RectA) })));
        Assert.Equal(new long[] { 10 }, watcher.WaitingIds);
        // The "seen" record is cleared when a window no longer exists, so here the window is only missing from this
        // enumeration and still exists (handle reuse after it is really gone is asserted by DragOut_SeenClearedWhenWindowGone).
        Assert.Empty(watcher.Poll(Snap(0.3, down: true, Array.Empty<DragOutWindow>(), existing: new long[] { 10 })));
        Assert.Empty(watcher.WaitingIds);

        // The same handle appears again later (seen, still exists): not added either.
        for (double t = 0.4; t <= 3; t += 0.1)
        {
            Assert.Empty(watcher.Poll(Snap(t, down: t < 0.6, new[] { Win(10, RectA) })));
        }

        // Becomes invisible: dropped as well.
        var hidden = new DragOutWatcher(Array.Empty<long>());
        Assert.Empty(hidden.Poll(Snap(0, down: true, new[] { Win(20, RectA) })));
        Assert.Empty(hidden.Poll(Snap(0.1, down: false, new[] { Win(20, RectA, visible: false) })));
        Assert.Empty(hidden.WaitingIds);
        Assert.Empty(hidden.Poll(Snap(2, down: false, new[] { Win(20, RectA) })));

        // Added manually while waiting (became managed): dropped, not added twice.
        var adopted = new DragOutWatcher(Array.Empty<long>());
        Assert.Empty(adopted.Poll(Snap(0, down: true, new[] { Win(30, RectA) })));
        Assert.Empty(adopted.Poll(Snap(0.1, down: false, new[] { Win(30, RectA) })));
        Assert.Empty(adopted.Poll(Snap(0.5, down: false, new[] { Win(30, RectA) }, managed: new long[] { 30 })));
        Assert.Empty(adopted.WaitingIds);
        Assert.Empty(adopted.Poll(Snap(2, down: false, new[] { Win(30, RectA) })));
    }

    [Fact(DisplayName = "AutoAdd_RememberedAfterUrlRecorded")]
    public void AutoAdd_RememberedAfterUrlRecorded()
    {
        // An automatically added cell joins the list once a URL is recorded.
        string[] urls = { "https://example.com/a", "https://example.com/b" };
        var store = new FakeSessionStore();
        var state = new WallState();
        var tracker = new SessionTracker(store, urls);
        state.AddWindow(100, urls[0], tracker.StartupEntry(0));
        state.AddWindow(101, urls[1], tracker.StartupEntry(1));
        tracker.Sync(state.Slots);
        Assert.Empty(store.Writes);

        // Added automatically: no URL recorded yet, the list is not written or changed; the cell has no list entry and an empty URL.
        Assert.NotEmpty(state.AutoAddWindow(300));
        tracker.Sync(state.Slots);
        Assert.Empty(store.Writes);
        Assert.Equal(urls, tracker.Urls);
        Assert.Null(state.Slots[2].SessionEntry);
        Assert.Equal(string.Empty, state.Slots[2].Url);

        // Swap the auto-added cell with cell 1 (A, auto, B): cells with URLs keep their relative order, the list is not rewritten.
        Assert.NotEmpty(state.SwapCells(2, 1));
        tracker.Sync(state.Slots);
        Assert.Empty(store.Writes);
        Assert.Equal(urls, tracker.Urls);

        // URL recorded: joins in cell order (A, dragged out, B).
        Assert.True(new LastSeenUrl().Record(state, 300, "https://example.com/dragged", false, TimeSpan.Zero));
        tracker.Sync(state.Slots);
        Assert.Equal(new[] { urls[0], "https://example.com/dragged", urls[1] }, store.LastWritten());

        // Close the auto-added cell: its URL is removed.
        Assert.NotEmpty(state.WindowClosed(300));
        tracker.Sync(state.Slots);
        Assert.Equal(urls, store.LastWritten());

        // Close a cell with a URL: its URL is removed as usual.
        Assert.NotEmpty(state.WindowClosed(100));
        tracker.Sync(state.Slots);
        Assert.Equal(new[] { urls[1] }, store.LastWritten());
    }

    [Fact(DisplayName = "DragOut_SeenClearedWhenWindowGone")]
    public void DragOut_SeenClearedWhenWindowGone()
    {
        var watcher = new DragOutWatcher(new long[] { 10 });
        Assert.Contains(10L, watcher.SeenIds);

        // Only invisible this time and still exists: not cleared (cleared only when gone).
        Assert.Empty(watcher.Poll(Snap(0, down: false, new[] { Win(10, RectA, visible: false) })));
        Assert.Contains(10L, watcher.SeenIds);
        Assert.Empty(watcher.Poll(Snap(0.05, down: true, Array.Empty<DragOutWindow>(), existing: new long[] { 10 })));
        Assert.Contains(10L, watcher.SeenIds);
        Assert.Empty(watcher.Poll(Snap(0.1, down: true, new[] { Win(10, RectA) })));
        Assert.Empty(watcher.WaitingIds);

        // Window gone: the "seen" record is cleared.
        Assert.Empty(watcher.Poll(Snap(0.2, down: false, Array.Empty<DragOutWindow>())));
        Assert.DoesNotContain(10L, watcher.SeenIds);

        // Handle reused, left button down when first seen: treated as dragged out, added once stable.
        Assert.Empty(watcher.Poll(Snap(0.3, down: true, new[] { Win(10, RectA) })));
        Assert.Equal(new long[] { 10 }, watcher.WaitingIds);
        Assert.Empty(watcher.Poll(Snap(0.4, down: false, new[] { Win(10, RectA) })));
        Assert.Equal(
            new[] { new DragOutDecision(10, DragOutDecisionKind.Add) },
            watcher.Poll(Snap(1.4, down: false, new[] { Win(10, RectA) })));

        // Handle reused, but left button not down when first seen (for example, Ctrl+N): not added.
        Assert.Empty(watcher.Poll(Snap(2, down: false, Array.Empty<DragOutWindow>())));
        Assert.DoesNotContain(10L, watcher.SeenIds);
        Assert.Empty(watcher.Poll(Snap(2.1, down: false, new[] { Win(10, RectA) })));
        for (double t = 2.2; t <= 4; t += 0.1)
        {
            Assert.Empty(watcher.Poll(Snap(t, down: t < 2.5, new[] { Win(10, RectA) })));
        }

        Assert.Empty(watcher.WaitingIds);
    }

    [Fact(DisplayName = "AutoAdd_LaunchWindowClaimedFirst")]
    public void AutoAdd_LaunchWindowClaimedFirst()
    {
        string[] urls = { "https://example.com/a" };
        var store = new FakeSessionStore();
        var state = new WallState();
        var tracker = new SessionTracker(store, urls);
        state.AddWindow(100, urls[0], tracker.StartupEntry(0));
        tracker.Sync(state.Slots);

        var watcher = new DragOutWatcher(new long[] { 100 });

        // "+ Add page" pressed and waiting for the new window (the App passes CanAddNow = CanAutoAdd && !AddBusy = false).
        Assert.Null(AddPageFlow.Begin(state, launching: false));
        bool busy = true;
        DragOutSnapshot At(double t, bool down, params DragOutWindow[] extra)
        {
            List<DragOutWindow> wins = new[] { Win(100, RectA) }.Concat(extra).ToList();
            return new(TimeSpan.FromSeconds(t), wins, down, WallPid,
                state.WindowIds.ToList(), Array.Empty<long>(), state.CanAutoAdd && !busy, state.FreeSlotCount,
                wins.Select(w => w.Info.Id).ToList());
        }

        // Window 200 opened by this program appears just while the left button is down; the user also drags out 201.
        Assert.Empty(watcher.Poll(At(0, true, Win(200, RectA), Win(201, RectB))));
        Assert.Equal(new long[] { 200, 201 }, watcher.WaitingIds);

        // While waiting: not added even when stable for over 1 second.
        for (double t = 0.1; t <= 2; t += 0.1)
        {
            Assert.Empty(watcher.Poll(At(t, false, Win(200, RectA), Win(201, RectB))));
        }

        // The detector claims 200: waiting ends and it is registered in the same code path; the new cell starts without a URL
        // and joins the list once a URL is recorded.
        busy = false;
        AddPageFlow.Finish(state, tracker, windowId: 200);
        Assert.Empty(store.Writes);
        Assert.True(new LastSeenUrl().Record(state, 200, "time.is", false, TimeSpan.Zero));
        tracker.Sync(state.Slots);
        Assert.Equal(new[] { urls[0], "https://time.is" }, store.LastWritten());

        // Next poll: 200 is managed and dropped; only Add for 201 is returned.
        IReadOnlyList<DragOutDecision> decisions = watcher.Poll(At(2.1, false, Win(200, RectA), Win(201, RectB)));
        Assert.Equal(new[] { new DragOutDecision(201, DragOutDecisionKind.Add) }, decisions);
        Assert.Empty(watcher.WaitingIds);

        Assert.NotEmpty(state.AutoAddWindow(201));
        tracker.Sync(state.Slots);
        Assert.Equal(new long[] { 100, 200, 201 }, state.WindowIds);
        Assert.Equal(new[] { urls[0], "https://time.is" }, tracker.Urls);
        Assert.Equal(new[] { urls[0], "https://time.is" }, store.LastWritten());
    }
}
