using BrowserWall.Core.State;

namespace BrowserWall.Tests;

public class WallStateTests
{
    private static WallState WithWindows(params long[] ids)
    {
        var state = new WallState();
        foreach (long id in ids)
        {
            state.AddWindow(id, $"https://example.com/{id}");
        }

        return state;
    }

    [Fact(DisplayName = "State_ClickCell_Expands")]
    public void ClickCell_Expands()
    {
        WallState state = WithWindows(11, 22, 33, 44);

        IReadOnlyList<WallAction> actions = state.ClickCell(1);

        Assert.Equal(WallMode.Expanded, state.Mode);
        Assert.Equal(22, state.ExpandedWindowId);
        Assert.Equal(new[] { WallAction.Expand(22), WallAction.ShowShrinkButton }, actions);
    }

    [Fact(DisplayName = "State_Shrink_ReturnsToGrid")]
    public void Shrink_ReturnsToGrid()
    {
        WallState state = WithWindows(11, 22);
        state.ClickCell(0);

        IReadOnlyList<WallAction> actions = state.Shrink();

        Assert.Equal(WallMode.Grid, state.Mode);
        Assert.Null(state.ExpandedWindowId);
        Assert.Equal(new[] { WallAction.Park(11), WallAction.HideShrinkButton }, actions);

        Assert.Empty(state.Shrink());
        Assert.Equal(WallMode.Grid, state.Mode);
    }

    [Fact(DisplayName = "State_OnlyOneExpanded")]
    public void OnlyOneExpanded()
    {
        WallState state = WithWindows(11, 22, 33);
        state.ClickCell(0);

        Assert.Empty(state.ClickCell(2));
        Assert.Equal(WallMode.Expanded, state.Mode);
        Assert.Equal(11, state.ExpandedWindowId);

        state.Shrink();
        Assert.Equal(new[] { WallAction.Expand(33), WallAction.ShowShrinkButton }, state.ClickCell(2));
        Assert.Equal(33, state.ExpandedWindowId);
    }

    [Fact(DisplayName = "State_ExpandedWindowClosed_ReturnsToGrid")]
    public void ExpandedWindowClosed_ReturnsToGrid()
    {
        WallState state = WithWindows(11, 22, 33);
        state.ClickCell(1);

        IReadOnlyList<WallAction> actions = state.WindowClosed(22);

        Assert.Equal(WallMode.Grid, state.Mode);
        Assert.Null(state.ExpandedWindowId);
        Assert.Equal(new long[] { 11, 33 }, state.WindowIds);
        Assert.Equal(new[] { WallAction.HideShrinkButton, WallAction.Relayout }, actions);
        Assert.DoesNotContain(actions, a => a.Kind == WallActionKind.Park);
    }

    [Fact(DisplayName = "State_AllWindowsClosed_ShowsEmpty")]
    public void AllWindowsClosed_ShowsEmpty()
    {
        WallState state = WithWindows(11, 22);
        state.AddFailed("https://failed.example.com");

        Assert.Equal(new[] { WallAction.Relayout }, state.WindowClosed(11));
        Assert.Equal(WallMode.Grid, state.Mode);
        Assert.Equal(2, state.Slots.Count);

        // The last managed window is closed: only that cell is removed, the failed cell stays;
        // the screen also shows "no windows" (HasManagedWindows = false).
        IReadOnlyList<WallAction> actions = state.WindowClosed(22);
        Assert.Equal(WallMode.Grid, state.Mode);
        Assert.Equal(new[] { WallSlot.ForFailed("https://failed.example.com") }, state.Slots);
        Assert.False(state.HasManagedWindows);
        Assert.Equal(new[] { WallAction.Relayout }, actions);

        // The last one is closed while expanded
        WallState expanded = WithWindows(55);
        expanded.ClickCell(0);
        IReadOnlyList<WallAction> expandedActions = expanded.WindowClosed(55);
        Assert.Equal(WallMode.Empty, expanded.Mode);
        Assert.Null(expanded.ExpandedWindowId);
        Assert.Empty(expanded.Slots);
        Assert.Equal(new[] { WallAction.HideShrinkButton, WallAction.ShowEmpty }, expandedActions);
    }

    [Fact(DisplayName = "State_SingleUrlFailed_ShowsFailedCell")]
    public void SingleUrlFailed_ShowsFailedCell()
    {
        var state = new WallState();

        Assert.Equal(new[] { WallAction.Relayout }, state.AddFailed("https://a.example.com"));

        Assert.Equal(WallMode.Grid, state.Mode);
        WallSlot slot = Assert.Single(state.Slots);
        Assert.Equal(WallSlot.ForFailed("https://a.example.com"), slot);
    }

