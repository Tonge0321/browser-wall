using System.Text;
using BrowserWall.Core;
using BrowserWall.Core.Config;
using BrowserWall.Core.Detect;
using BrowserWall.Core.Layout;
using BrowserWall.Core.Session;
using BrowserWall.Core.State;
using BrowserWall.Core.Text;

namespace BrowserWall.Tests;

public class LimitTests
{
    private static string[] Urls(int count) =>
        Enumerable.Range(1, count).Select(i => $"https://example.com/{i}").ToArray();

    private static byte[] ConfigWith(int count) => new UTF8Encoding(false).GetBytes(
        "{\"browserPath\":" + System.Text.Json.JsonSerializer.Serialize(FakeConfigEnvironment.ChromePath)
        + ",\"urls\":" + System.Text.Json.JsonSerializer.Serialize(Urls(count)) + "}");

    private static byte[] ListWith(int count) =>
        new UTF8Encoding(false).GetBytes(System.Text.Json.JsonSerializer.Serialize(Urls(count)));

    [Fact(DisplayName = "Limit_SixteenSharedEverywhere")]
    public void Limit_SixteenSharedEverywhere()
    {
        Assert.Equal(16, WallLimits.MaxCells);

        // Config file
        var env = new FakeConfigEnvironment(FakeConfigEnvironment.ChromePath);
        Assert.True(ConfigParser.Parse(ConfigWith(16), env).Success);
        Assert.False(ConfigParser.Parse(ConfigWith(17), env).Success);

        // Last page list
        string[] config = { "https://example.com/config" };
        SessionLoadResult ok = SessionLoader.Load(new FakeSessionStore(SessionReadStatus.Read, ListWith(16)), config);
        Assert.Equal(SessionSource.Session, ok.Source);
        Assert.Equal(16, ok.Urls.Count);
        SessionLoadResult tooMany = SessionLoader.Load(new FakeSessionStore(SessionReadStatus.Read, ListWith(17)), config);
        Assert.Equal(SessionSource.ConfigInvalid, tooMany.Source);
        Assert.Throws<ArgumentException>(() => new SessionTracker(new FakeSessionStore(), Urls(17)));

        // State machine and "+ Add page"
        var state = new WallState();
        for (long id = 1; id <= 15; id++)
        {
            state.AddWindow(id, $"https://example.com/{id}");
        }

        Assert.True(state.HasFreeSlot);
        Assert.Equal(1, state.FreeSlotCount);
        Assert.True(AddPicker.CanAdd(state));

        state.AddWindow(16, "https://example.com/16");
        Assert.False(state.HasFreeSlot);
        Assert.Equal(0, state.FreeSlotCount);
        Assert.False(AddPicker.CanAdd(state));
        Assert.Empty(state.AdoptWindow(17));
        Assert.Empty(state.AutoAddWindow(17));
        Assert.Throws<InvalidOperationException>(() => state.AddFailed("https://example.com/17"));
        Assert.Equal(16, state.Slots.Count);

        // Layout
        GridResult layout = GridLayout.Compute(16, 1920, 1040);
        Assert.Equal(4, layout.Columns);
        Assert.Equal(4, layout.Rows);
        Assert.Throws<ArgumentOutOfRangeException>(() => GridLayout.Compute(17, 1920, 1040));

        // The "+ Add page" full explanation uses the same limit
        Assert.Contains("16", UiStrings.Format(UiLanguage.ZhTw, UiTextKey.AddFull, WallLimits.MaxCells));
        Assert.Contains("16", UiStrings.Format(UiLanguage.En, UiTextKey.AddFull, WallLimits.MaxCells));
    }
}
