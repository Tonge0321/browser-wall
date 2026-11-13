namespace BrowserWall.Core.Layout;

/// <summary>
/// Display rule for the title bar ✕ and "Pop out" buttons: shown only on the cell under the mouse;
/// no cell shows them while a drag-to-swap is in progress.
/// </summary>
public static class TitleBarButtons
{
    /// <summary>
    /// The only cell that shows the buttons (0-based); -1 when no cell shows them. In order:
    /// dragging → -1; not grid mode → -1; mouse not on any cell → -1; otherwise → the cell under the mouse.
    /// </summary>
    /// <param name="hoveredCell">Cell under the mouse (0-based, whole cell including the title bar); -1 when not on any cell.</param>
    /// <param name="cellCount">Number of cells.</param>
    /// <param name="gridMode">The wall is in grid mode (not expanded, minimized or without windows).</param>
    /// <param name="dragging">A drag-to-swap is in progress (moved past the threshold after pressing).</param>
    public static int VisibleCell(int hoveredCell, int cellCount, bool gridMode, bool dragging)
    {
        if (dragging || !gridMode || hoveredCell < 0 || hoveredCell >= cellCount)
        {
            return -1;
        }

        return hoveredCell;
    }

    /// <summary>Whether cell <paramref name="cell"/> shows the buttons.</summary>
    public static bool IsVisible(int cell, int hoveredCell, int cellCount, bool gridMode, bool dragging) =>
        cell >= 0 && VisibleCell(hoveredCell, cellCount, gridMode, dragging) == cell;

    /// <summary>
    /// How long the mouse must rest on "Pop out" or ✕ before the tooltip appears (about 0.5 seconds). Defined only here; the UI uses it directly.
    /// </summary>
    public static readonly TimeSpan TooltipDelay = TimeSpan.FromMilliseconds(500);
}