    [Fact(DisplayName = "State_AllUrlsFailed_ShowFailedCells")]
    public void AllUrlsFailed_ShowFailedCells()
    {
        var state = new WallState();
        state.AddFailed("https://a.example.com");
        state.AddFailed("https://b.example.com");
        state.AddFailed("https://c.example.com");

        Assert.Equal(WallMode.Grid, state.Mode);
        Assert.All(state.Slots, s => Assert.Equal(WallSlotKind.Failed, s.Kind));
        Assert.Equal(
            new[] { "https://a.example.com", "https://b.example.com", "https://c.example.com" },
            state.Slots.Select(s => s.Url));
        Assert.Empty(state.WindowIds);
    }

    [Fact(DisplayName = "State_AddWhileExpanded_KeepsExpanded")]
    public void AddWhileExpanded_KeepsExpanded()
    {
        WallState state = WithWindows(11);
        state.ClickCell(0);

        IReadOnlyList<WallAction> addWindow = state.AddWindow(22, "https://example.com/22");
        IReadOnlyList<WallAction> addFailed = state.AddFailed("https://failed.example.com");

        Assert.Equal(WallMode.Expanded, state.Mode);
        Assert.Equal(11, state.ExpandedWindowId);
        Assert.Equal(new[] { WallAction.Park(22), WallAction.HideTaskbarButton(22), WallAction.Relayout }, addWindow);
        Assert.Equal(new[] { WallAction.Relayout }, addFailed);
        Assert.Equal(3, state.Slots.Count);

        Assert.Empty(state.ClickCell(1));
        Assert.Equal(11, state.ExpandedWindowId);

        state.Shrink();
        Assert.Equal(new[] { WallAction.Expand(22), WallAction.ShowShrinkButton }, state.ClickCell(1));
    }

    [Fact(DisplayName = "State_AddFirstCell_FromEmptyToGrid")]
    public void AddFirstCell_FromEmptyToGrid()
    {
        var state = new WallState();
        Assert.Equal(WallMode.Empty, state.Mode);
        Assert.Empty(state.Slots);

        Assert.Equal(
            new[] { WallAction.Park(11), WallAction.HideTaskbarButton(11), WallAction.Relayout },
            state.AddWindow(11, "https://example.com/11"));
        Assert.Equal(WallMode.Grid, state.Mode);

        state.AddWindow(22, "https://example.com/22");
        Assert.Equal(WallMode.Grid, state.Mode);
        Assert.Equal(new long[] { 11, 22 }, state.WindowIds);
    }

    [Fact(DisplayName = "State_ClickFailedCell_NoAction")]
    public void ClickFailedCell_NoAction()
    {
        WallState state = WithWindows(11);
        state.AddFailed("https://failed.example.com");

        Assert.Empty(state.ClickCell(1));
        Assert.Equal(WallMode.Grid, state.Mode);
    }

    [Fact(DisplayName = "State_ClickOutOfRange_NoAction")]
    public void ClickOutOfRange_NoAction()
    {
        WallState state = WithWindows(11, 22);

        Assert.Empty(state.ClickCell(-1));
        Assert.Empty(state.ClickCell(2));
        Assert.Empty(new WallState().ClickCell(0));
        Assert.Equal(WallMode.Grid, state.Mode);
    }

    [Fact(DisplayName = "State_UnknownWindowClosed_NoAction")]
    public void UnknownWindowClosed_NoAction()
    {
        WallState state = WithWindows(11);

        Assert.Empty(state.WindowClosed(99));
        Assert.Equal(new long[] { 11 }, state.WindowIds);
    }

    [Fact(DisplayName = "State_SyncAlive_RemovesManyRelayoutOnce")]
    public void SyncAlive_RemovesManyRelayoutOnce()
    {
        WallState state = WithWindows(11, 22, 33, 44);

        IReadOnlyList<WallAction> actions = state.SyncAlive(new long[] { 22, 44 });

        Assert.Equal(new[] { WallAction.Relayout }, actions);
        Assert.Equal(new long[] { 22, 44 }, state.WindowIds);
        Assert.Empty(state.SyncAlive(new long[] { 22, 44 }));
    }

    [Fact(DisplayName = "State_SyncAlive_ExpandedAndAllGone_ShowsEmptyOnce")]
    public void SyncAlive_ExpandedAndAllGone_ShowsEmptyOnce()
    {
        WallState state = WithWindows(11, 22);
        state.ClickCell(1);

        IReadOnlyList<WallAction> actions = state.SyncAlive(Array.Empty<long>());

        Assert.Equal(new[] { WallAction.HideShrinkButton, WallAction.ShowEmpty }, actions);
        Assert.Equal(WallMode.Empty, state.Mode);
        Assert.Empty(state.Slots);
    }

