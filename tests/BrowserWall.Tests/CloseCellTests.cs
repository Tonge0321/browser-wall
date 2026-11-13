using BrowserWall.Core.Session;
using BrowserWall.Core.State;

namespace BrowserWall.Tests;

public class CloseCellTests
{
    private static WallState Grid(params long[] ids)
    {
        var state = new WallState();
        foreach (long id in ids)
        {
            state.AddWindow(id, $"https://example.com/{id}");
        }

        return state;
    }

    private static void AssertNoMinimize(IEnumerable<WallAction> actions) =>
        Assert.DoesNotContain(actions, a => a.Kind == WallActionKind.Minimize);

    [Fact(DisplayName = "Close_RemovesCellAndUrl")]
    public void Close_RemovesCellAndUrl()
    {
        var store = new FakeSessionStore();
        string[] urls = { "https://example.com/a", "https://example.com/b" };
        var state = new WallState();
        var tracker = new SessionTracker(store, urls);
        state.AddWindow(100, urls[0], tracker.StartupEntry(0));
        state.AddWindow(101, urls[1], tracker.StartupEntry(1));
        tracker.Sync(state.Slots);

        // Press "Close": a close request is sent and the cell stays; pressing again sends nothing.
        IReadOnlyList<WallAction> close = state.CloseCell(100);
        Assert.Equal(new[] { WallAction.CloseWindow(100) }, close);
        Assert.Empty(state.CloseCell(100));
        Assert.Equal(2, state.Slots.Count);
        tracker.Sync(state.Slots);
        Assert.Empty(store.Writes);

        // The window disappears: the cell is removed, relayout, and the list drops the URL and is rewritten.
        Assert.Equal(new[] { WallAction.Relayout }, state.WindowClosed(100));
        tracker.Sync(state.Slots);
        Assert.Equal(new long[] { 101 }, state.WindowIds);
        Assert.Equal(new[] { urls[1] }, store.LastWritten());

        // No longer on the wall: no action on timeout either.
        Assert.Empty(state.CloseStillOpen(100));

        // Still there after 2 seconds: expand it so the user can deal with it.
        Assert.NotEmpty(state.CloseCell(101));
        IReadOnlyList<WallAction> expand = state.CloseStillOpen(101);
        Assert.Equal(new[] { WallAction.Expand(101), WallAction.ShowShrinkButton }, expand);
        Assert.Equal(WallMode.Expanded, state.Mode);
        Assert.Equal((long?)101, state.ExpandedWindowId);
        AssertNoMinimize(close.Concat(expand));

        // Expanded mode, wall minimized, wall closing: "Close" does nothing.
        Assert.Empty(state.CloseCell(101));
        WallState minimized = Grid(1, 2);
        minimized.WallMinimized();
        Assert.Empty(minimized.CloseCell(1));
        WallState exiting = Grid(1);
        exiting.BeginExit();
        Assert.Empty(exiting.CloseCell(1));

        // On a failed cell, "Close" is the same as "Pop out": the cell and its URL are removed.
        var failedState = new WallState();
        failedState.AddFailed("https://example.com/f");
        Assert.Equal(new[] { WallAction.ShowEmpty }, failedState.ReleaseFailed(0));
    }

    [Fact(DisplayName = "Close_TimeoutSwitchesFromOtherExpanded")]
    public void Close_TimeoutSwitchesFromOtherExpanded()
    {
        // Close A, then expand B right away; A times out → B is parked and A expanded; still only one expanded.
        WallState state = Grid(1, 2, 3);
        Assert.NotEmpty(state.CloseCell(1));
        state.ClickCell(1);
        Assert.Equal((long?)2, state.ExpandedWindowId);

        IReadOnlyList<WallAction> actions = state.CloseStillOpen(1);
        Assert.Equal(new[] { WallAction.Park(2), WallAction.Expand(1), WallAction.ShowShrinkButton }, actions);
        Assert.Equal(WallMode.Expanded, state.Mode);
        Assert.Equal((long?)1, state.ExpandedWindowId);
        Assert.Empty(state.CloseExpandQueue);
        AssertNoMinimize(actions);
    }

