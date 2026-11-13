using BrowserWall.Core.Layout;

namespace BrowserWall.Tests;

public class GridLayoutTests
{
    private static readonly (int Width, int Height)[] WallSizes =
    {
        (1920, 1040), (1280, 720), (1001, 777), (640, 480), (3840, 2100),
    };

    [Fact(DisplayName = "Layout_CountsOneToSixteen_RowsAndColumns")]
    public void CountsOneToSixteen_RowsAndColumns()
    {
        // N → (columns, rows): columns = ⌈√N⌉, rows = ⌈N ÷ columns⌉ (limit 16); for N = 2, 1 column and 2 rows (stacked)
        var expected = new Dictionary<int, (int Columns, int Rows)>
        {
            [1] = (1, 1), [2] = (1, 2), [3] = (2, 2), [4] = (2, 2), [5] = (3, 2),
            [6] = (3, 2), [7] = (3, 3), [8] = (3, 3), [9] = (3, 3),
            [10] = (4, 3), [11] = (4, 3), [12] = (4, 3), [13] = (4, 4), [14] = (4, 4),
            [15] = (4, 4), [16] = (4, 4),
        };

        foreach ((int n, (int columns, int rows)) in expected)
        {
            GridResult result = GridLayout.Compute(n, 1920, 1040);
            Assert.Equal(columns, result.Columns);
            Assert.Equal(rows, result.Rows);
            Assert.Equal(n, result.Cells.Count);
        }

        // Gap between cells: 12 pixels.
        Assert.Equal(12, GridLayout.DefaultGap);
        // 2 cells are stacked, so 3 cells (2 columns, each (1212 − 12) ÷ 2 = 600 wide) are used to check the side-by-side gap.
        GridResult three = GridLayout.Compute(3, 1212, 600);
        Assert.Equal(612, three.Cells[1].X);
    }

    [Fact(DisplayName = "Layout_TwoWindowsStacked")]
    public void TwoWindowsStacked()
    {
        // 2 cells are stacked: 1 column, 2 rows, both as wide as the wall, 12 apart vertically.
        Assert.Equal(1, GridLayout.ColumnsFor(2));
        Assert.Equal(2, GridLayout.RowsFor(2));

        GridResult result = GridLayout.Compute(2, 1212, 812);
        Assert.Equal(1, result.Columns);
        Assert.Equal(2, result.Rows);
        Assert.Equal(2, result.Cells.Count);
        Assert.Equal(new CellRect(0, 0, 1212, 400), result.Cells[0]);
        Assert.Equal(new CellRect(0, 412, 1212, 400), result.Cells[1]);
        Assert.Equal(1212, result.Cells[0].Width);
        Assert.Equal(result.Cells[0].Width, result.Cells[1].Width);
        Assert.Equal(12, result.Cells[1].Y - result.Cells[0].Bottom);

        // The top half is cell 1, the bottom half cell 2, and the gap between belongs to no cell.
        Assert.Equal(0, result.IndexAt(600, 100));
        Assert.Equal(0, result.IndexAt(1211, 399));
        Assert.Equal(-1, result.IndexAt(600, 400));
        Assert.Equal(-1, result.IndexAt(600, 411));
        Assert.Equal(1, result.IndexAt(600, 412));
        Assert.Equal(1, result.IndexAt(0, 811));
    }

