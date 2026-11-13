using BrowserWall.Core.State;

namespace BrowserWall.Tests;

public class ExitTests
{
    private static WallState WithWindowsAndFailed()
    {
        var state = new WallState();
        state.AddWindow(11, "https://example.com/11");
        state.AddFailed("https://failed.example.com");
        state.AddWindow(22, "https://example.com/22");
        state.AddWindow(33, "https://example.com/33");
        return state;
    }

    [Fact(DisplayName = "Exit_ClosesAllManaged")]
    public void ClosesAllManaged()
    {
        // Grid mode: one close request per managed window in registration order, excluding failed cells
        WallState grid = WithWindowsAndFailed();
        Assert.Equal(
            new[] { WallAction.CloseWindow(11), WallAction.CloseWindow(22), WallAction.CloseWindow(33) },
            grid.BeginExit());
        Assert.True(grid.IsExiting);
        Assert.Empty(grid.BeginExit()); // No action the second time

        // While exiting, other operations do nothing and the state does not change
        Assert.Empty(grid.ClickCell(0));
        Assert.Empty(grid.Shrink());
        Assert.Empty(grid.AdoptWindow(44));
        Assert.Empty(grid.ReleaseWindow(22));
        Assert.Empty(grid.AddWindow(55, "https://example.com/55"));
        Assert.Empty(grid.AddFailed("https://late.example.com"));
        Assert.Empty(grid.SyncAlive(new long[] { 11 }));
        Assert.Empty(grid.WindowClosed(22));
        Assert.Empty(grid.WindowMinimized(11));
        Assert.Empty(grid.WallMinimized());
        Assert.Empty(grid.WallResized());
        Assert.Equal(WallMode.Grid, grid.Mode);
        Assert.Equal(new long[] { 11, 22, 33 }, grid.WindowIds);
        Assert.Equal(4, grid.Slots.Count);

        // Expanded
        WallState expanded = WithWindowsAndFailed();
        expanded.ClickCell(2);
        Assert.Equal(
            new[] { WallAction.CloseWindow(11), WallAction.CloseWindow(22), WallAction.CloseWindow(33) },
            expanded.BeginExit());

        // Wall minimized
        WallState minimized = WithWindowsAndFailed();
        minimized.WallMinimized();
        Assert.Equal(
            new[] { WallAction.CloseWindow(11), WallAction.CloseWindow(22), WallAction.CloseWindow(33) },
            minimized.BeginExit());
        Assert.Empty(minimized.WallRestored());

        // No managed windows
        Assert.Empty(new WallState().BeginExit());
    }

    [Fact(DisplayName = "Exit_SurvivorsReleasedVisible")]
    public void SurvivorsReleasedVisible()
    {
        // Exit not started: no action
        WallState notExiting = WithWindowsAndFailed();
        Assert.Empty(notExiting.FinishExit(new long[] { 11, 22, 33 }));

        // Only 22 is still there (for example, the page asked "Leave site?")
        WallState one = WithWindowsAndFailed();
        one.BeginExit();
        Assert.Equal(new[] { WallAction.ReleaseOnExit(22) }, one.FinishExit(new long[] { 22, 999 }));
        Assert.Empty(one.FinishExit(new long[] { 22 })); // No action the second time

        // All still there: in registration order
        WallState all = WithWindowsAndFailed();
        all.BeginExit();
        Assert.Equal(
            new[] { WallAction.ReleaseOnExit(11), WallAction.ReleaseOnExit(22), WallAction.ReleaseOnExit(33) },
            all.FinishExit(new long[] { 33, 22, 11 }));

        // All closed
        WallState none = WithWindowsAndFailed();
        none.BeginExit();
        Assert.Empty(none.FinishExit(Array.Empty<long>()));
    }
}
