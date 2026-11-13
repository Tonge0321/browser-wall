using System.Text;
using BrowserWall.Core.Session;
using BrowserWall.Core.State;

namespace BrowserWall.Tests;

/// <summary>Test page list file: records every write; the read result and write failures can be set.</summary>
internal sealed class FakeSessionStore : ISessionStore
{
    public FakeSessionStore(SessionReadStatus status = SessionReadStatus.Missing, byte[]? content = null)
    {
        Status = status;
        Content = content;
    }

    public SessionReadStatus Status { get; set; }

    public byte[]? Content { get; private set; }

    public bool FailWrites { get; set; }

    public List<byte[]> Writes { get; } = new();

    public SessionReadStatus Read(out byte[]? bytes)
    {
        bytes = Status == SessionReadStatus.Read ? Content?.ToArray() : null;
        return Status;
    }

    public bool TryWrite(byte[] bytes)
    {
        Writes.Add(bytes.ToArray());
        if (FailWrites)
        {
            return false;
        }

        Content = bytes.ToArray();
        Status = SessionReadStatus.Read;
        return true;
    }

    /// <summary>The last written content, read back with <see cref="SessionLoader.Parse"/>.</summary>
    public List<string> LastWritten() => SessionLoader.Parse(Writes[^1])!;
}

public class SessionTests
{
    private static readonly string[] ConfigUrls = { "https://example.com/config" };

    private static byte[] Utf8(string text, bool bom = false)
    {
        byte[] body = new UTF8Encoding(false).GetBytes(text);
        return bom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray() : body;
    }

    private static string JsonArray(IEnumerable<string> urls) =>
        "[" + string.Join(",", urls.Select(u => System.Text.Json.JsonSerializer.Serialize(u))) + "]";

    private static string[] Urls(int count) =>
        Enumerable.Range(1, count).Select(i => $"https://example.com/{i}").ToArray();

