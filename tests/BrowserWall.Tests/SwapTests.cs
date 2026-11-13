using BrowserWall.Core.Layout;
using BrowserWall.Core.Session;
using BrowserWall.Core.State;

namespace BrowserWall.Tests;

public class SwapTests
{
    [Fact(DisplayName = "Swap_ExchangesCellsAndSessionOrder")]
    public void Swap_ExchangesCellsAndSessionOrder()
    {
        string[] urls = { "https://example.com/a", "https://example.com/b", "https://example.com/c" };
        var store = new FakeSessionStore();
        var state = new WallState();
        var tracker = new SessionTracker(store, urls);
        state.AddWindow(100, urls[0], tracker.StartupEntry(0));
        state.AddWindow(101, urls[1], tracker.StartupEntry(1));
        state.AddFailed(urls[2], tracker.StartupEntry(2));
        tracker.Sync(state.Slots);
        Assert.Empty(store.Writes);

        // Swap cell 0 (A) and cell 2 (failed C).
        Assert.Equal(new[] { WallAction.Relayout }, state.SwapCells(0, 2));
        Assert.Equal(WallSlotKind.Failed, state.Slots[0].Kind);
        Assert.Equal(urls[2], state.Slots[0].Url);
        Assert.Equal(urls[1], state.Slots[1].Url);
        Assert.Equal((long?)100, state.Slots[2].WindowId);
        Assert.Equal(new long[] { 101, 100 }, state.WindowIds);
        Assert.Equal(WallMode.Grid, state.Mode);

        // The list is rewritten in full in the new cell order.
        tracker.Sync(state.Slots);
        Assert.Single(store.Writes);
        Assert.Equal(new[] { urls[2], urls[1], urls[0] }, store.LastWritten());
        Assert.Equal(new[] { urls[2], urls[1], urls[0] }, tracker.Urls);

        // No rewrite when the order did not change.
        tracker.Sync(state.Slots);
        Assert.Single(store.Writes);

        // Swapping an added cell (no URL): the list order is not affected by it.
        Assert.NotEmpty(state.AdoptWindow(300));
        Assert.Equal(new[] { WallAction.Relayout }, state.SwapCells(3, 0));
        tracker.Sync(state.Slots);
        Assert.Equal(2, store.Writes.Count);
        Assert.Equal(new[] { urls[1], urls[0], urls[2] }, store.LastWritten());
    }

    private static (WallState State, SessionTracker Tracker, FakeSessionStore Store, string[] Urls) ThreeCells()
    {
        string[] urls = { "https://example.com/a", "https://example.com/b", "https://example.com/c" };
        var store = new FakeSessionStore();
        var state = new WallState();
        var tracker = new SessionTracker(store, urls);
        state.AddWindow(100, urls[0], tracker.StartupEntry(0));
        state.AddFailed(urls[1], tracker.StartupEntry(1));
        state.AddWindow(102, urls[2], tracker.StartupEntry(2));
        tracker.Sync(state.Slots);
        return (state, tracker, store, urls);
    }

    [Fact(DisplayName = "Swap_DropOutsideNoChange")]
    public void Swap_DropOutsideNoChange()
    {
        (WallState state, SessionTracker tracker, FakeSessionStore store, string[] urls) = ThreeCells();
        List<WallSlot> before = state.Slots.ToList();

        // Dragged and dropped outside the cells, outside the wall (-1) or on the same cell: the gesture result is no change.
        foreach (int drop in new[] { -1, 0 })
        {
            var g = new CellDragGesture();
            g.Press(0, 10, 10);
            Assert.True(g.Move(200, 10));
            Assert.Equal(CellDragResult.None, g.Release(drop));
        }

        // The state machine also rejects outside, out of range and the same cell.
        Assert.Empty(state.SwapCells(0, -1));
        Assert.Empty(state.SwapCells(-1, 0));
        Assert.Empty(state.SwapCells(0, 3));
        Assert.Empty(state.SwapCells(1, 1));
        Assert.Equal(before, state.Slots);

        tracker.Sync(state.Slots);
        Assert.Empty(store.Writes);
        Assert.Equal(urls, tracker.Urls);
    }

    [Fact(DisplayName = "Swap_NotAllowedWhileExpanded")]
    public void Swap_NotAllowedWhileExpanded()
    {
        (WallState state, SessionTracker tracker, FakeSessionStore store, string[] urls) = ThreeCells();
        List<WallSlot> before = state.Slots.ToList();

        // No swapping in expanded mode.
        state.ClickCell(0);
        Assert.Equal(WallMode.Expanded, state.Mode);
        Assert.Empty(state.SwapCells(0, 2));
        Assert.Equal(before, state.Slots);
        Assert.Equal(WallMode.Expanded, state.Mode);
        Assert.Equal((long?)100, state.ExpandedWindowId);
        state.Shrink();

        // Not while the wall is minimized or a restore is pending either.
        state.WallMinimized();
        Assert.Empty(state.SwapCells(0, 2));
        state.WallRestored();
        Assert.True(state.RestorePending);
        Assert.Empty(state.SwapCells(0, 2));
        Assert.Equal(before, state.Slots);
        tracker.Sync(state.Slots);
        Assert.Empty(store.Writes);

        // After the restore completes and grid mode is back, the same swap works.
        state.SettleRestore();
        Assert.Equal(new[] { WallAction.Relayout }, state.SwapCells(0, 2));
        tracker.Sync(state.Slots);
        Assert.Equal(new[] { urls[2], urls[1], urls[0] }, store.LastWritten());
    }
}
