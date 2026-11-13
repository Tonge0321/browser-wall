using BrowserWall.Core.Layout;

namespace BrowserWall.Tests;

public class CellDragGestureTests
{
    [Fact(DisplayName = "Drag_BelowThresholdIsClick")]
    public void Drag_BelowThresholdIsClick()
    {
        Assert.Equal(10, CellDragGesture.Threshold);

        // Released without passing 10 pixels (exactly 10 included): a click.
        foreach ((double dx, double dy) in new[] { (0.0, 0.0), (10.0, 0.0), (6.0, 8.0), (0.0, -10.0) })
        {
            var g = new CellDragGesture();
            g.Press(2, 100, 100);
            Assert.False(g.Move(100 + dx, 100 + dy));
            Assert.Equal(new CellDragResult(CellDragResultKind.Click, 2, -1), g.Release(5));
            Assert.False(g.IsPressed);
        }

        // Past 10 pixels: a drag; dropped on another cell, they swap.
        foreach ((double dx, double dy) in new[] { (11.0, 0.0), (8.0, 8.0), (-7.5, -7.5) })
        {
            var g = new CellDragGesture();
            g.Press(2, 100, 100);
            Assert.True(g.Move(100 + dx, 100 + dy));
            Assert.Equal(new CellDragResult(CellDragResultKind.Swap, 2, 5), g.Release(5));
        }

        // Once a drag, releasing back at the start point is not a click; dropping on the same cell or outside the cells changes nothing.
        var back = new CellDragGesture();
        back.Press(1, 0, 0);
        Assert.True(back.Move(20, 0));
        Assert.True(back.Move(0, 0));
        Assert.Equal(CellDragResult.None, back.Release(1));

        var outside = new CellDragGesture();
        outside.Press(1, 0, 0);
        outside.Move(50, 50);
        Assert.Equal(CellDragResult.None, outside.Release(-1));

        // Released without a press, or after cancel: no change.
        var idle = new CellDragGesture();
        Assert.Equal(CellDragResult.None, idle.Release(0));
        idle.Press(0, 0, 0);
        idle.Cancel();
        Assert.Equal(CellDragResult.None, idle.Release(0));

        Assert.False(CellDragGesture.ExceedsThreshold(6, 8));
        Assert.True(CellDragGesture.ExceedsThreshold(6, 8.01));
    }
}
