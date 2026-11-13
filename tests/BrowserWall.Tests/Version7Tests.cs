using BrowserWall.Core.Health;
using BrowserWall.Core.Infra;
using BrowserWall.Core.Layout;
using BrowserWall.Core.Session;
using BrowserWall.Core.State;

namespace BrowserWall.Tests;

/// <summary>
/// Page list write failure logging, taskbar replay after recovery, and finishing the log before the wall closes.
/// The browser preferences tests are in <see cref="PreferencesWriteTests"/>.
/// </summary>
public class Version7Tests
{
    [Fact(DisplayName = "Session_WriteFailureLoggedOncePerEpisode")]
    public void WriteFailureLoggedOncePerEpisode()
    {
        string[] urls = { "https://example.com/1", "https://example.com/2", "https://example.com/3" };
        var store = new FakeSessionStore();
        var state = new WallState();
        var tracker = new SessionTracker(store, urls);
        var events = new List<EpisodeChange>();
        tracker.WriteStatusChanged += events.Add;
        for (int i = 0; i < urls.Length; i++)
        {
            state.AddWindow(100 + i, urls[i], tracker.StartupEntry(i));
            tracker.Sync(state.Slots);
        }

        // Content unchanged: not written, not triggered.
        Assert.Empty(store.Writes);
        Assert.Empty(events);

        // Three failures in a row: only the first one notifies.
        store.FailWrites = true;
        Assert.NotEmpty(state.ReleaseWindow(101));
        tracker.Sync(state.Slots);
        tracker.Sync(state.Slots);
        tracker.Sync(state.Slots);
        Assert.Equal(3, store.Writes.Count);
        Assert.Equal(new[] { EpisodeChange.FirstFailure }, events);

        // The first success afterwards: notifies recovery once; further successes do not.
        store.FailWrites = false;
        tracker.Sync(state.Slots);
        Assert.Equal(new[] { EpisodeChange.FirstFailure, EpisodeChange.Recovered }, events);
        Assert.NotEmpty(state.ReleaseWindow(102));
        tracker.Sync(state.Slots);
        Assert.Equal(5, store.Writes.Count);
        Assert.Equal(2, events.Count);

        // Fails again: a new episode, the first failure is notified again.
        store.FailWrites = true;
        Assert.NotEmpty(state.ReleaseWindow(100));
        tracker.Sync(state.Slots);
        Assert.Equal(new[] { EpisodeChange.FirstFailure, EpisodeChange.Recovered, EpisodeChange.FirstFailure }, events);

        // Log lines have no URLs.
        string failed = DiagnosticsFormat.SessionWrite(new DateTime(2026, 11, 13, 1, 0, 0, DateTimeKind.Utc), EpisodeChange.FirstFailure);
        string recovered = DiagnosticsFormat.SessionWrite(new DateTime(2026, 11, 13, 1, 0, 0, DateTimeKind.Utc), EpisodeChange.Recovered);
        Assert.Contains("session write failed", failed);
        Assert.Contains("session write recovered", recovered);
        Assert.DoesNotContain("example.com", failed + recovered);
        Assert.DoesNotContain("://", failed + recovered);
    }