    /// <summary>Like the App's startup flow: one cell per startup URL (window handle = 100 + index; failedIndex fails to open).</summary>
    private static (WallState State, SessionTracker Tracker) Start(FakeSessionStore store, string[] urls, int failedIndex = -1)
    {
        var state = new WallState();
        var tracker = new SessionTracker(store, urls);
        for (int i = 0; i < urls.Length; i++)
        {
            if (i == failedIndex)
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

    [Fact(DisplayName = "Session_LoadsValidList")]
    public void Session_LoadsValidList()
    {
        string[] two = { "https://example.com/a", "http://example.org/b" };
        var store = new FakeSessionStore(SessionReadStatus.Read, Utf8(JsonArray(two)));
        SessionLoadResult result = SessionLoader.Load(store, ConfigUrls);
        Assert.Equal(SessionSource.Session, result.Source);
        Assert.Equal(two, result.Urls);
        Assert.False(result.ShowInvalidNotice);
        Assert.Empty(store.Writes);

        string[] full = Urls(16);
        full[3] = "HTTPS://Example.com/4";
        full[6] = "https://example.com/a b";
        var fullStore = new FakeSessionStore(SessionReadStatus.Read, Utf8(JsonArray(full), bom: true));
        SessionLoadResult fullResult = SessionLoader.Load(fullStore, ConfigUrls);
        Assert.Equal(SessionSource.Session, fullResult.Source);
        Assert.Equal(full, fullResult.Urls);
        Assert.False(fullResult.ShowInvalidNotice);
        Assert.Empty(fullStore.Writes);
    }

    [Fact(DisplayName = "Session_MissingOrEmptyUsesConfig")]
    public void Session_MissingOrEmptyUsesConfig()
    {
        var missing = new FakeSessionStore(SessionReadStatus.Missing);
        SessionLoadResult m = SessionLoader.Load(missing, ConfigUrls);
        Assert.Equal(SessionSource.ConfigMissing, m.Source);
        Assert.Equal(ConfigUrls, m.Urls);
        Assert.False(m.ShowInvalidNotice);
        Assert.Empty(missing.Writes);

        var empty = new FakeSessionStore(SessionReadStatus.Read, Utf8("[]"));
        SessionLoadResult e = SessionLoader.Load(empty, ConfigUrls);
        Assert.Equal(SessionSource.ConfigEmpty, e.Source);
        Assert.Equal(ConfigUrls, e.Urls);
        Assert.False(e.ShowInvalidNotice);
        Assert.Empty(empty.Writes);
    }

    [Fact(DisplayName = "Session_InvalidUsesConfigAndKeepsFile")]
    public void Session_InvalidUsesConfigAndKeepsFile()
    {
        var unreadable = new FakeSessionStore(SessionReadStatus.Unreadable);
        SessionLoadResult u = SessionLoader.Load(unreadable, ConfigUrls);
        Assert.Equal(SessionSource.ConfigInvalid, u.Source);
        Assert.Equal(ConfigUrls, u.Urls);
        Assert.True(u.ShowInvalidNotice);
        Assert.Empty(unreadable.Writes);

        var bad = new List<byte[]>
        {
            new byte[] { 0x5B, 0xFF, 0xFE, 0x5D },
            Utf8("[\"https://example.com/1\","),
            Utf8("{\"urls\":[\"https://example.com/1\"]}"),
            Utf8(JsonArray(Urls(17))),
            Utf8("[\"https://example.com/1\", 5]"),
            Utf8("[\"ftp://example.com/1\"]"),
            Utf8("[\"example.com\"]"),
        };

        foreach (byte[] content in bad)
        {
            var store = new FakeSessionStore(SessionReadStatus.Read, content);
            SessionLoadResult result = SessionLoader.Load(store, ConfigUrls);

            Assert.Equal(SessionSource.ConfigInvalid, result.Source);
            Assert.Equal(ConfigUrls, result.Urls);
            Assert.True(result.ShowInvalidNotice);
            Assert.Empty(store.Writes);
            Assert.Equal(content, store.Content);
        }
    }

    [Fact(DisplayName = "Session_UpdatedOnAddRemoveClose")]
    public void Session_UpdatedOnAddRemoveClose()
    {
        // "+ Add page" has no URL and adds no cell when nothing is found; failed cells come only from startup.
        // Added cells join the list once a URL is recorded.
        var store = new FakeSessionStore();
        string[] urls = Urls(3).Append("https://example.com/fail").ToArray();
        (WallState state, SessionTracker tracker) = Start(store, urls, failedIndex: 3);
        var recorder = new LastSeenUrl();
        Assert.Empty(store.Writes);
        Assert.Equal(urls, tracker.Urls);

        // "+ Add page" found its window: not written without a URL; written once a URL is recorded.
        state.AddWindow(200, string.Empty);
        tracker.Sync(state.Slots);
        Assert.Empty(store.Writes);
        Assert.True(recorder.Record(state, 200, "https://example.com/new", false, TimeSpan.Zero));
        tracker.Sync(state.Slots);
        Assert.Single(store.Writes);
        Assert.Equal(
            new[] { "https://example.com/1", "https://example.com/2", "https://example.com/3", "https://example.com/fail", "https://example.com/new" },
            store.LastWritten());

        // Pop out one cell.
        Assert.NotEmpty(state.ReleaseWindow(101));
        tracker.Sync(state.Slots);
        Assert.Equal(2, store.Writes.Count);
        Assert.Equal(new[] { "https://example.com/1", "https://example.com/3", "https://example.com/fail", "https://example.com/new" }, store.LastWritten());

        // Added through "Add open window", no URL recorded yet: the list is not affected.
        Assert.NotEmpty(state.AdoptWindow(300));
        tracker.Sync(state.Slots);
        Assert.Equal(2, store.Writes.Count);

        // A failed write does not interrupt; the user closes a cell.
        store.FailWrites = true;
        Assert.NotEmpty(state.WindowClosed(100));
        tracker.Sync(state.Slots);
        Assert.Equal(3, store.Writes.Count);
        Assert.Equal(new[] { "https://example.com/3", "https://example.com/fail", "https://example.com/new" }, tracker.Urls);

        // After writes recover, the next change writes the full list as usual.
        store.FailWrites = false;
        Assert.NotEmpty(state.SyncAlive(new long[] { 102, 300 }));
        tracker.Sync(state.Slots);
        Assert.Equal(4, store.Writes.Count);
        Assert.Equal(new[] { "https://example.com/3", "https://example.com/fail" }, store.LastWritten());

        // The added cell joins once a URL is recorded; removed after it is closed.
        Assert.True(recorder.Record(state, 300, "https://example.com/adopted", false, TimeSpan.Zero));
        tracker.Sync(state.Slots);
        Assert.Equal(5, store.Writes.Count);
        Assert.Equal(new[] { "https://example.com/3", "https://example.com/fail", "https://example.com/adopted" }, store.LastWritten());
        Assert.NotEmpty(state.WindowClosed(300));
        tracker.Sync(state.Slots);
        Assert.Equal(6, store.Writes.Count);
        Assert.Equal(new[] { "https://example.com/3", "https://example.com/fail" }, store.LastWritten());

        // The last managed window closes: only that cell's URL is removed; the failed cell and its URL stay.
        Assert.NotEmpty(state.WindowClosed(102));
        tracker.Sync(state.Slots);
        Assert.Equal(7, store.Writes.Count);
        Assert.Equal(new[] { "https://example.com/fail" }, store.LastWritten());

        // Popping out the last failed cell: the list becomes empty and an empty array is written.
        Assert.Equal(new[] { WallAction.ShowEmpty }, state.ReleaseFailed(0));
        tracker.Sync(state.Slots);
        Assert.Equal(8, store.Writes.Count);
        Assert.Empty(store.LastWritten());
        Assert.Equal("[]", Encoding.UTF8.GetString(store.Writes[^1]));
    }

    [Fact(DisplayName = "Session_NotUpdatedOnWallExit")]
    public void Session_NotUpdatedOnWallExit()
    {
        var store = new FakeSessionStore();
        (WallState state, SessionTracker tracker) = Start(store, Urls(3));

        state.BeginExit();
        tracker.BeginExit();

        // URLs read after the wall starts closing are not recorded.
        Assert.False(new LastSeenUrl().Record(state, 100, "https://example.com/late", false, TimeSpan.Zero));
        Assert.False(state.SetWindowUrl(100, "https://example.com/late"));
        tracker.Sync(state.Slots);

        // Windows closed together with the wall: all cells disappear.
        tracker.Sync(Array.Empty<WallSlot>());

        Assert.Empty(store.Writes);
        Assert.Equal(Urls(3), tracker.Urls);
    }

    [Fact(DisplayName = "Session_FullWithFailed_KeepsFailedUrl")]
    public void Session_FullWithFailed_KeepsFailedUrl()
    {
        var store = new FakeSessionStore();
        string[] full = Urls(16);
        (WallState state, SessionTracker tracker) = Start(store, full, failedIndex: 4);
        Assert.Equal(16, state.Slots.Count);
        Assert.Empty(store.Writes);

        // Cells full: adding an open window does nothing, does not push out the failed cell, and writes nothing.
        Assert.Empty(state.AdoptWindow(300));
        tracker.Sync(state.Slots);
        Assert.Empty(store.Writes);
        Assert.Equal(full, tracker.Urls);

        // Pop out one cell (cell 1, window 100): 15 entries written, still including the failed URL.
        Assert.NotEmpty(state.ReleaseWindow(100));
        tracker.Sync(state.Slots);
        Assert.Single(store.Writes);
        List<string> afterRelease = store.LastWritten();
        Assert.Equal(15, afterRelease.Count);
        Assert.Contains("https://example.com/5", afterRelease);
        Assert.DoesNotContain("https://example.com/1", afterRelease);
        AssertReadsBack(store);

        // Add 1 cell and record its URL: 16 entries, still including the failed URL.
        state.AddWindow(201, string.Empty);
        Assert.True(new LastSeenUrl().Record(state, 201, "https://example.com/new1", false, TimeSpan.Zero));
        tracker.Sync(state.Slots);
        Assert.Equal(16, tracker.Urls.Count);
        Assert.Equal(16, state.Slots.Count);
        Assert.Contains("https://example.com/5", store.LastWritten());
        AssertReadsBack(store);

        // Everything written can be read back and has at most 16 entries.
        foreach (byte[] written in store.Writes)
        {
            Assert.NotNull(SessionLoader.Parse(written));
            Assert.True(SessionLoader.Parse(written)!.Count <= 16);
        }

        // The list has at most 16 entries (safeguard: it stays within the limit when the cells are full).
        Assert.False(state.HasFreeSlot);
        Assert.Equal(16, tracker.Urls.Count);
    }

    [Fact(DisplayName = "Session_FollowsGridSkipsCellsWithoutUrl")]
    public void Session_FollowsGridSkipsCellsWithoutUrl()
    {
        var store = new FakeSessionStore();
        string[] urls = { "https://example.com/a", "https://example.com/f" };
        var state = new WallState();
        var tracker = new SessionTracker(store, urls);
        var recorder = new LastSeenUrl();

        // Startup cell (A), added cell (no URL), failed cell (F). Same content as at startup: not written.
        state.AddWindow(100, urls[0], tracker.StartupEntry(0));
        tracker.Sync(state.Slots);
        state.AdoptWindow(300);
        tracker.Sync(state.Slots);
        state.AddFailed(urls[1], tracker.StartupEntry(1));
        tracker.Sync(state.Slots);
        Assert.Empty(store.Writes);
        Assert.Equal(urls, tracker.Urls);

        // The startup cell reads B: rewritten in full in cell order, skipping cells without a URL.
        Assert.True(recorder.Record(state, 100, "https://example.com/b", false, TimeSpan.Zero));
        tracker.Sync(state.Slots);
        Assert.Single(store.Writes);
        Assert.Equal(new[] { "https://example.com/b", "https://example.com/f" }, store.LastWritten());

        // Content unchanged: not written.
        tracker.Sync(state.Slots);
        Assert.Single(store.Writes);

        // Swap: the order follows the cells.
        Assert.NotEmpty(state.SwapCells(0, 2));
        tracker.Sync(state.Slots);
        Assert.Equal(2, store.Writes.Count);
        Assert.Equal(new[] { "https://example.com/f", "https://example.com/b" }, store.LastWritten());

        // Swapping a cell without a URL, content unchanged: not written.
        Assert.NotEmpty(state.SwapCells(1, 2));
        tracker.Sync(state.Slots);
        Assert.Equal(2, store.Writes.Count);
    }

    [Fact(DisplayName = "Session_StartupPendingKeptAndInvalidFileNotOverwritten")]
    public void Session_StartupPendingKeptAndInvalidFileNotOverwritten()
    {
        // An invalid list file falls back to the config file: not overwritten while the content is unchanged.
        byte[] bad = Utf8("[\"example.com\"]");
        var store = new FakeSessionStore(SessionReadStatus.Read, bad);
        string[] urls = { "https://example.com/1", "https://example.com/2" };
        var state = new WallState();
        var tracker = new SessionTracker(store, urls);
        state.AddWindow(100, urls[0], tracker.StartupEntry(0));
        tracker.Sync(state.Slots);
        Assert.Empty(store.Writes);
        Assert.Equal(bad, store.Content);

        // Cell 1 changes URL during startup: the not-yet-opened second entry stays at the end of the list.
        Assert.True(new LastSeenUrl().Record(state, 100, "https://example.com/x", false, TimeSpan.Zero));
        tracker.Sync(state.Slots);
        Assert.Equal(new[] { "https://example.com/x", "https://example.com/2" }, store.LastWritten());

        // Write fails: no exception; the next Sync writes again.
        store.FailWrites = true;
        state.AddWindow(101, urls[1], tracker.StartupEntry(1));
        Assert.True(new LastSeenUrl().Record(state, 101, "https://example.com/y", false, TimeSpan.Zero));
        tracker.Sync(state.Slots);
        int failedCount = store.Writes.Count;
        store.FailWrites = false;
        tracker.Sync(state.Slots);
        Assert.Equal(failedCount + 1, store.Writes.Count);
        Assert.Equal(new[] { "https://example.com/x", "https://example.com/y" }, store.LastWritten());
    }

    private static void AssertReadsBack(FakeSessionStore store)
    {
        SessionLoadResult result = SessionLoader.Load(store, ConfigUrls);
        Assert.Equal(SessionSource.Session, result.Source);
        Assert.Equal(store.LastWritten(), result.Urls);
    }
}
