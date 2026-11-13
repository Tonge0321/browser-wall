using BrowserWall.Core.Layout;

namespace BrowserWall.Tests;

public class CellContentTests
{
    /// <summary>The title bar is at the top of the cell.</summary>
    [Fact(DisplayName = "Cell_Split_TitleAbove")]
    public void Split_TitleAbove()
    {
        CellParts parts = CellContent.Split(new CellRect(10, 20, 300, 200), 24);

        Assert.Equal(new CellRect(10, 20, 300, 24), parts.Title);
        Assert.Equal(new CellRect(10, 44, 300, 176), parts.Thumbnail);
    }

    /// <summary>The right side holds "Pop out" then ✕; ✕ is rightmost.</summary>
    [Fact(DisplayName = "Cell_TitleButtons_ReleaseThenCloseOnRight")]
    public void TitleButtons_ReleaseThenCloseOnRight()
    {
        var title = new CellRect(10, 20, 300, 24);
        CellTitleButtons b = CellContent.TitleButtons(title, 50);
        Assert.Equal(new CellRect(260, 20, 50, 24), b.Close);
        Assert.Equal(new CellRect(210, 20, 50, 24), b.Release);
        Assert.Equal(new CellRect(10, 20, 200, 24), b.Text);

        // When narrow, both buttons always keep their place and the text area shrinks first.
        CellTitleButtons narrow = CellContent.TitleButtons(new CellRect(0, 0, 100, 24), 50);
        Assert.Equal(new CellRect(0, 0, 0, 24), narrow.Text);
        Assert.Equal(new CellRect(0, 0, 50, 24), narrow.Release);
        Assert.Equal(new CellRect(50, 0, 50, 24), narrow.Close);
        Assert.Equal(1, CellContent.TitleButtons(new CellRect(0, 0, 101, 24), 50).Text.Width);

        CellTitleButtons tiny = CellContent.TitleButtons(new CellRect(0, 0, 60, 24), 44);
        Assert.Equal(30, tiny.Close.Width);
        Assert.Equal(30, tiny.Release.Width);
        Assert.Equal(0, tiny.Text.Width);
        CellTitleButtons hundred = CellContent.TitleButtons(new CellRect(0, 0, 100, 24), 44);
        Assert.Equal(44, hundred.Close.Width);
        Assert.Equal(44, hundred.Release.Width);
        Assert.Equal(12, hundred.Text.Width);
        Assert.Equal(CellHit.ReleaseButton, CellContent.HitTest(new CellRect(0, 0, 60, 100), 24, 44, 10, 5));
        Assert.Equal(CellHit.CloseButton, CellContent.HitTest(new CellRect(0, 0, 60, 100), 24, 44, 50, 5));

        // Height 0: no buttons.
        Assert.Equal(0, CellContent.TitleButtons(new CellRect(0, 0, 100, 0), 44).Close.Width);
    }

    /// <summary>The title text width always excludes both buttons, so it does not jump when the buttons appear or hide.</summary>
    [Fact(DisplayName = "Cell_TitleButtons_TextWidthFixed")]
    public void TitleButtons_TextWidthFixed()
    {
        foreach (int width in new[] { 120, 300, 641, 1000 })
        {
            var title = new CellRect(5, 7, width, 30);
            CellTitleButtons b = CellContent.TitleButtons(title, 46);

            // Positions depend only on the title bar and button width (there is no "shown" parameter), so the text area is the same whether shown or hidden.
            Assert.Equal(width - (2 * 46), b.Text.Width);
            Assert.Equal(title.X, b.Text.X);
            Assert.Equal(b.Release.X, b.Text.Right);
            Assert.Equal(b, CellContent.TitleButtons(title, 46));
        }
    }

    [Fact(DisplayName = "Layout_TitleBarOnTop")]
    public void Layout_TitleBarOnTop()
    {
        // Wall 1212×1624, 3 cells (2 columns, 2 rows, the first two side by side), gap 12.
        // 2 cells are stacked, so 3 cells are used to check the gap and width of two side-by-side cells; each cell is (1624 − 12) ÷ 2 = 806 high.
        GridResult grid = GridLayout.Compute(3, 1212, 1624);
        Assert.Equal(new CellRect(0, 0, 600, 806), grid.Cells[0]);
        Assert.Equal(new CellRect(612, 0, 600, 806), grid.Cells[1]);
        Assert.Equal(new CellRect(0, 818, 600, 806), grid.Cells[2]);
        Assert.Equal(12, grid.Cells[1].X - grid.Cells[0].Right);

        foreach (CellRect cell in grid.Cells)
        {
            // Title bar on top, 24 high; the thumbnail follows below to the bottom of the cell.
            CellParts parts = CellContent.Split(cell, 24);
            Assert.Equal(cell.Y, parts.Title.Y);
            Assert.Equal(24, parts.Title.Height);
            Assert.Equal(parts.Title.Bottom, parts.Thumbnail.Y);
            Assert.Equal(cell.Bottom, parts.Thumbnail.Bottom);
            Assert.Equal(cell.Width, parts.Thumbnail.Width);

            // Right side holds "Pop out" then ✕: ✕ rightmost, "Pop out" right next to it, text area leftmost.
            CellTitleButtons b = CellContent.TitleButtons(parts.Title, 44);
            Assert.Equal(cell.Right, b.Close.Right);
            Assert.Equal(b.Close.X, b.Release.Right);
            Assert.Equal(cell.X, b.Text.X);
            Assert.Equal(b.Release.X, b.Text.Right);

            // On a button it is that button; the title text area and thumbnail area can be dragged.
            Assert.Equal(CellHit.CloseButton, CellContent.HitTest(cell, 24, 44, b.Close.X + 1, cell.Y + 1));
            Assert.Equal(CellHit.ReleaseButton, CellContent.HitTest(cell, 24, 44, b.Release.X + 1, cell.Y + 1));
            Assert.Equal(CellHit.Body, CellContent.HitTest(cell, 24, 44, cell.X + 5, cell.Y + 5));
            Assert.Equal(CellHit.Body, CellContent.HitTest(cell, 24, 44, cell.X + 300, cell.Y + 400));
        }
    }

