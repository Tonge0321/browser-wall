namespace BrowserWall.Core.Layout;

/// <summary>Kind of result when the mouse is released.</summary>
public enum CellDragResultKind
{
    /// <summary>No change (not pressed, dropped on the same cell, outside the cells or outside the wall).</summary>
    None,

    /// <summary>Did not pass the threshold: treated as a click, which expands the cell.</summary>
    Click,

    /// <summary>Dragged and dropped on another cell: the two cells swap.</summary>
    Swap,
}

/// <summary>Result of releasing the mouse; <see cref="From"/> and <see cref="To"/> are -1 when not applicable.</summary>
public sealed record CellDragResult(CellDragResultKind Kind, int From, int To)
{
    public static CellDragResult None { get; } = new(CellDragResultKind.None, -1, -1);
}

/// <summary>
/// "Press → move → release" decision for a cell. Coordinates are logical pixels. Does not touch the UI.
/// Dragging is not allowed in expanded mode or while the wall is minimized: <see cref="State.WallState.SwapCells"/> refuses it,
/// and the App does not start the gesture in those cases either.
/// </summary>
public sealed class CellDragGesture
{
    /// <summary>Drag threshold (logical pixels): it is a drag only when the move distance exceeds it.</summary>
    public const double Threshold = 10;

    private double _startX;
    private double _startY;

    public bool IsPressed { get; private set; }

    public bool IsDragging { get; private set; }

    /// <summary>The pressed cell; -1 when not pressed.</summary>
    public int SourceIndex { get; private set; } = -1;

    /// <summary>Whether the straight-line distance from the press point to the current point is strictly greater than <see cref="Threshold"/> (exactly 10 is still a click).</summary>
    public static bool ExceedsThreshold(double dx, double dy) => (dx * dx) + (dy * dy) > Threshold * Threshold;

    /// <summary>Left button pressed on cell <paramref name="index"/>.</summary>
    public void Press(int index, double x, double y)
    {
        IsPressed = true;
        IsDragging = false;
        SourceIndex = index;
        _startX = x;
        _startY = y;
    }

    /// <summary>Mouse moved; returns whether dragging. Once past the threshold, it stays a drag.</summary>
    public bool Move(double x, double y)
    {
        if (IsPressed && !IsDragging && ExceedsThreshold(x - _startX, y - _startY))
        {
            IsDragging = true;
        }

        return IsDragging;
    }

    /// <summary>
    /// Left button released. <paramref name="dropIndex"/> is the cell at the release point, -1 outside the cells or the wall.
    /// Not dragging → Click; dragging and dropped on another cell → Swap; otherwise → None. Then returns to not pressed.
    /// </summary>
    public CellDragResult Release(int dropIndex)
    {
        if (!IsPressed)
        {
            return CellDragResult.None;
        }

        int source = SourceIndex;
        bool dragging = IsDragging;
        Cancel();

        if (!dragging)
        {
            return source >= 0 ? new CellDragResult(CellDragResultKind.Click, source, -1) : CellDragResult.None;
        }

        return dropIndex >= 0 && source >= 0 && dropIndex != source
            ? new CellDragResult(CellDragResultKind.Swap, source, dropIndex)
            : CellDragResult.None;
    }

    /// <summary>Cancels the gesture (for example, mouse capture lost or mode changed).</summary>
    public void Cancel()
    {
        IsPressed = false;
        IsDragging = false;
        SourceIndex = -1;
    }
}
