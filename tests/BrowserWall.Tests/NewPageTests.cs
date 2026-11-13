using BrowserWall.Core.Detect;
using BrowserWall.Core.Session;
using BrowserWall.Core.State;
using BrowserWall.Core.Text;

namespace BrowserWall.Tests;

public class NewPageTests
{
    private const uint WallPid = 7;
    private const string Chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

    private static (WallState State, SessionTracker Tracker, FakeSessionStore Store) Start(int count)
    {
        var store = new FakeSessionStore();
        string[] urls = Enumerable.Range(1, count).Select(i => $"https://example.com/{i}").ToArray();
        var state = new WallState();
        var tracker = new SessionTracker(store, urls);
        for (int i = 0; i < count; i++)
        {
            state.AddWindow(100 + i, urls[i], tracker.StartupEntry(i));
            tracker.Sync(state.Slots);
        }

        return (state, tracker, store);
    }

    [Fact(DisplayName = "NewPage_ExpandsNewWindow")]
    public void NewPage_ExpandsNewWindow()
    {
        // New window found in grid mode: register, park, hide the taskbar button, relayout, then expand.
        (WallState state, SessionTracker tracker, FakeSessionStore store) = Start(2);
        Assert.Null(AddPageFlow.Begin(state, launching: false));
        AddPageResult result = AddPageFlow.Finish(state, tracker, windowId: 200);
        Assert.True(result.Found);
        Assert.Equal(
            new[]
            {
                WallAction.Park(200), WallAction.HideTaskbarButton(200), WallAction.Relayout,
                WallAction.Expand(200), WallAction.ShowShrinkButton,
            },
            result.Actions);
        Assert.Equal(WallMode.Expanded, state.Mode);
        Assert.Equal((long?)200, state.ExpandedWindowId);
        Assert.Equal(WallSlot.ForWindow(200, string.Empty), state.Slots[^1]);
        Assert.Empty(store.Writes);
        Assert.DoesNotContain(result.Actions, a => a.Kind == WallActionKind.Minimize);

        // The user expanded another cell while waiting: switch; still only one expanded.
        (WallState other, SessionTracker otherTracker, _) = Start(2);
        other.ClickCell(0);
        AddPageResult switched = AddPageFlow.Finish(other, otherTracker, windowId: 300);
        Assert.Equal(
            new[]
            {
                WallAction.Park(300), WallAction.HideTaskbarButton(300), WallAction.Relayout,
                WallAction.Park(100), WallAction.Expand(300), WallAction.ShowShrinkButton,
            },
            switched.Actions);
        Assert.Equal((long?)300, other.ExpandedWindowId);

        // Found while the wall is minimized: register and minimize with it; no expand.
        (WallState minimized, SessionTracker minTracker, _) = Start(1);
        minimized.WallMinimized();
        AddPageResult whileMin = AddPageFlow.Finish(minimized, minTracker, windowId: 400);
        Assert.True(whileMin.Found);
        Assert.Equal(new[] { WallAction.Minimize(400), WallAction.HideTaskbarButton(400), WallAction.Relayout }, whileMin.Actions);
        Assert.Contains(400L, minimized.WindowIds);
        Assert.NotEqual((long?)400, minimized.ExpandedWindowId);

        // Not found: no cell is added.
        (WallState none, SessionTracker noneTracker, _) = Start(1);
        AddPageResult notFound = AddPageFlow.Finish(none, noneTracker, windowId: null);
        Assert.False(notFound.Found);
        Assert.Empty(notFound.Actions);
        Assert.Single(none.Slots);

        // Found while the wall is closing: not registered, not expanded.
        (WallState exiting, SessionTracker exitTracker, _) = Start(1);
        exiting.BeginExit();
        Assert.False(AddPageFlow.Finish(exiting, exitTracker, windowId: 500).Found);
        Assert.Empty(exiting.ExpandNewWindow(100));
    }

    [Fact(DisplayName = "Existing_ButtonOnlyWhenCandidates")]
    public void Existing_ButtonOnlyWhenCandidates()
    {
        (WallState state, _, _) = Start(2);

        // N counts only windows of the same process and class, with a title, not yet managed.
        var windows = new[]
        {
            new WindowInfo(100, Chrome, WindowDetector.BrowserWindowClass, "Managed", true, WallPid),
            new WindowInfo(201, Chrome, WindowDetector.BrowserWindowClass, "Ctrl+N", true, WallPid),
            new WindowInfo(202, Chrome, WindowDetector.BrowserWindowClass, "Popup", true, WallPid),
            new WindowInfo(203, Chrome, WindowDetector.BrowserWindowClass, "Other Chrome", true, 99),
            new WindowInfo(204, Chrome, "Other", "Other class", true, WallPid),
            new WindowInfo(205, Chrome, WindowDetector.BrowserWindowClass, "", true, WallPid),
        };
        int n = AddPicker.Candidates(windows, WallPid, state.WindowIds).Count;
        Assert.Equal(2, n);

        ExistingButtonState shown = AddPicker.ExistingButton(state, n, launching: false);
        Assert.True(shown.Visible);
        Assert.True(shown.Enabled);
        Assert.Equal(2, shown.Count);
        Assert.Equal("加入已開著的視窗（2）", UiStrings.Format(UiLanguage.ZhTw, UiTextKey.AddExisting, shown.Count));

        // N is 0: not shown.
        Assert.False(AddPicker.ExistingButton(state, 0, launching: false).Visible);
        Assert.False(AddPicker.ExistingButton(state, AddPicker.Candidates(windows, null, state.WindowIds).Count, false).Visible);

        // Launching or adding: shown but disabled.
        ExistingButtonState busy = AddPicker.ExistingButton(state, n, launching: true);
        Assert.True(busy.Visible);
        Assert.False(busy.Enabled);

        // Expanded mode, wall minimized: not shown.
        state.ClickCell(0);
        Assert.False(AddPicker.ExistingButton(state, n, false).Visible);
        state.Shrink();
        state.WallMinimized();
        Assert.False(AddPicker.ExistingButton(state, n, false).Visible);

        // After popping out the only cell (no cells): still a candidate, the button is shown and clickable, and picking it adds it back.
        (WallState single, _, _) = Start(1);
        Assert.NotEmpty(single.ReleaseWindow(100));
        Assert.Equal(WallMode.Empty, single.Mode);
        var released = new[] { new WindowInfo(100, Chrome, WindowDetector.BrowserWindowClass, "Popped out", true, WallPid) };
        int left = AddPicker.Candidates(released, WallPid, single.WindowIds).Count;
        Assert.Equal(1, left);
        ExistingButtonState empty = AddPicker.ExistingButton(single, left, launching: false);
        Assert.True(empty.Visible);
        Assert.True(empty.Enabled);
        Assert.True(AddPicker.CanAdd(single, launching: false));
        Assert.True(AddPicker.IsStillCandidate(100, released, WallPid, single.WindowIds));
        Assert.NotEmpty(single.AdoptWindow(100));
        Assert.Equal(new long[] { 100 }, single.WindowIds);
        Assert.Equal(WallMode.Grid, single.Mode);

        // Cells full: shown but disabled.
        (WallState full, _, _) = Start(16);
        ExistingButtonState fullState = AddPicker.ExistingButton(full, 1, false);
        Assert.True(fullState.Visible);
        Assert.False(fullState.Enabled);
    }
}