    [Fact(DisplayName = "Cell_HitTest_ButtonsNotDrag")]
    public void HitTest_ButtonsNotDrag()
    {
        var cell = new CellRect(10, 20, 300, 200);

        Assert.Equal(CellHit.CloseButton, CellContent.HitTest(cell, 24, 50, 300, 30));
        Assert.Equal(CellHit.ReleaseButton, CellContent.HitTest(cell, 24, 50, 220, 30));
        Assert.Equal(CellHit.Body, CellContent.HitTest(cell, 24, 50, 20, 30));    // Title text area
        Assert.Equal(CellHit.Body, CellContent.HitTest(cell, 24, 50, 300, 100));  // Thumbnail area
        Assert.Equal(CellHit.None, CellContent.HitTest(cell, 24, 50, 9, 30));
        Assert.Equal(CellHit.None, CellContent.HitTest(cell, 24, 50, 100, 220));
    }

    [Fact(DisplayName = "Cell_Split_TooShort_NoThumbnail")]
    public void Split_TooShort_NoThumbnail()
    {
        CellParts parts = CellContent.Split(new CellRect(0, 0, 100, 10), 24);

        Assert.Equal(0, parts.Thumbnail.Height);
        Assert.Equal(new CellRect(0, 0, 100, 10), parts.Title);
    }

    [Fact(DisplayName = "Cell_FitCentered_WideSource")]
    public void FitCentered_WideSource()
    {
        // 1920×1080 into 400×400: width fills, height 225, centered vertically
        CellRect r = CellContent.FitCentered(1920, 1080, new CellRect(100, 50, 400, 400));

        Assert.Equal(new CellRect(100, 137, 400, 225), r);
    }

    [Fact(DisplayName = "Cell_FitCentered_TallSource")]
    public void FitCentered_TallSource()
    {
        // 600×1200 into 400×300: height fills, width 150, centered horizontally
        CellRect r = CellContent.FitCentered(600, 1200, new CellRect(0, 0, 400, 300));

        Assert.Equal(new CellRect(125, 0, 150, 300), r);
    }

    [Fact(DisplayName = "Cell_FitCentered_SameRatio_Fills")]
    public void FitCentered_SameRatio_Fills()
    {
        CellRect area = new(7, 9, 320, 180);

        Assert.Equal(area, CellContent.FitCentered(1920, 1080, area));
    }

    [Fact(DisplayName = "Cell_FitCentered_ZeroSource_Empty")]
    public void FitCentered_ZeroSource_Empty()
    {
        CellRect r = CellContent.FitCentered(0, 1080, new CellRect(0, 0, 400, 300));

        Assert.Equal(0, r.Width);
        Assert.Equal(0, r.Height);
    }

    [Fact(DisplayName = "Cell_FitCentered_InsideAreaKeepsRatio")]
    public void FitCentered_InsideAreaKeepsRatio()
    {
        var sources = new[] { (1920, 1080), (1080, 1920), (1366, 768), (800, 600), (1, 1000), (1000, 1) };
        var areas = new[] { new CellRect(3, 5, 631, 349), new CellRect(0, 0, 97, 411), new CellRect(50, 60, 1000, 1000) };

        foreach ((int sw, int sh) in sources)
        {
            foreach (CellRect area in areas)
            {
                CellRect r = CellContent.FitCentered(sw, sh, area);

                Assert.True(r.X >= area.X && r.Y >= area.Y && r.Right <= area.Right && r.Bottom <= area.Bottom);
                Assert.True(r.Width == area.Width || r.Height == area.Height);

                // Aspect ratio within 1 pixel: derive the height from the scaled width
                double expectedHeight = (double)r.Width * sh / sw;
                double expectedWidth = (double)r.Height * sw / sh;
                Assert.True(Math.Abs(r.Height - expectedHeight) <= 1 || Math.Abs(r.Width - expectedWidth) <= 1);
            }
        }
    }
}