    [Fact(DisplayName = "State_DuplicateWindow_Throws")]
    public void DuplicateWindow_Throws()
    {
        WallState state = WithWindows(11);

        Assert.Throws<InvalidOperationException>(() => state.AddWindow(11, "https://example.com/again"));
    }

    [Fact(DisplayName = "State_TooManySlots_Throws")]
    public void TooManySlots_Throws()
    {
        WallState state = WithWindows(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16);

        Assert.Throws<InvalidOperationException>(() => state.AddFailed("https://example.com/17"));
    }

    [Fact(DisplayName = "State_OtherWindowClosedWhileExpanded_StaysExpanded")]
    public void OtherWindowClosedWhileExpanded_StaysExpanded()
    {
        WallState state = WithWindows(11, 22, 33);
        state.ClickCell(0);

        Assert.Equal(new[] { WallAction.Relayout }, state.WindowClosed(33));
        Assert.Equal(WallMode.Expanded, state.Mode);
        Assert.Equal(11, state.ExpandedWindowId);
    }

    [Fact(DisplayName = "State_MinimizedWindow_RestoredAndParked")]
    public void MinimizedWindow_RestoredAndParked()
    {
        WallState state = WithWindows(11, 22, 33);

        Assert.Equal(new[] { WallAction.Restore(22), WallAction.Park(22) }, state.WindowMinimized(22));
        Assert.Equal(WallMode.Grid, state.Mode);
        Assert.Equal(new long[] { 11, 22, 33 }, state.WindowIds);

        // The expanded window is minimized: same as shrink
        state.ClickCell(2);
        Assert.Equal(
            new[] { WallAction.Restore(33), WallAction.Park(33), WallAction.HideShrinkButton },
            state.WindowMinimized(33));
        Assert.Equal(WallMode.Grid, state.Mode);
        Assert.Null(state.ExpandedWindowId);

        // While expanded, another parked window is minimized: the expanded one is not affected
        state.ClickCell(0);
        Assert.Equal(new[] { WallAction.Restore(22), WallAction.Park(22) }, state.WindowMinimized(22));
        Assert.Equal(WallMode.Expanded, state.Mode);
        Assert.Equal(11, state.ExpandedWindowId);

        // Not registered
        Assert.Empty(state.WindowMinimized(99));
    }

    [Fact(DisplayName = "State_ParkedWindowIds_ExcludeExpanded")]
    public void ParkedWindowIds_ExcludeExpanded()
    {
        WallState state = WithWindows(11, 22, 33);
        Assert.Equal(new long[] { 11, 22, 33 }, state.ParkedWindowIds);

        state.ClickCell(1);
        Assert.Equal(new long[] { 11, 33 }, state.ParkedWindowIds);
    }

    [Fact(DisplayName = "State_SomeWindowsClosed_FailedCellsKept")]
    public void SomeWindowsClosed_FailedCellsKept()
    {
        var state = new WallState();
        state.AddWindow(11, "https://example.com/11");
        state.AddFailed("https://failed.example.com");
        state.AddWindow(22, "https://example.com/22");

        state.WindowClosed(11);

        Assert.Equal(
            new[] { WallSlot.ForFailed("https://failed.example.com"), WallSlot.ForWindow(22, "https://example.com/22") },
            state.Slots);
    }

    [Fact(DisplayName = "State_AddWindow_ParksAndRelayouts")]
    public void AdoptWindow_ParksAndRelayouts()
    {
        WallState state = WithWindows(11, 22);

        IReadOnlyList<WallAction> actions = state.AdoptWindow(33);

        Assert.Equal(new[] { WallAction.Park(33), WallAction.HideTaskbarButton(33), WallAction.Relayout }, actions);
        Assert.Equal(new long[] { 11, 22, 33 }, state.WindowIds);
        Assert.Equal(WallSlot.ForWindow(33, string.Empty), state.Slots[^1]);
        Assert.Equal(WallMode.Grid, state.Mode);

        // Already registered: no action
        Assert.Empty(state.AdoptWindow(22));
        Assert.Equal(3, state.Slots.Count);

        // Expanded: no action
        state.ClickCell(0);
        Assert.Empty(state.AdoptWindow(44));
        Assert.Equal(3, state.Slots.Count);
        state.Shrink();

        // Wall minimized: no action
        state.WallMinimized();
        Assert.Empty(state.AdoptWindow(44));
        Assert.Equal(3, state.Slots.Count);
        state.WallRestored();

        // 16 managed windows already: no action
        WallState full = WithWindows(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16);
        Assert.Empty(full.AdoptWindow(17));
        Assert.Equal(16, full.Slots.Count);

        // After all windows are closed (Empty), adding still works and returns to grid mode
        WallState empty = WithWindows(11);
        empty.WindowClosed(11);
        Assert.Equal(WallMode.Empty, empty.Mode);
        Assert.Equal(new[] { WallAction.Park(55), WallAction.HideTaskbarButton(55), WallAction.Relayout }, empty.AdoptWindow(55));
        Assert.Equal(WallMode.Grid, empty.Mode);
    }

