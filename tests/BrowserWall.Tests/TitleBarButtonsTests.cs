using BrowserWall.Core.Layout;

namespace BrowserWall.Tests;

/// <summary>The title bar ✕ and "Pop out" show only on the cell under the mouse; none show during a drag-to-swap.</summary>
public class TitleBarButtonsTests
{
    private const int Cells = 16;

    [Fact(DisplayName = "TitleBar_ButtonsOnlyOnHoveredCell")]
    public void ButtonsOnlyOnHoveredCell()
    {
        for (int k = 0; k < Cells; k++)
        {
            Assert.Equal(k, TitleBarButtons.VisibleCell(k, Cells, gridMode: true, dragging: false));
            for (int cell = 0; cell < Cells; cell++)
            {
                Assert.Equal(cell == k, TitleBarButtons.IsVisible(cell, k, Cells, gridMode: true, dragging: false));
            }
        }

        // Mouse not on any cell (gap, toolbar, outside the wall): none shown.
        foreach (int outside in new[] { -1, Cells, Cells + 3 })
        {
            Assert.Equal(-1, TitleBarButtons.VisibleCell(outside, Cells, gridMode: true, dragging: false));
            for (int cell = 0; cell < Cells; cell++)
            {
                Assert.False(TitleBarButtons.IsVisible(cell, outside, Cells, gridMode: true, dragging: false));
            }
        }

        // Not grid mode (expanded, wall minimized): none shown.
        for (int k = 0; k < Cells; k++)
        {
            Assert.Equal(-1, TitleBarButtons.VisibleCell(k, Cells, gridMode: false, dragging: false));
            Assert.False(TitleBarButtons.IsVisible(k, k, Cells, gridMode: false, dragging: false));
        }

        // No cells: none shown; a negative cell index never shows.
        Assert.Equal(-1, TitleBarButtons.VisibleCell(0, 0, gridMode: true, dragging: false));
        Assert.False(TitleBarButtons.IsVisible(-1, -1, Cells, gridMode: true, dragging: false));

        // No text labels: the title bar is text area, "Pop out", ✕ from left to right; the text area's right edge equals "Pop out"'s left edge.
        var title = new CellRect(100, 50, 400, 30);
        CellTitleButtons b = CellContent.TitleButtons(title, 46);
        Assert.Equal(title.X, b.Text.X);
        Assert.Equal(b.Release.X, b.Text.Right);
        Assert.Equal(b.Close.X, b.Release.Right);
        Assert.Equal(title.Right, b.Close.Right);
        Assert.Equal(46, b.Release.Width);
        Assert.Equal(46, b.Close.Width);
        Assert.Equal(title.Width - 2 * 46, b.Text.Width);

        // The tooltip appears after about 0.5 seconds.
        Assert.Equal(TimeSpan.FromMilliseconds(500), TitleBarButtons.TooltipDelay);
    }

    [Fact(DisplayName = "TitleBar_NoButtonsWhileDragging")]
    public void NoButtonsWhileDragging()
    {
        for (int k = -1; k <= Cells; k++)
        {
            Assert.Equal(-1, TitleBarButtons.VisibleCell(k, Cells, gridMode: true, dragging: true));
            for (int cell = 0; cell < Cells; cell++)
            {
                Assert.False(TitleBarButtons.IsVisible(cell, k, Cells, gridMode: true, dragging: true));
            }
        }

        // With the drag gesture: still shown while pressed below the threshold, hidden after passing it, back after release.
        var drag = new CellDragGesture();
        drag.Press(3, 100, 100);
        drag.Move(105, 105);
        Assert.False(drag.IsDragging);
        Assert.Equal(3, TitleBarButtons.VisibleCell(3, Cells, true, drag.IsDragging));

        drag.Move(100, 111);
        Assert.True(drag.IsDragging);
        Assert.Equal(-1, TitleBarButtons.VisibleCell(3, Cells, true, drag.IsDragging));
        Assert.Equal(-1, TitleBarButtons.VisibleCell(5, Cells, true, drag.IsDragging));

        drag.Release(5);
        Assert.False(drag.IsDragging);
        Assert.Equal(5, TitleBarButtons.VisibleCell(5, Cells, true, drag.IsDragging));
    }
}
