using System.Globalization;
using BrowserWall.Core.Launch;

namespace BrowserWall.Core.Health;

/// <summary>Kind of background work, for the diagnostics log.</summary>
public enum BackgroundWorkKind
{
    Taskbar,
    AddressBar,
    FlatCheck,
}

/// <summary>Result of the recheck after a nudge.</summary>
public enum NudgeOutcome
{
    Recovered,
    NotRecovered,

    /// <summary>Not rechecked: at recheck time the wall was minimized or closing, the window had left the wall, or the capture was invalid.</summary>
    NotChecked,
}

/// <summary>
/// Every line of the diagnostics log. Parameters are only time, cell position, color, result, milliseconds and taskbar action;
/// there are deliberately no title or URL parameters. Fields are TAB-separated; time is UTC; cell position is 1-based, "-" when not on a cell.
/// Lines are always English, regardless of the UI language.
/// </summary>
public static class DiagnosticsFormat
{
    /// <summary>File size limit: 1 MB.</summary>
    public const long MaxBytes = 1024 * 1024;

    public static string Flat(DateTime utc, int? cell, Rgb color) =>
        Line(utc, cell, "flat", Hex(color));

    public static string Nudge(DateTime utc, int? cell, Rgb color) =>
        Line(utc, cell, "nudge", Hex(color));

    public static string NudgeResult(DateTime utc, int? cell, Rgb color, NudgeOutcome outcome) =>
        Line(utc, cell, "nudge result", Hex(color), outcome switch
        {
            NudgeOutcome.Recovered => "recovered",
            NudgeOutcome.NotRecovered => "not recovered",
            _ => "not checked",
        });

    public static string SlowRound(DateTime utc, TimeSpan elapsed) =>
        Line(utc, null, "check round over 1 s", ((long)elapsed.TotalMilliseconds).ToString(CultureInfo.InvariantCulture) + " ms");

    public static string CaptureTimeout(DateTime utc, int? cell) =>
        Line(utc, cell, "capture timed out");

    /// <param name="show">true = show the taskbar button again, false = hide it.</param>
    public static string TaskbarTimeout(DateTime utc, int? cell, bool show) =>
        Line(utc, cell, show ? "taskbar show timed out, skipped" : "taskbar hide timed out, skipped");

    /// <summary>Background work reached the stuck cap (<paramref name="reached"/> is true) or resumed. Only time, kind and fixed text.</summary>
    public static string StuckCap(DateTime utc, BackgroundWorkKind kind, bool reached) =>
        Line(utc, null, "background " + KindText(kind) + (reached ? " stuck cap reached, paused" : " resumed"));

    /// <summary>Writing the browser preferences failed. Only time and fixed result text; no file content or path.</summary>
    public static string PreferencesFailure(DateTime utc, PreferencesWriteResult result) =>
        Line(utc, null, "preferences " + result switch
        {
            PreferencesWriteResult.ReadFailed => "read failed, not written",
            PreferencesWriteResult.BackupFailed => "backup failed, not written",
            PreferencesWriteResult.TempFailed => "temp file failed, not written",
            PreferencesWriteResult.ReplaceFailedOriginalKept => "replace failed, original kept",
            PreferencesWriteResult.ReplaceFailedRestoreFailed => "replace failed, restore failed",
            PreferencesWriteResult.Written => "written",
            PreferencesWriteResult.NotNeeded => "not needed",
            _ => "unexpected error",
        });

    /// <summary>Deleting the temp file after writing the browser preferences failed. Only time and fixed text; no path or content.</summary>
    public static string PreferencesTempDeleteFailed(DateTime utc) =>
        Line(utc, null, "preferences temp file delete failed");

    /// <summary>
    /// Writing the last page list failed for the first time, or succeeded for the first time after failures. Only time and fixed text; no URLs.
    /// Must not be called with <paramref name="change"/> = <see cref="EpisodeChange.None"/>.
    /// </summary>
    public static string SessionWrite(DateTime utc, EpisodeChange change) =>
        Line(utc, null, change == EpisodeChange.Recovered ? "session write recovered" : "session write failed");

    private static string KindText(BackgroundWorkKind kind) => kind switch
    {
        BackgroundWorkKind.Taskbar => "taskbar",
        BackgroundWorkKind.AddressBar => "address bar",
        _ => "flat check",
    };

    /// <summary>Whether to rotate before writing: current size plus this line exceeds 1 MB.</summary>
    public static bool ShouldRotate(long currentSize, int addBytes) => currentSize + addBytes > MaxBytes;

    private static string Hex(Rgb c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string Line(DateTime utc, int? cell, params string[] fields)
    {
        string time = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        string where = cell is int n ? "cell " + n.ToString(CultureInfo.InvariantCulture) : "-";
        return string.Join('\t', new[] { time, where }.Concat(fields));
    }
}