    [Fact(DisplayName = "State_RemoveWindow_ReleasesAndRelayouts")]
    public void ReleaseWindow_ReleasesAndRelayouts()
    {
        WallState state = WithWindows(11, 22, 33);

        IReadOnlyList<WallAction> actions = state.ReleaseWindow(22);

        Assert.Equal(new[] { WallAction.Release(22), WallAction.Relayout }, actions);
        Assert.Equal(new long[] { 11, 33 }, state.WindowIds);
        Assert.Equal(WallMode.Grid, state.Mode);

        // Not registered: no action
        Assert.Empty(state.ReleaseWindow(22));
        Assert.Equal(new long[] { 11, 33 }, state.WindowIds);

        // The expanded window cannot be popped out
        state.ClickCell(0);
        Assert.Empty(state.ReleaseWindow(11));
        Assert.Equal(new long[] { 11, 33 }, state.WindowIds);
        Assert.Equal(WallMode.Expanded, state.Mode);
        state.Shrink();

        // Wall minimized: no action
        state.WallMinimized();
        Assert.Empty(state.ReleaseWindow(33));
        state.WallRestored();

        // Pop out down to the last one → no windows
        Assert.Equal(new[] { WallAction.Release(11), WallAction.Relayout }, state.ReleaseWindow(11));
        Assert.Equal(new[] { WallAction.Release(33), WallAction.ShowEmpty }, state.ReleaseWindow(33));
        Assert.Equal(WallMode.Empty, state.Mode);
        Assert.Empty(state.Slots);
    }

    [Fact(DisplayName = "State_RemoveExpanded_NotAllowed")]
    public void RemoveExpanded_NotAllowed()
    {
        WallState state = WithWindows(11, 22);
        state.ClickCell(1);

        Assert.Empty(state.ReleaseWindow(22));
        Assert.Equal(WallMode.Expanded, state.Mode);
        Assert.Equal(22, state.ExpandedWindowId);
        Assert.Equal(new long[] { 11, 22 }, state.WindowIds);

        // After shrinking, the same window can be popped out
        state.Shrink();
        Assert.Equal(new[] { WallAction.Release(22), WallAction.Relayout }, state.ReleaseWindow(22));
        Assert.Equal(new long[] { 11 }, state.WindowIds);
    }

    [Fact(DisplayName = "State_WallMinimized_MinimizesAll")]
    public void WallMinimized_MinimizesAll()
    {
        // Grid mode
        WallState grid = WithWindows(11, 22, 33);
        Assert.Equal(
            new[] { WallAction.Minimize(11), WallAction.Minimize(22), WallAction.Minimize(33) },
            grid.WallMinimized());
        Assert.True(grid.IsWallMinimized);
        Assert.Equal(WallMode.Grid, grid.Mode);

        // While minimized: auto-restore paused; clicking a cell, shrinking and resizing do nothing; minimizing again does nothing
        Assert.Empty(grid.WindowMinimized(22));
        Assert.Empty(grid.ClickCell(0));
        Assert.Empty(grid.Shrink());
        Assert.Empty(grid.WallResized());
        Assert.Empty(grid.WallMinimized());
        Assert.Equal(WallMode.Grid, grid.Mode);

        // A window registered at startup while minimized: minimized together, not parked
        Assert.Equal(
            new[] { WallAction.Minimize(44), WallAction.HideTaskbarButton(44), WallAction.Relayout },
            grid.AddWindow(44, "https://example.com/44"));

        // Expanded mode: also hide the "Back to grid" button and remember the expanded window
        WallState expanded = WithWindows(11, 22);
        expanded.ClickCell(1);
        Assert.Equal(
            new[] { WallAction.Minimize(11), WallAction.Minimize(22), WallAction.HideShrinkButton },
            expanded.WallMinimized());
        Assert.Equal(WallMode.Expanded, expanded.Mode);
        Assert.Equal(22, expanded.ExpandedWindowId);

        // Registering while not minimized produces no minimize
        WallState normal = WithWindows(11);
        Assert.DoesNotContain(
            normal.AddWindow(22, "https://example.com/22"),
            a => a.Kind == WallActionKind.Minimize);
    }

