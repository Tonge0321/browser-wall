using BrowserWall.Core.Session;
using BrowserWall.Core.State;

namespace BrowserWall.Tests;

public class AddPageFlowTests
{
    private static readonly string[] ConfigUrls = { "https://example.com/config" };

    /// <summary>Registers cells like the App's startup flow (window handle = 100 + index; indexes in failed fail to open).</summary>
    private static (WallState State, SessionTracker Tracker) Start(FakeSessionStore store, string[] urls, params int[] failed)
    {
        var state = new WallState();
        var tracker = new SessionTracker(store, urls);
        for (int i = 0; i < urls.Length; i++)
        {
            if (failed.Contains(i))
            {
                state.AddFailed(urls[i], tracker.StartupEntry(i));
            }
            else
            {
                state.AddWindow(100 + i, urls[i], tracker.StartupEntry(i));
            }

            tracker.Sync(state.Slots);
        }

        return (state, tracker);
    }

    private static string[] Urls(int count) =>
        Enumerable.Range(1, count).Select(i => $"https://example.com/{i}").ToArray();

    private static int IndexOfFailed(WallState state, string url) =>
        state.Slots.ToList().FindIndex(s => s.Kind == WallSlotKind.Failed && s.Url == url);

    [Fact(DisplayName = "AddPage_NotFound_NoCell")]
    public void AddPage_NotFound_NoCell()
    {
        // No new window found within 10 seconds: no cell is added.
        var store = new FakeSessionStore();
        (WallState state, SessionTracker tracker) = Start(store, Urls(2));

        Assert.Null(AddPageFlow.Begin(state, launching: false));
        Assert.Equal("chrome://newtab/", AddPageFlow.NewWindowUrl);
        Assert.Empty(store.Writes);

        AddPageResult notFound = AddPageFlow.Finish(state, tracker, windowId: null);
        Assert.False(notFound.Found);
        Assert.Empty(notFound.Actions);
        Assert.Equal(2, state.Slots.Count);
        Assert.Empty(store.Writes);
        Assert.Equal(Urls(2), tracker.Urls);
    }

    [Fact(DisplayName = "Pick_ExistingWindowRememberedAfterUrlRecorded")]
    public void Pick_ExistingWindowRememberedAfterUrlRecorded()
    {
        // Added cells and "+ Add page" cells join the list once a URL is recorded.
        var store = new FakeSessionStore();
        (WallState state, SessionTracker tracker) = Start(store, Urls(2));
        var recorder = new LastSeenUrl();

        // Adding an open window: no URL recorded yet, so the list does not change and is not written.
        Assert.NotEmpty(state.AdoptWindow(300));
        tracker.Sync(state.Slots);
        Assert.Empty(store.Writes);
        Assert.Equal(Urls(2), tracker.Urls);
        Assert.Equal(string.Empty, state.Slots[^1].Url);
        Assert.Null(state.Slots[^1].SessionEntry);

        // Once a URL is recorded, it joins in cell order.
        Assert.True(recorder.Record(state, 300, "https://example.com/adopted", false, TimeSpan.Zero));
        tracker.Sync(state.Slots);
        Assert.Single(store.Writes);
        Assert.Equal(new[] { "https://example.com/1", "https://example.com/2", "https://example.com/adopted" }, store.LastWritten());

        // "+ Add page": the new cell has no URL, nothing written; it joins once a URL is recorded.
        Assert.Null(AddPageFlow.Begin(state, launching: false));
        Assert.True(AddPageFlow.Finish(state, tracker, windowId: 200).Found);
        Assert.Equal(WallSlot.ForWindow(200, string.Empty), state.Slots[^1]);
        Assert.Single(store.Writes);
        Assert.True(recorder.Record(state, 200, "example.com/new", false, TimeSpan.Zero));
        tracker.Sync(state.Slots);
        Assert.Equal(
            new[] { "https://example.com/1", "https://example.com/2", "https://example.com/adopted", "https://example.com/new" },
            store.LastWritten());

        // The user closes the added cell: the list drops its URL.
        Assert.NotEmpty(state.WindowClosed(300));
        tracker.Sync(state.Slots);
        Assert.Equal(3, store.Writes.Count);
        Assert.Equal(new[] { "https://example.com/1", "https://example.com/2", "https://example.com/new" }, store.LastWritten());
    }

    [Fact(DisplayName = "AddPage_Begin_InvalidOrNotAllowed_NoChange")]
    public void AddPage_Begin_InvalidOrNotAllowed_NoChange()
    {
        // There is no input box, so only "cannot add" is checked here.
        var store = new FakeSessionStore();
        (WallState state, SessionTracker tracker) = Start(store, Urls(2));

        Assert.Equal(AddPageBlock.NotAllowed, AddPageFlow.Begin(state, launching: true));

        state.ClickCell(0);
        Assert.Equal(AddPageBlock.NotAllowed, AddPageFlow.Begin(state, launching: false));
        state.Shrink();

        (WallState full, SessionTracker _) = Start(store, Urls(16));
        Assert.Equal(AddPageBlock.NotAllowed, AddPageFlow.Begin(full, launching: false));

        Assert.Equal(2, state.Slots.Count);
        Assert.Equal(Urls(2), tracker.Urls);
        Assert.Equal(16, full.Slots.Count);
        Assert.Empty(store.Writes);
    }

