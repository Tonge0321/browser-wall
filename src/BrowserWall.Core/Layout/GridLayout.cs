namespace BrowserWall.Core.Layout;

/// <summary>Layout result.</summary>
public sealed record GridResult(int Columns, int Rows, IReadOnlyList<CellRect> Cells)
{
    /// <summary>Index of the cell containing (x, y) (wall coordinates); -1 when in a gap or outside the cells.</summary>
    public int IndexAt(int x, int y)
    {
        for (int i = 0; i < Cells.Count; i++)
        {
            CellRect c = Cells[i];
            if (x >= c.X && x < c.Right && y >= c.Y && y < c.Bottom)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>Grid mode layout.</summary>
public static class GridLayout
{
    /// <summary>Gap between cells: 12 pixels.</summary>
    public const int DefaultGap = 12;

    /// <summary>
    /// Columns: 1 when N = 2 (two cells stacked top and bottom); ⌈√N⌉ for other N (integer arithmetic).
    /// Columns are defined only here; rows and placement are derived from it.
    /// </summary>
    public static int ColumnsFor(int count)
    {
        CheckCount(count);
        if (count == 2)
        {
            return 1;
        }

        int c = 0;
        while (c * c < count)
        {
            c++;
        }

        return c;
    }

    /// <summary>Rows = ⌈N ÷ columns⌉.</summary>
    public static int RowsFor(int count)
    {
        int columns = ColumnsFor(count);
        return columns == 0 ? 0 : (count + columns - 1) / columns;
    }

    /// <summary>
    /// Computes the positions of <paramref name="count"/> cells on the wall. Cells have the same size with <paramref name="gap"/>
    /// pixels between them, in cell order left to right, top to bottom; an incomplete last row is left-aligned. Leftover
    /// pixels from the division stay at the right and bottom.
    /// </summary>
    public static GridResult Compute(int count, int wallWidth, int wallHeight, int gap = DefaultGap)
    {
        CheckCount(count);
        if (gap < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gap), gap, "Gap must not be negative.");
        }

        int columns = ColumnsFor(count);
        int rows = RowsFor(count);
        if (count == 0)
        {
            return new GridResult(0, 0, Array.Empty<CellRect>());
        }

        int cellWidth = Math.Max(0, (Math.Max(0, wallWidth) - gap * (columns - 1)) / columns);
        int cellHeight = Math.Max(0, (Math.Max(0, wallHeight) - gap * (rows - 1)) / rows);

        var cells = new CellRect[count];
        for (int i = 0; i < count; i++)
        {
            int col = i % columns;
            int row = i / columns;
            cells[i] = new CellRect(col * (cellWidth + gap), row * (cellHeight + gap), cellWidth, cellHeight);
        }

        return new GridResult(columns, rows, cells);
    }

    private static void CheckCount(int count)
    {
        if (count < 0 || count > WallLimits.MaxCells)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, $"Cell count must be 0–{WallLimits.MaxCells}.");
        }
    }
}
