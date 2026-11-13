namespace BrowserWall.Core.Infra;

/// <summary>
/// How long closing the wall may wait for the diagnostics log to finish writing: at most 500 ms, and the whole exit must still finish within 3 seconds.
/// </summary>
public static class ExitBudget
{
    /// <summary>Time limit for the program to exit completely after the wall is closed.</summary>
    public static readonly TimeSpan Total = TimeSpan.FromSeconds(3);

    /// <summary>Margin left for the program itself to exit.</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromMilliseconds(200);

    /// <summary>Maximum wait for the diagnostics log to finish writing.</summary>
    public static readonly TimeSpan MaxLogFlush = TimeSpan.FromMilliseconds(500);

    /// <summary>With <paramref name="elapsed"/> since closing started, how long to wait for the diagnostics log now: min(500 ms, max(0, 3 s − 200 ms − elapsed)).</summary>
    public static TimeSpan LogFlushWait(TimeSpan elapsed)
    {
        TimeSpan left = Total - Margin - elapsed;
        if (left <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return left < MaxLogFlush ? left : MaxLogFlush;
    }
}
