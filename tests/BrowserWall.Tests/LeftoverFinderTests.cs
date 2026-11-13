using BrowserWall.Core.Detect;

namespace BrowserWall.Tests;

public class LeftoverFinderTests
{
    private const string Chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
    private const string Cls = WindowDetector.BrowserWindowClass;
    private const uint WallPid = 1000;

    [Fact(DisplayName = "Leftover_SameProcessBeforeLaunch_Closed")]
    public void SameProcessBeforeLaunch_Closed()
    {
        var snapshot = new[]
        {
            new WindowInfo(1, Chrome, Cls, "Left over A", true, WallPid),
            new WindowInfo(2, Chrome, Cls, "Everyday Chrome", true, 4000),
            new WindowInfo(3, @"C:\Users\user\AppData\Local\Programs\ChatGPT\ChatGPT.exe", Cls, "ChatGPT", true, 2000),
            new WindowInfo(4, Chrome, Cls, string.Empty, true, WallPid), // A title is not required
            new WindowInfo(5, Chrome, Cls, "Invisible", false, WallPid),
            new WindowInfo(6, Chrome, Cls, "Managed", true, WallPid),
            new WindowInfo(7, Chrome, "Chrome_RenderWidgetHostHWND", "Other class", true, WallPid),
            new WindowInfo(8, Chrome, Cls, "Unknown process", true, null),
        };

        IReadOnlyList<long> leftovers = LeftoverFinder.Find(snapshot, WallPid, new long[] { 6 });

        Assert.Equal(new long[] { 1, 4 }, leftovers);

        // No window of the wall's process in the snapshot → nothing to close
        Assert.Empty(LeftoverFinder.Find(snapshot, 9999, Array.Empty<long>()));
    }
}
