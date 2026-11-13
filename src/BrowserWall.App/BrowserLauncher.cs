using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using BrowserWall.Core.Config;
using BrowserWall.Core.Detect;
using BrowserWall.Core.Launch;

namespace BrowserWall.App;

/// <summary>
/// Launches the browser for each URL in order and finds its new window. One URL at a time; waits with await on the UI thread without blocking the UI.
/// </summary>
internal static class BrowserLauncher
{
    /// <param name="urls">URLs to open (the last page list or the config file's urls), opened one by one in order.</param>
    /// <param name="onFound">Called when a window is found (window handle, URL, index).</param>
    /// <param name="onFailed">Called when the launch fails or nothing is found within 10 seconds (URL, index).</param>
    /// <param name="isCancelled">Returns true once the wall is closed, stopping further launches.</param>
    public static async Task LaunchAllAsync(
        WallConfig config,
        IReadOnlyList<string> urls,
        Action<long, string, int> onFound,
        Action<string, int> onFailed,
        Func<bool> isCancelled,
        DiagnosticsLog diagnostics)
    {
        for (int index = 0; index < urls.Count; index++)
        {
            string url = urls[index];
            if (isCancelled())
            {
                return;
            }

            long? found = await LaunchOneAsync(config, url, isCancelled, diagnostics);
            if (isCancelled())
            {
                return;
            }

            if (found is long id)
            {
                onFound(id, url, index);
            }
            else
            {
                onFailed(url, index);
            }
        }
    }

    /// <summary>
    /// Opens one URL and finds its new window; returns null when the launch fails, nothing is found within 10 seconds, or it is cancelled.
    /// Shared by startup and "+ Add page".
    /// </summary>
    internal static async Task<long?> LaunchOneAsync(
        WallConfig config, string url, Func<bool> isCancelled, DiagnosticsLog diagnostics)
    {
        var detector = new WindowDetector(config.BrowserPath, WindowSystem.VisibleTopLevelIds());

        // Always show full URLs: set only when no browser is using that profile folder.
        PreferencesWriter.TryEnsure(config.ProfileDir, diagnostics);

        if (!Start(config, url))
        {
            return null;
        }

        var clock = Stopwatch.StartNew();
        while (true)
        {
            await Task.Delay(WindowDetector.PollInterval);
            if (isCancelled())
            {
                return null;
            }

            DetectResult result = detector.Poll(WindowSystem.VisibleTopLevelInfos(), clock.Elapsed);
            switch (result.Status)
            {
                case DetectStatus.Found:
                    return result.WindowId;
                case DetectStatus.TimedOut:
                    return null;
            }
        }
    }

    /// <summary>Starts the browser process; does not keep, wait for or end it.</summary>
    private static bool Start(WallConfig config, string url)
    {
        var info = new ProcessStartInfo(config.BrowserPath)
        {
            UseShellExecute = false,
        };
        foreach (string arg in BrowserArguments.For(config.ProfileDir, url))
        {
            info.ArgumentList.Add(arg);
        }

        try
        {
            using Process? process = Process.Start(info);
            return process is not null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }
}