    [Fact(DisplayName = "Layout_OtherCountsUnchanged")]
    public void OtherCountsUnchanged()
    {
        // Only 2 cells are special; 1 and 3–16 cells follow the formula (columns = ⌈√N⌉, rows = ⌈N ÷ columns⌉).
        var expected = new Dictionary<int, (int Columns, int Rows)>
        {
            [1] = (1, 1), [3] = (2, 2), [4] = (2, 2), [5] = (3, 2), [6] = (3, 2),
            [7] = (3, 3), [8] = (3, 3), [9] = (3, 3), [10] = (4, 3), [11] = (4, 3),
            [12] = (4, 3), [13] = (4, 4), [14] = (4, 4), [15] = (4, 4), [16] = (4, 4),
        };

        foreach ((int n, (int columns, int rows)) in expected)
        {
            // Compute ⌈√N⌉ independently to check the expected table.
            int ceilSqrt = (int)Math.Ceiling(Math.Sqrt(n));
            Assert.Equal(ceilSqrt, columns);
            Assert.Equal((n + columns - 1) / columns, rows);

            Assert.Equal(columns, GridLayout.ColumnsFor(n));
            Assert.Equal(rows, GridLayout.RowsFor(n));
            GridResult result = GridLayout.Compute(n, 1920, 1040);
            Assert.Equal(columns, result.Columns);
            Assert.Equal(rows, result.Rows);
            Assert.Equal(n, result.Cells.Count);
        }

        // From 2 to 3 cells: from stacked back to 2 columns and 2 rows.
        Assert.Equal(1, GridLayout.Compute(2, 1920, 1040).Columns);
        GridResult three = GridLayout.Compute(3, 1920, 1040);
        Assert.Equal(2, three.Columns);
        Assert.Equal(2, three.Rows);
        Assert.Equal(three.Cells[0].Y, three.Cells[1].Y);
        Assert.True(three.Cells[1].X > three.Cells[0].Right);
    }

    [Fact(DisplayName = "Layout_CellsDoNotOverlap")]
    public void CellsDoNotOverlap()
    {
        foreach ((int width, int height) in WallSizes)
        {
            for (int n = 1; n <= 16; n++)
            {
                GridResult result = GridLayout.Compute(n, width, height);
                IReadOnlyList<CellRect> cells = result.Cells;

                for (int i = 0; i < cells.Count; i++)
                {
                    // Same size, inside the wall, non-empty
                    Assert.Equal(cells[0].Width, cells[i].Width);
                    Assert.Equal(cells[0].Height, cells[i].Height);
                    Assert.True(cells[i].Width > 0 && cells[i].Height > 0);
                    Assert.True(cells[i].X >= 0 && cells[i].Y >= 0);
                    Assert.True(cells[i].Right <= width && cells[i].Bottom <= height);

                    for (int j = i + 1; j < cells.Count; j++)
                    {
                        Assert.False(cells[i].Overlaps(cells[j]), $"N={n} wall {width}x{height}: cell {i} overlaps cell {j}");
                    }
                }

                // Adjacent cells are exactly 12 pixels apart
                for (int i = 0; i < cells.Count; i++)
                {
                    int col = i % result.Columns;
                    if (col + 1 < result.Columns && i + 1 < cells.Count)
                    {
                        Assert.Equal(12, cells[i + 1].X - cells[i].Right);
                    }

                    if (i + result.Columns < cells.Count)
                    {
                        Assert.Equal(12, cells[i + result.Columns].Y - cells[i].Bottom);
                    }
                }
            }
        }
    }

    [Fact(DisplayName = "Layout_OrderLeftToRightTopToBottom")]
    public void OrderLeftToRightTopToBottom()
    {
        // The default gap is 12; this test's expected coordinates use a gap of 4, passed explicitly.
        GridResult result = GridLayout.Compute(5, 1204, 604, gap: 4);

        // 3 columns, 2 rows, cell (1204-8)/3=398 × (604-4)/2=300
        Assert.Equal(new CellRect(0, 0, 398, 300), result.Cells[0]);
        Assert.Equal(new CellRect(402, 0, 398, 300), result.Cells[1]);
        Assert.Equal(new CellRect(804, 0, 398, 300), result.Cells[2]);
        Assert.Equal(new CellRect(0, 304, 398, 300), result.Cells[3]);
        Assert.Equal(new CellRect(402, 304, 398, 300), result.Cells[4]);
    }

