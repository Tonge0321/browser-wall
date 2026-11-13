namespace BrowserWall.Core.Launch;

/// <summary>Browser launch arguments.</summary>
public static class BrowserArguments
{
    public const string DisableOcclusion = "--disable-features=CalculateNativeWinOcclusion";
    public const string NewWindow = "--new-window";

    /// <summary>
    /// Returns, in order: <c>--user-data-dir=&lt;profileDir&gt;</c>, <c>--disable-features=CalculateNativeWinOcclusion</c>,
    /// <c>--new-window</c>, the URL. Each is a separate argument (the App adds them one by one through ArgumentList).
    /// </summary>
    public static IReadOnlyList<string> For(string profileDir, string url)
    {
        ArgumentNullException.ThrowIfNull(profileDir);
        ArgumentNullException.ThrowIfNull(url);
        return new[] { "--user-data-dir=" + profileDir, DisableOcclusion, NewWindow, url };
    }
}