    [Fact(DisplayName = "Taskbar_ReplaysLastRequestAfterRecover")]
    public void ReplaysLastRequestAfterRecover()
    {
        var alive = new HashSet<long> { 1, 2, 3 };
        bool Exists(long id) => alive.Contains(id);

        // Versions increase in send order (same as the return value of LatestRequest.Request).
        long v = 0;

        // (1) While at the cap, "hide" then "show" for the same window are both skipped: on recovery only one "show" is replayed, and it is cleared once taken.
        var missed = new MissedRequests<long, bool>();
        missed.CapReached();
        long hide1 = ++v;
        missed.Requested(1, hide1);
        Assert.Empty(missed.Missed(1, false, hide1, Exists));
        long show1 = ++v;
        missed.Requested(1, show1);
        Assert.Empty(missed.Missed(1, true, show1, Exists));
        long hide2 = ++v;
        missed.Requested(2, hide2);
        Assert.Empty(missed.Missed(2, false, hide2, Exists));
        alive.Remove(2); // Window 2 closed before recovery: not replayed.
        var replay = missed.Recovered(Exists);
        Assert.Equal(new[] { (1L, true) }, replay);
        Assert.Equal(0, missed.PendingCount);
        Assert.Empty(missed.Recovered(Exists));

        // (2) Recovered first, registered later: the registration returns the one to replay right away.
        long hide3 = ++v;
        missed.Requested(3, hide3);
        Assert.Equal(new[] { (3L, false) }, missed.Missed(3, false, hide3, Exists));

        // (3) The same window is skipped again after the same recovery: not replayed at once, kept for the next recovery.
        Assert.Empty(missed.Missed(3, false, hide3, Exists));
        Assert.Equal(1, missed.PendingCount);
        missed.CapReached();
        Assert.Equal(new[] { (3L, false) }, missed.Recovered(Exists));

        // (4) A new request arrives during the replay: it clears the pending one; a later registration stores the new state.
        missed.CapReached();
        long hide4 = ++v;
        missed.Requested(1, hide4);
        Assert.Empty(missed.Missed(1, false, hide4, Exists));
        long show4 = ++v;
        missed.Requested(1, show4);
        Assert.Equal(0, missed.PendingCount);
        Assert.Empty(missed.Missed(1, true, show4, Exists));
        Assert.Equal(new[] { (1L, true) }, missed.Recovered(Exists));

        // (5) Interleaving: while stuck, an old "hide" report takes its version and pauses; the user pops the window out, and the
        //     new "show" is skipped and registered; only then does the old report register "hide" — it must not overwrite the newer
        //     "show", and recovery replays "show".
        missed.CapReached();
        long staleHide = ++v;
        missed.Requested(1, staleHide);
        long newShow = ++v;
        missed.Requested(1, newShow);
        Assert.Empty(missed.Missed(1, true, newShow, Exists));
        Assert.Empty(missed.Missed(1, false, staleHide, Exists));
        Assert.Equal(new[] { (1L, true) }, missed.Recovered(Exists));

        // (6) Interleaving: an old report registers after a new request was sent, and the new request has no result yet: the old one is not registered (the new request does the work).
        missed.CapReached();
        long oldHide = ++v;
        missed.Requested(1, oldHide);
        long laterShow = ++v;
        missed.Requested(1, laterShow);
        Assert.Empty(missed.Missed(1, false, oldHide, Exists));
        Assert.Equal(0, missed.PendingCount);
        Assert.Empty(missed.Recovered(Exists));

        // Source of the last requested state.
        var latest = new LatestRequest<long, bool>();
        Assert.False(latest.TryGetLatest(9, out _));
        latest.Request(9, false);
        long last = latest.Request(9, true);
        Assert.True(latest.TryGetLatest(9, out bool state));
        Assert.True(state);
        Assert.True(latest.TryGetLatest(9, out bool state2, out long version));
        Assert.True(state2);
        Assert.Equal(last, version);
    }

    [Fact(DisplayName = "Exit_LogFlushWaitWithinThreeSeconds")]
    public void LogFlushWaitWithinThreeSeconds()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(500), ExitBudget.LogFlushWait(TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromMilliseconds(500), ExitBudget.LogFlushWait(TimeSpan.FromMilliseconds(2300)));
        Assert.Equal(TimeSpan.FromMilliseconds(300), ExitBudget.LogFlushWait(TimeSpan.FromMilliseconds(2500)));
        Assert.Equal(TimeSpan.Zero, ExitBudget.LogFlushWait(TimeSpan.FromMilliseconds(2900)));
        Assert.Equal(TimeSpan.Zero, ExitBudget.LogFlushWait(TimeSpan.FromSeconds(5)));

        // The temp file delete failure line has only time and fixed text.
        string line = DiagnosticsFormat.PreferencesTempDeleteFailed(new DateTime(2026, 11, 13, 1, 0, 0, DateTimeKind.Utc));
        Assert.Equal("2026-11-13T01:00:00Z\t-\tpreferences temp file delete failed", line);
    }
}
