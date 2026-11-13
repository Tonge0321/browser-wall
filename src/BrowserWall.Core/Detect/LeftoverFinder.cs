namespace BrowserWall.Core.Detect;

/// <summary>
/// Finds windows left over from the last time the wall ended abnormally.
/// </summary>
public static class LeftoverFinder
{
    /// <summary>
    /// Returns the handles in <paramref name="snapshotBefore"/> (top-level windows captured before the first URL launch) that are
    /// visible, of class Chrome_WidgetWin_1, owned by <paramref name="wallProcessId"/>, and not managed.
    /// The process match is required; same-class windows of other processes are never included.
    /// </summary>
    public static IReadOnlyList<long> Find(
        IEnumerable<WindowInfo> snapshotBefore,
        uint wallProcessId,
        IReadOnlyCollection<long> managedIds)
    {
        ArgumentNullException.ThrowIfNull(snapshotBefore);
        ArgumentNullException.ThrowIfNull(managedIds);

        var managed = new HashSet<long>(managedIds);
        return snapshotBefore
            .Where(w => w.Visible
                && w.ProcessId == wallProcessId
                && string.Equals(w.ClassName, WindowDetector.BrowserWindowClass, StringComparison.Ordinal)
                && !managed.Contains(w.Id))
            .Select(w => w.Id)
            .Distinct()
            .ToList();
    }
}