    [Fact(DisplayName = "AddPage_FullWithFailed_KeepsFailedSlot")]
    public void AddPage_FullWithFailed_KeepsFailedSlot()
    {
        var store = new FakeSessionStore();
        (WallState state, SessionTracker tracker) = Start(store, Urls(16), 4);
        Assert.False(state.HasFreeSlot);

        Assert.Equal(AddPageBlock.Full, AddPageFlow.Begin(state, launching: false));
        Assert.Empty(state.AdoptWindow(300));
        tracker.Sync(state.Slots);

        Assert.Equal(16, state.Slots.Count);
        Assert.Equal(4, IndexOfFailed(state, "https://example.com/5"));
        Assert.Empty(store.Writes);
        Assert.Contains("https://example.com/5", tracker.Urls);

        // After popping out a managed window, adding is possible; the failed URL is still there.
        Assert.NotEmpty(state.ReleaseWindow(100));
        tracker.Sync(state.Slots);
        Assert.Single(store.Writes);
        Assert.DoesNotContain("https://example.com/1", store.LastWritten());
        Assert.Contains("https://example.com/5", store.LastWritten());

        // Pressing "+ Add page" does not write the list; the new cell joins only once a URL is recorded.
        Assert.Null(AddPageFlow.Begin(state, launching: false));
        AddPageFlow.Finish(state, tracker, windowId: 400);
        Assert.True(new LastSeenUrl().Record(state, 400, "https://example.com/new", false, TimeSpan.Zero));
        tracker.Sync(state.Slots);
        Assert.Contains("https://example.com/5", store.LastWritten());
        Assert.Contains("https://example.com/new", store.LastWritten());
    }

    [Fact(DisplayName = "AddPage_ReleaseFailedSlot_ThenAdd")]
    public void AddPage_ReleaseFailedSlot_ThenAdd()
    {
        var store = new FakeSessionStore();
        (WallState state, SessionTracker tracker) = Start(store, Urls(16), 4);

        // Cases that cannot pop out: pointing at a managed window cell, out of range.
        Assert.Empty(state.ReleaseFailed(0));
        Assert.Empty(state.ReleaseFailed(-1));
        Assert.Empty(state.ReleaseFailed(16));

        // Expanded, wall minimized: no action.
        state.ClickCell(0);
        Assert.Empty(state.ReleaseFailed(4));
        state.Shrink();
        state.WallMinimized();
        Assert.Empty(state.ReleaseFailed(4));
        state.WallRestored();

        tracker.Sync(state.Slots);
        Assert.Equal(16, state.Slots.Count);
        Assert.Empty(store.Writes);

        // Popping out a failed cell: relayout, the list drops its URL, and adding is possible afterwards.
        Assert.Equal(new[] { WallAction.Relayout }, state.ReleaseFailed(4));
        tracker.Sync(state.Slots);
        Assert.Equal(15, state.Slots.Count);
        Assert.Single(store.Writes);
        Assert.DoesNotContain("https://example.com/5", store.LastWritten());
        Assert.Equal(15, store.LastWritten().Count);

        Assert.Null(AddPageFlow.Begin(state, launching: false));

        // Exiting: no action.
        (WallState exiting, SessionTracker _) = Start(new FakeSessionStore(), Urls(2), 1);
        exiting.BeginExit();
        Assert.Empty(exiting.ReleaseFailed(1));
        Assert.Equal(2, exiting.Slots.Count);
    }

    [Fact(DisplayName = "Session_LastWindowClosed_KeepsFailedUrl")]
    public void Session_LastWindowClosed_KeepsFailedUrl()
    {
        foreach (bool release in new[] { false, true })
        {
            var store = new FakeSessionStore();
            (WallState state, SessionTracker tracker) =
                Start(store, new[] { "https://example.com/a", "https://example.com/b" }, 1);

            IReadOnlyList<WallAction> actions = release ? state.ReleaseWindow(100) : state.WindowClosed(100);
            Assert.Equal(WallAction.Relayout, actions[^1]);
            Assert.Equal(WallMode.Grid, state.Mode);
            Assert.False(state.HasManagedWindows);
            Assert.Single(state.Slots);

            tracker.Sync(state.Slots);
            Assert.Single(store.Writes);
            Assert.Equal(new[] { "https://example.com/b" }, store.LastWritten());

            SessionLoadResult reload = SessionLoader.Load(store, ConfigUrls);
            Assert.Equal(SessionSource.Session, reload.Source);
            Assert.Equal(new[] { "https://example.com/b" }, reload.Urls);
        }
    }

    [Fact(DisplayName = "State_ReleaseLastFailed_WritesEmpty")]
    public void State_ReleaseLastFailed_WritesEmpty()
    {
        var store = new FakeSessionStore();
        (WallState state, SessionTracker tracker) = Start(store, Urls(2), 0, 1);
        Assert.Equal(WallMode.Grid, state.Mode);
        Assert.False(state.HasManagedWindows);

        Assert.Equal(new[] { WallAction.Relayout }, state.ReleaseFailed(0));
        Assert.Equal(WallMode.Grid, state.Mode);
        tracker.Sync(state.Slots);
        Assert.Equal(new[] { "https://example.com/2" }, store.LastWritten());

        Assert.Equal(new[] { WallAction.ShowEmpty }, state.ReleaseFailed(0));
        Assert.Equal(WallMode.Empty, state.Mode);
        tracker.Sync(state.Slots);
        Assert.Equal(2, store.Writes.Count);
        Assert.Empty(store.LastWritten());
    }
}