    [Fact(DisplayName = "State_WallRestored_ReturnsToPreviousMode")]
    public void WallRestored_ReturnsToPreviousMode()
    {
        // Grid mode
        WallState grid = WithWindows(11, 22);
        Assert.Empty(grid.WallRestored()); // Not minimized: no action
        grid.WallMinimized();
        Assert.Equal(
            new[]
            {
                WallAction.Restore(11), WallAction.Park(11),
                WallAction.Restore(22), WallAction.Park(22),
                WallAction.Relayout,
            },
            grid.WallRestored());
        Assert.False(grid.IsWallMinimized);
        Assert.Equal(WallMode.Grid, grid.Mode);

        // After the deferred restore runs (restore actions are deferred, with no auto-restore meanwhile), auto-restore resumes
        grid.SettleRestore();
        Assert.Equal(new[] { WallAction.Restore(22), WallAction.Park(22) }, grid.WindowMinimized(22));

        // Expanded mode: expand the same window again and show the button
        WallState expanded = WithWindows(11, 22, 33);
        expanded.ClickCell(1);
        expanded.WallMinimized();
        Assert.Equal(
            new[]
            {
                WallAction.Restore(11), WallAction.Park(11),
                WallAction.Restore(33), WallAction.Park(33),
                WallAction.Restore(22), WallAction.Expand(22), WallAction.ShowShrinkButton,
                WallAction.Relayout,
            },
            expanded.WallRestored());
        Assert.Equal(WallMode.Expanded, expanded.Mode);
        Assert.Equal(22, expanded.ExpandedWindowId);

        // The expanded window is closed while minimized → grid mode after restore
        WallState closed = WithWindows(11, 22);
        closed.ClickCell(0);
        closed.WallMinimized();
        Assert.Equal(new[] { WallAction.HideShrinkButton, WallAction.Relayout }, closed.SyncAlive(new long[] { 22 }));
        Assert.Equal(
            new[] { WallAction.Restore(22), WallAction.Park(22), WallAction.Relayout },
            closed.WallRestored());
        Assert.Equal(WallMode.Grid, closed.Mode);
        Assert.Null(closed.ExpandedWindowId);

        // All closed while minimized → only "no windows" after restore
        WallState gone = WithWindows(11);
        gone.WallMinimized();
        gone.WindowClosed(11);
        Assert.Equal(new[] { WallAction.ShowEmpty }, gone.WallRestored());
        Assert.Equal(WallMode.Empty, gone.Mode);
    }

    [Fact(DisplayName = "State_WallActivatedWhileExpanded_Shrinks")]
    public void WallActivatedWhileExpanded_Shrinks()
    {
        WallState state = WithWindows(11, 22, 33);
        state.ClickCell(1);

        IReadOnlyList<WallAction> actions = state.WallActivated();

        Assert.Equal(WallMode.Grid, state.Mode);
        Assert.Null(state.ExpandedWindowId);
        Assert.Equal(new[] { WallAction.HideShrinkButton, WallAction.ParkDeferred(22) }, actions);
        var foregroundOrSync = new[] { WallActionKind.Expand, WallActionKind.Release, WallActionKind.Park, WallActionKind.Restore };
        Assert.DoesNotContain(actions, a => foregroundOrSync.Contains(a.Kind));

        // Can expand again after shrinking.
        Assert.Equal(new[] { WallAction.Expand(33), WallAction.ShowShrinkButton }, state.ClickCell(2));

        // Wall minimized: no action, state unchanged.
        state.WallMinimized();
        Assert.Empty(state.WallActivated());
        Assert.Equal(WallMode.Expanded, state.Mode);
        Assert.Equal(33, state.ExpandedWindowId);

        // Exiting: no action, state unchanged.
        WallState exiting = WithWindows(11, 22);
        exiting.ClickCell(0);
        exiting.BeginExit();
        Assert.Empty(exiting.WallActivated());
        Assert.Equal(WallMode.Expanded, exiting.Mode);
        Assert.Equal(11, exiting.ExpandedWindowId);
    }

    private static readonly WallActionKind[] ForegroundOrSyncKinds =
    {
        WallActionKind.Expand, WallActionKind.Release, WallActionKind.Park, WallActionKind.Restore, WallActionKind.Minimize,
    };

