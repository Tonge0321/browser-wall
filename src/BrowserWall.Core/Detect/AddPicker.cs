using BrowserWall.Core.State;

namespace BrowserWall.Core.Detect;

/// <summary>State of the "Add open window (N)" button. <see cref="Count"/> is N; the App formats the text from the string table.</summary>
public sealed record ExistingButtonState(bool Visible, bool Enabled, int Count);

/// <summary>
/// List filtering for "Add open window", and whether the "+ Add page" and "Add open window (N)" buttons are usable.
/// The process id must equal the wall's browser process; the class name alone is not enough to be listed.
/// </summary>
public static class AddPicker
{
    /// <summary>
    /// In the order of <paramref name="current"/>, returns the windows that can be added to the wall: visible, class
    /// Chrome_WidgetWin_1, has a title, owned by <paramref name="wallProcessId"/>, and not yet managed.
    /// Returns an empty list when <paramref name="wallProcessId"/> is null (every URL failed to open).
    /// </summary>
    public static IReadOnlyList<WindowInfo> Candidates(
        IEnumerable<WindowInfo> current,
        uint? wallProcessId,
        IReadOnlyCollection<long> managedIds)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(managedIds);

        if (wallProcessId is not uint wallPid)
        {
            return Array.Empty<WindowInfo>();
        }

        var managed = new HashSet<long>(managedIds);
        return current
            .Where(w => w.Visible
                && w.ProcessId == wallPid
                && string.Equals(w.ClassName, WindowDetector.BrowserWindowClass, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(w.Title)
                && !managed.Contains(w.Id))
            .ToList();
    }

    /// <summary>
    /// Rechecks when a row is picked in the list: whether that window can still be added.
    /// false when, while the list was open, the window closed (not in <paramref name="current"/>), became managed, or no
    /// longer belongs to the wall's process.
    /// </summary>
    public static bool IsStillCandidate(
        long windowId,
        IEnumerable<WindowInfo> current,
        uint? wallProcessId,
        IReadOnlyCollection<long> managedIds) =>
        Candidates(current, wallProcessId, managedIds).Any(w => w.Id == windowId);

    /// <summary>
    /// The "Add open window (N)" button: shown only in grid mode or with no cells (not expanded, wall not minimized, not
    /// closing) and <paramref name="candidateCount"/> &gt; 0; clickable when shown, not launching or adding, and a cell is free.
    /// Also shown with no cells, so the last cell can be added back after it was popped out.
    /// </summary>
    public static ExistingButtonState ExistingButton(WallState state, int candidateCount, bool launching)
    {
        ArgumentNullException.ThrowIfNull(state);
        int n = Math.Max(0, candidateCount);
        bool visible = n > 0
            && !state.IsExiting
            && !state.IsWallMinimized
            && state.Mode != WallMode.Expanded;
        bool enabled = visible && !launching && state.HasFreeSlot;
        return new ExistingButtonState(visible, enabled, n);
    }

    /// <summary>
    /// Whether the "+ Add page" button is usable: wall not minimized, not in expanded mode, and managed windows below the cell limit.
    /// </summary>
    public static bool CanAdd(WallState state) => CanAdd(state, launching: false);

    /// <summary>
    /// Same as above, and also disabled while startup URLs are launching or "+ Add page" is still looking for its window
    /// (<paramref name="launching"/>): URLs not yet registered will still take cells (found or failed), so added windows
    /// must not fill the cells first.
    /// </summary>
    public static bool CanAdd(WallState state, bool launching)
    {
        ArgumentNullException.ThrowIfNull(state);
        return !launching
            && !state.IsWallMinimized
            && state.Mode != WallMode.Expanded
            && state.WindowIds.Count < WallLimits.MaxCells;
    }
}