    [Fact(DisplayName = "Close_MultipleTimeoutsInOrder")]
    public void Close_MultipleTimeoutsInOrder()
    {
        // Closing A and B both time out: A is expanded first and B waits; after A is handled (closed or shrunk), B is expanded.
        WallState state = Grid(1, 2, 3);
        Assert.NotEmpty(state.CloseCell(1));
        Assert.NotEmpty(state.CloseCell(2));

        Assert.Equal(new[] { WallAction.Expand(1), WallAction.ShowShrinkButton }, state.CloseStillOpen(1));
        Assert.Empty(state.CloseStillOpen(2));
        Assert.Equal(new long[] { 2 }, state.CloseExpandQueue);
        Assert.Empty(state.ExpandTimedOutClose());
        Assert.Equal((long?)1, state.ExpandedWindowId);

        // The user closes A: the next handling expands B.
        state.WindowClosed(1);
        Assert.Equal(WallMode.Grid, state.Mode);
        Assert.Equal(new[] { WallAction.Expand(2), WallAction.ShowShrinkButton }, state.ExpandTimedOutClose());
        Assert.Equal((long?)2, state.ExpandedWindowId);
        Assert.Empty(state.CloseExpandQueue);

        // Shrinking also counts as handled.
        WallState other = Grid(1, 2, 3);
        other.CloseCell(1);
        other.CloseCell(3);
        other.CloseStillOpen(1);
        other.CloseStillOpen(3);
        other.Shrink();
        Assert.Equal(new[] { WallAction.Expand(3), WallAction.ShowShrinkButton }, other.ExpandTimedOutClose());
    }

    [Fact(DisplayName = "Close_ShrinkThenReexpandDoesNotBlockQueue")]
    public void Close_ShrinkThenReexpandDoesNotBlockQueue()
    {
        // A and B both time out, A expanded and B waiting; the user shrinks and opens A again before the next handling: B must still follow.
        WallState state = Grid(1, 2, 3);
        state.CloseCell(1);
        state.CloseCell(2);
        Assert.Equal(new[] { WallAction.Expand(1), WallAction.ShowShrinkButton }, state.CloseStillOpen(1));
        Assert.Empty(state.CloseStillOpen(2));

        state.Shrink();
        Assert.NotEmpty(state.ClickCell(0));
        Assert.Equal((long?)1, state.ExpandedWindowId);

        // This time the user expanded A, so it no longer counts as a close-timeout expand: B switches in; still only one expanded.
        Assert.Equal(new[] { WallAction.Park(1), WallAction.Expand(2), WallAction.ShowShrinkButton }, state.ExpandTimedOutClose());
        Assert.Equal((long?)2, state.ExpandedWindowId);
        Assert.Empty(state.CloseExpandQueue);

        // The wall becoming active (same as shrink) or the expanded window being minimized (same as shrink) also ends that timeout expand at once.
        foreach (bool viaActivation in new[] { true, false })
        {
            WallState s = Grid(1, 2, 3);
            s.CloseCell(1);
            s.CloseCell(3);
            s.CloseStillOpen(1);
            s.CloseStillOpen(3);
            Assert.NotEmpty(viaActivation ? s.WallActivated() : s.WindowMinimized(1));
            Assert.Equal(WallMode.Grid, s.Mode);
            Assert.NotEmpty(s.ClickCell(0));
            Assert.Equal(new[] { WallAction.Park(1), WallAction.Expand(3), WallAction.ShowShrinkButton }, s.ExpandTimedOutClose());
        }
    }

    [Fact(DisplayName = "Close_TimeoutWhileMinimizedKept")]
    public void Close_TimeoutWhileMinimizedKept()
    {
        // Timeout while the wall is minimized: kept, and expanded after the restore and its actions are done.
        WallState state = Grid(1, 2);
        Assert.NotEmpty(state.CloseCell(1));
        state.WallMinimized();
        Assert.Empty(state.CloseStillOpen(1));
        Assert.Equal(new long[] { 1 }, state.CloseExpandQueue);

        state.WallRestored();
        Assert.True(state.RestorePending);
        Assert.Empty(state.ExpandTimedOutClose());
        state.SettleRestore();
        Assert.Equal(new[] { WallAction.Expand(1), WallAction.ShowShrinkButton }, state.ExpandTimedOutClose());
        Assert.Equal((long?)1, state.ExpandedWindowId);

        // Also kept while the wall's title bar minimize or close button is pressed.
        WallState caption = Grid(1, 2);
        caption.CloseCell(2);
        caption.CaptionPressStarted();
        Assert.Empty(caption.CloseStillOpen(2));
        Assert.Equal(new long[] { 2 }, caption.CloseExpandQueue);
        caption.CaptionPressEnded(wallStillActive: false);
        Assert.Equal(new[] { WallAction.Expand(2), WallAction.ShowShrinkButton }, caption.ExpandTimedOutClose());

        // The window disappears while waiting: removed from the queue.
        WallState gone = Grid(1, 2);
        gone.CloseCell(1);
        gone.WallMinimized();
        gone.CloseStillOpen(1);
        gone.SyncAlive(new long[] { 2 });
        Assert.Empty(gone.CloseExpandQueue);
    }
}
