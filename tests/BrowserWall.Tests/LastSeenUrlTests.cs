using BrowserWall.Core.Session;
using BrowserWall.Core.State;

namespace BrowserWall.Tests;

public class LastSeenUrlTests
{
    private static string UrlOf(WallState state, long id) =>
        state.Slots.First(s => s.WindowId == id).Url;

    [Fact(DisplayName = "Url_RecordsHttpOnlyLastSeen")]
    public void Url_RecordsHttpOnlyLastSeen()
    {
        var state = new WallState();
        state.AddWindow(100, "https://example.com/start");
        state.AdoptWindow(300);
        var recorder = new LastSeenUrl();
        TimeSpan t = TimeSpan.Zero;
        TimeSpan Next() => t += LastSeenUrl.RecordInterval;

        Assert.True(recorder.Record(state, 100, "https://a/1", false, Next()));
        Assert.Equal("https://a/1", UrlOf(state, 100));
        Assert.True(recorder.Record(state, 100, "http://b/", false, Next()));
        Assert.Equal("http://b/", UrlOf(state, 100));

        // New tab, other scheme, unreadable: not recorded this time; the old value is kept.
        foreach (string? value in new[] { "", "   ", "chrome://newtab/", "about:blank", "file:///C:/x", "data:text/html,x", null })
        {
            Assert.False(recorder.Record(state, 100, value, false, Next()), value ?? "null");
            Assert.Equal("http://b/", UrlOf(state, 100));
        }

        // The same value is not recorded twice.
        Assert.False(recorder.Record(state, 100, "http://b/", false, Next()));

        // An added cell that never recorded a URL: still no URL, not in the list.
        Assert.False(recorder.Record(state, 300, "chrome://newtab/", false, Next()));
        Assert.Equal(string.Empty, UrlOf(state, 300));
        var tracker = new SessionTracker(new FakeSessionStore(), Array.Empty<string>());
        tracker.Sync(state.Slots);
        Assert.Equal(new[] { "http://b/" }, tracker.Urls);

        // Not a managed window: not recorded.
        Assert.False(recorder.Record(state, 999, "https://x/", false, Next()));

        // ToRecord rules.
        Assert.Equal("HTTPS://Example.com/A", LastSeenUrl.ToRecord("  HTTPS://Example.com/A ", false));
        Assert.Null(LastSeenUrl.ToRecord("ftp://x", false));
    }

    [Fact(DisplayName = "Url_SkipsWhileAddressBarFocused")]
    public void Url_SkipsWhileAddressBarFocused()
    {
        var state = new WallState();
        state.AddWindow(100, "https://example.com/start");
        var recorder = new LastSeenUrl();

        // The address bar has keyboard focus (the user is typing): not read, not recorded.
        Assert.Null(LastSeenUrl.ToRecord("https://c/", true));
        Assert.False(recorder.Record(state, 100, "https://c/", true, TimeSpan.FromSeconds(2)));
        Assert.False(recorder.Record(state, 100, "typing", true, TimeSpan.FromSeconds(4)));
        Assert.Equal("https://example.com/start", UrlOf(state, 100));

        // Recorded only when the same value is read after focus leaves.
        Assert.True(recorder.Record(state, 100, "https://c/", false, TimeSpan.FromSeconds(6)));
        Assert.Equal("https://c/", UrlOf(state, 100));
    }

    [Fact(DisplayName = "Url_AddsHttpsWhenSchemeMissing")]
    public void Url_AddsHttpsWhenSchemeMissing()
    {
        var cases = new Dictionary<string, string>
        {
            ["example.com/path?x=1"] = "https://example.com/path?x=1",
            ["example.com"] = "https://example.com",
            ["  time.is  "] = "https://time.is",
            ["localhost:8080/x"] = "https://localhost:8080/x",
            ["example.com/a:b"] = "https://example.com/a:b",
        };

        foreach ((string read, string expected) in cases)
        {
            Assert.Equal(expected, LastSeenUrl.ToRecord(read, false));
        }

        // Not URL-like (contains whitespace), other scheme: not recorded.
        foreach (string read in new[] { "a b", "search words", "chrome:x", "about:blank", "javascript:void(0)", "mailto:a@b" })
        {
            Assert.Null(LastSeenUrl.ToRecord(read, false));
        }

        // Record stores the completed URL.
        var state = new WallState();
        state.AdoptWindow(300);
        Assert.True(new LastSeenUrl().Record(state, 300, "example.com/path?x=1", false, TimeSpan.Zero));
        Assert.Equal("https://example.com/path?x=1", UrlOf(state, 300));
    }

    [Fact(DisplayName = "Url_SameCellAtMostEveryTwoSeconds")]
    public void Url_SameCellAtMostEveryTwoSeconds()
    {
        var state = new WallState();
        state.AddWindow(100, "https://example.com/start");
        state.AddWindow(101, "https://example.com/other");
        var recorder = new LastSeenUrl();

        Assert.True(recorder.Record(state, 100, "https://a/", false, TimeSpan.FromSeconds(10)));

        // A second change of the same cell within 2 seconds: not recorded; other cells are not affected.
        Assert.False(recorder.Record(state, 100, "https://b/", false, TimeSpan.FromSeconds(11.9)));
        Assert.Equal("https://a/", UrlOf(state, 100));
        Assert.True(recorder.Record(state, 101, "https://c/", false, TimeSpan.FromSeconds(11.9)));

        // After 2 seconds: recorded.
        Assert.True(recorder.Record(state, 100, "https://b/", false, TimeSpan.FromSeconds(12)));
        Assert.Equal("https://b/", UrlOf(state, 100));

        // After the wall starts closing: not recorded.
        state.BeginExit();
        Assert.False(recorder.Record(state, 100, "https://z/", false, TimeSpan.FromSeconds(30)));
    }
}