    [Fact(DisplayName = "Layout_WallResized_Recomputed")]
    public void WallResized_Recomputed()
    {
        var state = new BrowserWall.Core.State.WallState();
        foreach (long id in new long[] { 11, 22, 33, 44 })
        {
            state.AddWindow(id, $"https://example.com/{id}");
        }

        // Grid mode: park all again (registration order), then relayout
        Assert.Equal(
            new[]
            {
                BrowserWall.Core.State.WallAction.Park(11), BrowserWall.Core.State.WallAction.Park(22),
                BrowserWall.Core.State.WallAction.Park(33), BrowserWall.Core.State.WallAction.Park(44),
                BrowserWall.Core.State.WallAction.Relayout,
            },
            state.WallResized());

        // Layout recomputed for the new wall size
        GridResult big = GridLayout.Compute(state.Slots.Count, 1920, 1040);
        GridResult small = GridLayout.Compute(state.Slots.Count, 960, 520);
        Assert.NotEqual(big.Cells[0].Width, small.Cells[0].Width);
        Assert.NotEqual(big.Cells[0].Height, small.Cells[0].Height);
        for (int i = 0; i < small.Cells.Count; i++)
        {
            Assert.True(small.Cells[i].Right <= 960 && small.Cells[i].Bottom <= 520);
            for (int j = i + 1; j < small.Cells.Count; j++)
            {
                Assert.False(small.Cells[i].Overlaps(small.Cells[j]));
            }
        }

        // Expanded mode: other windows parked, the expanded one expanded again, the button moved, relayout
        state.ClickCell(1);
        Assert.Equal(
            new[]
            {
                BrowserWall.Core.State.WallAction.Park(11), BrowserWall.Core.State.WallAction.Park(33),
                BrowserWall.Core.State.WallAction.Park(44), BrowserWall.Core.State.WallAction.Expand(22),
                BrowserWall.Core.State.WallAction.ShowShrinkButton, BrowserWall.Core.State.WallAction.Relayout,
            },
            state.WallResized());
        Assert.Equal(BrowserWall.Core.State.WallMode.Expanded, state.Mode);
        Assert.Equal(22, state.ExpandedWindowId);

        // No windows
        Assert.Equal(new[] { BrowserWall.Core.State.WallAction.ShowEmpty }, new BrowserWall.Core.State.WallState().WallResized());
    }

    [Fact(DisplayName = "Layout_IndexAt_HitsCellsNotGaps")]
    public void IndexAt_HitsCellsNotGaps()
    {
        // 3 columns, 2 rows, cell 398×300, gap 4 (passed explicitly; the default is 12)
        GridResult result = GridLayout.Compute(5, 1204, 604, gap: 4);

        Assert.Equal(0, result.IndexAt(0, 0));
        Assert.Equal(0, result.IndexAt(397, 299));
        Assert.Equal(-1, result.IndexAt(398, 10));   // Gap
        Assert.Equal(-1, result.IndexAt(10, 301));   // Gap
        Assert.Equal(1, result.IndexAt(402, 0));
        Assert.Equal(4, result.IndexAt(500, 400));
        Assert.Equal(-1, result.IndexAt(900, 400));  // No cell at row 2, column 3
        Assert.Equal(-1, result.IndexAt(-1, 5));
        Assert.Equal(-1, result.IndexAt(5, 700));
    }

    [Fact(DisplayName = "Layout_ZeroCount_Empty")]
    public void ZeroCount_Empty()
    {
        GridResult result = GridLayout.Compute(0, 1920, 1040);

        Assert.Equal(0, result.Columns);
        Assert.Equal(0, result.Rows);
        Assert.Empty(result.Cells);
    }

    [Fact(DisplayName = "Layout_CountOutOfRange_Throws")]
    public void CountOutOfRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GridLayout.Compute(-1, 100, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => GridLayout.Compute(17, 100, 100));
    }

    [Fact(DisplayName = "Layout_TinyWall_ZeroSizeNoThrow")]
    public void TinyWall_ZeroSizeNoThrow()
    {
        GridResult result = GridLayout.Compute(9, 5, -3);

        Assert.Equal(9, result.Cells.Count);
        Assert.All(result.Cells, c => Assert.True(c.Width == 0 && c.Height == 0));
    }
}
