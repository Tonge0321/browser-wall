namespace BrowserWall.Core.Detect;

/// <summary>Kind of match result.</summary>
public enum DetectStatus
{
    Pending,
    Found,
    TimedOut,
}

/// <summary>Result of one poll; <see cref="WindowId"/> is set only when Found.</summary>
public readonly record struct DetectResult(DetectStatus Status, long? WindowId = null)
{
    public static DetectResult Pending { get; } = new(DetectStatus.Pending);

    public static DetectResult TimedOut { get; } = new(DetectStatus.TimedOut);

    public static DetectResult Found(long windowId) => new(DetectStatus.Found, windowId);
}

/// <summary>
/// Finds the window that newly appeared after one browser launch.
/// Matches: did not exist before the launch, visible, belongs to the browser executable, class name Chrome_WidgetWin_1, has a title.
/// </summary>
public sealed class WindowDetector
{
    public const string BrowserWindowClass = "Chrome_WidgetWin_1";

    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly string _browserPath;
    private readonly HashSet<long> _before;

    /// <param name="browserPath">Absolute path of the browser executable.</param>
    /// <param name="visibleBefore">Handles of all visible top-level windows before the launch.</param>
    public WindowDetector(string browserPath, IEnumerable<long> visibleBefore)
    {
        ArgumentNullException.ThrowIfNull(browserPath);
        ArgumentNullException.ThrowIfNull(visibleBefore);
        _browserPath = Normalize(browserPath) ?? throw new ArgumentException("Invalid browser path.", nameof(browserPath));
        _before = new HashSet<long>(visibleBefore);
    }

    /// <summary>
    /// Matches against the current top-level windows (in enumeration order). Matching comes first; it times out only when
    /// nothing matches and <paramref name="elapsed"/> has reached 10 seconds.
    /// </summary>
    public DetectResult Poll(IEnumerable<WindowInfo> current, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(current);

        foreach (WindowInfo window in current)
        {
            if (IsMatch(window))
            {
                return DetectResult.Found(window.Id);
            }
        }

        return elapsed >= Timeout ? DetectResult.TimedOut : DetectResult.Pending;
    }

    private bool IsMatch(WindowInfo window) =>
        !_before.Contains(window.Id)
        && window.Visible
        && string.Equals(window.ClassName, BrowserWindowClass, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(window.Title)
        && window.ExecutablePath is not null
        && string.Equals(Normalize(window.ExecutablePath), _browserPath, StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