    [Fact(DisplayName = "State_WallActivated_NoForegroundAction")]
    public void WallActivated_NoForegroundAction()
    {
        // Grid mode: one deferred park per managed window, state unchanged
        WallState grid = WithWindows(11, 22);
        grid.AddFailed("https://failed.example.com");
        IReadOnlyList<WallAction> gridActions = grid.WallActivated();
        Assert.Equal(new[] { WallAction.ParkDeferred(11), WallAction.ParkDeferred(22) }, gridActions);
        Assert.DoesNotContain(gridActions, a => ForegroundOrSyncKinds.Contains(a.Kind));
        Assert.Equal(WallMode.Grid, grid.Mode);

        // Expanded mode: shrink, only hide the button and defer the parking
        WallState expanded = WithWindows(11, 22);
        expanded.ClickCell(0);
        IReadOnlyList<WallAction> expandedActions = expanded.WallActivated();
        Assert.Equal(new[] { WallAction.HideShrinkButton, WallAction.ParkDeferred(11) }, expandedActions);
        Assert.DoesNotContain(expandedActions, a => ForegroundOrSyncKinds.Contains(a.Kind));

        // Title bar minimize or close button pressed: no action in either mode, state unchanged
        WallState caption = WithWindows(11, 22);
        Assert.Empty(caption.WallActivated(captionButton: true));
        Assert.True(caption.CaptionPressPending);
        Assert.Equal(WallMode.Grid, caption.Mode);
        WallState captionExpanded = WithWindows(11, 22);
        captionExpanded.ClickCell(1);
        Assert.Empty(captionExpanded.WallActivated(captionButton: true));
        Assert.Equal(WallMode.Expanded, captionExpanded.Mode);
        Assert.Equal(22, captionExpanded.ExpandedWindowId);

        // No windows, wall minimized, exiting, restore pending: no action
        Assert.Empty(new WallState().WallActivated());
        WallState minimized = WithWindows(11);
        minimized.WallMinimized();
        Assert.Empty(minimized.WallActivated());
        WallState exiting = WithWindows(11);
        exiting.BeginExit();
        Assert.Empty(exiting.WallActivated());
        WallState restoring = WithWindows(11);
        restoring.WallMinimized();
        restoring.WallRestored();
        Assert.True(restoring.RestorePending);
        Assert.Empty(restoring.WallActivated());
    }

    [Fact(DisplayName = "State_MinimizeWhileExpanded_MinimizesAll")]
    public void MinimizeWhileExpanded_MinimizesAll()
    {
        WallState state = WithWindows(11, 22, 33);
        state.ClickCell(1);

        // Minimize button pressed: the wall becomes active but does not shrink
        Assert.Empty(state.WallActivated(captionButton: true));
        Assert.Equal(WallMode.Expanded, state.Mode);

        // Released: minimize; all managed windows (including the expanded one) minimize together
        Assert.Equal(
            new[] { WallAction.Minimize(11), WallAction.Minimize(22), WallAction.Minimize(33), WallAction.HideShrinkButton },
            state.WallMinimized());
        Assert.True(state.IsWallMinimized);
        Assert.False(state.CaptionPressPending);

        // Restore: before the deferred run, no shrink, no auto-restore, no cell clicks
        state.WallRestored();
        Assert.True(state.RestorePending);
        Assert.Empty(state.WallActivated());
        Assert.Empty(state.WindowMinimized(22));
        Assert.Empty(state.ClickCell(0));
        Assert.Equal(WallMode.Expanded, state.Mode);

        Assert.Equal(
            new[]
            {
                WallAction.Restore(11), WallAction.Park(11),
                WallAction.Restore(33), WallAction.Park(33),
                WallAction.Restore(22), WallAction.Expand(22), WallAction.ShowShrinkButton,
                WallAction.Relayout,
            },
            state.SettleRestore());
        Assert.False(state.RestorePending);
        Assert.Equal(new[] { WallAction.HideShrinkButton, WallAction.ParkDeferred(22) }, state.WallActivated());

        // Click the wall to shrink first, then minimize: restore goes back to the grid
        WallState shrunkFirst = WithWindows(11, 22);
        shrunkFirst.ClickCell(1);
        shrunkFirst.WallActivated();
        Assert.Equal(new[] { WallAction.Minimize(11), WallAction.Minimize(22) }, shrunkFirst.WallMinimized());
        shrunkFirst.WallRestored();
        IReadOnlyList<WallAction> settled = shrunkFirst.SettleRestore();
        Assert.DoesNotContain(settled, a => a.Kind == WallActionKind.Expand);
        Assert.Equal(WallMode.Grid, shrunkFirst.Mode);
    }

