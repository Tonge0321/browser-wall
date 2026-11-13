using BrowserWall.Core.Detect;

namespace BrowserWall.Tests;

public class WindowDetectorTests
{
    private const string Chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
    private const string OtherChrome = @"C:\Users\Tester\AppData\Local\Google\Chrome\Application\chrome.exe";
    private const string Class = "Chrome_WidgetWin_1";

    private static readonly TimeSpan At1s = TimeSpan.FromSeconds(1);

    private static WindowInfo Browser(long id, string title = "New Tab - Google Chrome") =>
        new(id, Chrome, Class, title, true);

    private static WindowInfo Other(long id) =>
        new(id, @"C:\Windows\explorer.exe", "CabinetWClass", "File Explorer", true);

    [Fact(DisplayName = "Detect_NewWindowAfterLaunch_Found")]
    public void NewWindowAfterLaunch_Found()
    {
        var detector = new WindowDetector(Chrome, new long[] { 1, 2 });

        Assert.Equal(DetectResult.Pending, detector.Poll(new[] { Other(1), Browser(2) }, TimeSpan.FromMilliseconds(200)));
        Assert.Equal(DetectResult.Found(3), detector.Poll(new[] { Browser(3), Other(1), Browser(2) }, TimeSpan.FromMilliseconds(400)));
    }

    [Fact(DisplayName = "Detect_ExistingWindowIgnored")]
    public void ExistingWindowIgnored()
    {
        var detector = new WindowDetector(Chrome, new long[] { 2 });

        Assert.Equal(DetectResult.Pending, detector.Poll(new[] { Browser(2) }, At1s));
        Assert.Equal(DetectResult.Found(5), detector.Poll(new[] { Browser(2), Browser(5) }, At1s));
    }

    [Fact(DisplayName = "Detect_WrongClassIgnored")]
    public void WrongClassIgnored()
    {
        var detector = new WindowDetector(Chrome, Array.Empty<long>());

        var windows = new[]
        {
            new WindowInfo(7, Chrome, "Chrome_WidgetWin_0", "Title", true),
            new WindowInfo(8, Chrome, "chrome_widgetwin_1", "Title", true),
        };
        Assert.Equal(DetectResult.Pending, detector.Poll(windows, At1s));
    }

    [Fact(DisplayName = "Detect_Timeout_ReportsFailure")]
    public void Timeout_ReportsFailure()
    {
        var detector = new WindowDetector(Chrome, new long[] { 1 });
        WindowInfo[] none = { Other(1) };

        Assert.Equal(DetectResult.Pending, detector.Poll(none, TimeSpan.FromMilliseconds(9800)));
        Assert.Equal(DetectResult.TimedOut, detector.Poll(none, TimeSpan.FromSeconds(10)));
        Assert.Equal(DetectResult.TimedOut, detector.Poll(none, TimeSpan.FromSeconds(12)));

        // The poll at exactly 10 seconds still matches first
        Assert.Equal(DetectResult.Found(9), detector.Poll(new[] { Other(1), Browser(9) }, TimeSpan.FromSeconds(10)));
    }

    [Fact(DisplayName = "Detect_OtherExecutableIgnored")]
    public void OtherExecutableIgnored()
    {
        var detector = new WindowDetector(Chrome, Array.Empty<long>());

        var windows = new[]
        {
            new WindowInfo(3, OtherChrome, Class, "Title", true),
            new WindowInfo(4, null, Class, "Title", true),
        };
        Assert.Equal(DetectResult.Pending, detector.Poll(windows, At1s));
    }

    [Fact(DisplayName = "Detect_ExecutablePathCaseInsensitive")]
    public void ExecutablePathCaseInsensitive()
    {
        var detector = new WindowDetector(Chrome, Array.Empty<long>());

        var window = new WindowInfo(3, Chrome.ToUpperInvariant(), Class, "Title", true);
        Assert.Equal(DetectResult.Found(3), detector.Poll(new[] { window }, At1s));
    }

    [Fact(DisplayName = "Detect_UntitledOrHiddenIgnored")]
    public void UntitledOrHiddenIgnored()
    {
        var detector = new WindowDetector(Chrome, Array.Empty<long>());

        var windows = new[]
        {
            new WindowInfo(3, Chrome, Class, "", true),
            new WindowInfo(4, Chrome, Class, "   ", true),
            new WindowInfo(5, Chrome, Class, "Title", false),
        };
        Assert.Equal(DetectResult.Pending, detector.Poll(windows, At1s));
    }

    [Fact(DisplayName = "Detect_MultipleMatches_FirstInOrder")]
    public void MultipleMatches_FirstInOrder()
    {
        var detector = new WindowDetector(Chrome, Array.Empty<long>());

        Assert.Equal(DetectResult.Found(6), detector.Poll(new[] { Other(1), Browser(6), Browser(4) }, At1s));
    }
}