    [Fact(DisplayName = "State_CaptionPressCancelled_Shrinks")]
    public void CaptionPressCancelled_Shrinks()
    {
        // Cancelled in expanded mode: shrink, defer the parking
        WallState expanded = WithWindows(11, 22);
        expanded.ClickCell(1);
        expanded.WallActivated(captionButton: true);
        Assert.True(expanded.CaptionPressPending);
        Assert.Equal(new[] { WallAction.HideShrinkButton, WallAction.ParkDeferred(22) }, expanded.CaptionPressEnded(true));
        Assert.Equal(WallMode.Grid, expanded.Mode);
        Assert.False(expanded.CaptionPressPending);

        // Cancelled in grid mode: deferred check of the parking
        WallState grid = WithWindows(11, 22);
        grid.CaptionPressStarted();
        Assert.Equal(new[] { WallAction.ParkDeferred(11), WallAction.ParkDeferred(22) }, grid.CaptionPressEnded(true));

        // The wall is no longer active: only clear the record
        WallState inactive = WithWindows(11, 22);
        inactive.ClickCell(0);
        inactive.CaptionPressStarted();
        Assert.Empty(inactive.CaptionPressEnded(false));
        Assert.Equal(WallMode.Expanded, inactive.Mode);
        Assert.False(inactive.CaptionPressPending);

        // Really minimized: record cleared; still expanded after restore
        WallState minimized = WithWindows(11, 22);
        minimized.ClickCell(1);
        minimized.CaptionPressStarted();
        minimized.WallMinimized();
        Assert.False(minimized.CaptionPressPending);
        Assert.Empty(minimized.CaptionPressEnded(true));
        minimized.WallRestored();
        Assert.Contains(WallAction.Expand(22), minimized.SettleRestore());

        // Really closed: record cleared
        WallState closing = WithWindows(11, 22);
        closing.ClickCell(1);
        closing.CaptionPressStarted();
        closing.BeginExit();
        Assert.False(closing.CaptionPressPending);
        Assert.Empty(closing.CaptionPressEnded(true));

        // No button pressed: no action
        Assert.Empty(WithWindows(11).CaptionPressEnded(true));
    }

    [Fact(DisplayName = "State_CaptionPressDuringRestore_DefersRestore")]
    public void CaptionPressDuringRestore_DefersRestore()
    {
        static WallState RestoringExpanded()
        {
            WallState s = WithWindows(11, 22);
            s.ClickCell(1);
            s.WallMinimized();
            s.WallRestored();
            s.CaptionPressStarted();
            Assert.True(s.CaptionPressPending);
            Assert.Empty(s.SettleRestore());
            Assert.True(s.RestorePending);
            return s;
        }

        // (a) Really minimized: both records cleared; after restoring again, the same cell is expanded
        WallState a = RestoringExpanded();
        a.WallMinimized();
        Assert.False(a.RestorePending);
        Assert.False(a.CaptionPressPending);
        a.WallRestored();
        IReadOnlyList<WallAction> aSettled = a.SettleRestore();
        Assert.Contains(WallAction.Expand(22), aSettled);
        Assert.Contains(WallAction.ShowShrinkButton, aSettled);

        // (b) Cancelled: the target becomes the grid; the deferred restore only restores and parks
        WallState b = RestoringExpanded();
        Assert.Equal(new[] { WallAction.HideShrinkButton }, b.CaptionPressEnded(true));
        Assert.Equal(WallMode.Grid, b.Mode);
        Assert.True(b.RestorePending);
        Assert.Equal(
            new[] { WallAction.Restore(11), WallAction.Park(11), WallAction.Restore(22), WallAction.Park(22), WallAction.Relayout },
            b.SettleRestore());

        // (c) Really closed: record cleared, no more restore
        WallState c = RestoringExpanded();
        c.BeginExit();
        Assert.False(c.RestorePending);
        Assert.False(c.CaptionPressPending);
        Assert.Empty(c.SettleRestore());
    }

    [Fact(DisplayName = "State_ReleaseDuringRestore_NotParkedAgain")]
    public void ReleaseDuringRestore_NotParkedAgain()
    {
        // Popped out during the restore: not parked back behind the wall
        WallState released = WithWindows(11, 22, 33);
        released.WallMinimized();
        released.WallRestored();
        Assert.Equal(WallAction.Release(22), released.ReleaseWindow(22)[0]);
        Assert.Equal(
            new[] { WallAction.Restore(11), WallAction.Park(11), WallAction.Restore(33), WallAction.Park(33), WallAction.Relayout },
            released.SettleRestore());

        // The user closes a cell during the restore
        WallState closed = WithWindows(11, 22, 33);
        closed.WallMinimized();
        closed.WallRestored();
        closed.WindowClosed(33);
        Assert.Equal(
            new[] { WallAction.Restore(11), WallAction.Park(11), WallAction.Restore(22), WallAction.Park(22), WallAction.Relayout },
            closed.SettleRestore());

        // The expanded window is closed during the restore: back to the grid, nothing expanded
        WallState expandedClosed = WithWindows(11, 22);
        expandedClosed.ClickCell(1);
        expandedClosed.WallMinimized();
        expandedClosed.WallRestored();
        expandedClosed.WindowClosed(22);
        IReadOnlyList<WallAction> settled = expandedClosed.SettleRestore();
        Assert.Equal(new[] { WallAction.Restore(11), WallAction.Park(11), WallAction.Relayout }, settled);
        Assert.Equal(WallMode.Grid, expandedClosed.Mode);
    }

    [Fact(DisplayName = "State_DeferredPark_SkipsReexpandedOrReleased")]
    public void DeferredPark_SkipsReexpandedOrReleased()
    {
        // Expand A → click the wall (park A scheduled) → expand A again before the deferred run
        WallState state = WithWindows(11, 22);
        state.ClickCell(0);
        IReadOnlyList<long> scheduled = state.WallActivated()
            .Where(a => a.Kind == WallActionKind.ParkDeferred).Select(a => a.WindowId!.Value).ToList();
        Assert.Equal(new long[] { 11 }, scheduled);
        state.ClickCell(0);
        Assert.Empty(state.DeferredParkTargets(scheduled));

        // Grid mode schedules A, B, C → pop out B, close C → only A remains
        WallState grid = WithWindows(11, 22, 33);
        IReadOnlyList<long> all = grid.WallActivated().Select(a => a.WindowId!.Value).ToList();
        grid.ReleaseWindow(22);
        grid.WindowClosed(33);
        Assert.Equal(new long[] { 11 }, grid.DeferredParkTargets(all));
    }

    [Fact(DisplayName = "State_ResizeDuringActivationPark_Deferred")]
    public void ResizeDuringActivationPark_Deferred()
    {
        // Expand → hold the title bar (the wall becomes active, shrinks and schedules a deferred park) → drag the wall (move events)
        WallState state = WithWindows(11, 22);
        state.ClickCell(0);
        state.WallActivated();
        Assert.True(state.ActivationParkPending);

        IReadOnlyList<WallAction> moved = state.WallResized();
        Assert.Equal(new[] { WallAction.ParkDeferred(11), WallAction.ParkDeferred(22), WallAction.Relayout }, moved);
        Assert.DoesNotContain(moved, a => ForegroundOrSyncKinds.Contains(a.Kind));
        Assert.True(state.ActivationParkPending);

        // The deferred park runs on the currently parked windows; later moves park them synchronously as before
        Assert.Equal(new long[] { 11, 22 }, state.DeferredParkTargets(new long[] { 11, 22 }));
        Assert.False(state.ActivationParkPending);
        Assert.Equal(new[] { WallAction.Park(11), WallAction.Park(22), WallAction.Relayout }, state.WallResized());

        // Moved while a title bar button is pressed: also deferred, and the expanded window is not adjusted synchronously either
        WallState pressing = WithWindows(11, 22);
        pressing.ClickCell(1);
        pressing.CaptionPressStarted();
        Assert.Equal(new[] { WallAction.ParkDeferred(11), WallAction.Relayout }, pressing.WallResized());

        // Shrink, click a cell to expand, then move: parking deferred, the expanded window adjusted as usual
        WallState reexpanded = WithWindows(11, 22);
        reexpanded.ClickCell(0);
        reexpanded.WallActivated();
        reexpanded.ClickCell(1);
        Assert.Equal(
            new[] { WallAction.ParkDeferred(11), WallAction.Expand(22), WallAction.ShowShrinkButton, WallAction.Relayout },
            reexpanded.WallResized());

        // Minimizing or closing the wall ends the deferred park
        WallState minimized = WithWindows(11);
        minimized.WallActivated();
        minimized.WallMinimized();
        Assert.False(minimized.ActivationParkPending);
        WallState exiting = WithWindows(11);
        exiting.WallActivated();
        exiting.BeginExit();
        Assert.False(exiting.ActivationParkPending);
    }

    [Fact(DisplayName = "State_RestorePending_ResizeOnlyRelayout")]
    public void RestorePending_ResizeOnlyRelayout()
    {
        WallState state = WithWindows(11, 22);
        state.ClickCell(1);
        state.WallMinimized();
        state.WallRestored();
        Assert.Equal(new[] { WallAction.Relayout }, state.WallResized());

        state.SettleRestore();
        Assert.Equal(
            new[] { WallAction.Park(11), WallAction.Expand(22), WallAction.ShowShrinkButton, WallAction.Relayout },
            state.WallResized());

        // No pending restore, wall minimized, exiting: empty
        Assert.Empty(state.SettleRestore());
        state.WallMinimized();
        Assert.Empty(state.SettleRestore());
        state.WallRestored();
        state.BeginExit();
        Assert.Empty(state.SettleRestore());
    }
}
