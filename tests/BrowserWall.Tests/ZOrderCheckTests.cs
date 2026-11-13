using BrowserWall.Core.State;

namespace BrowserWall.Tests;

public class ZOrderCheckTests
{
    private const long Wall = 100;
    private const long Button = 200;
    private const long Other = 900;

    [Fact(DisplayName = "ZOrder_Grid_ParkedBelowWall_Ok")]
    public void Grid_ParkedBelowWall_Ok()
    {
        ZOrderReport report = ZOrderCheck.Inspect(new long[] { Other, Wall, 1, 2, 3 }, Wall, new long[] { 1, 2, 3 }, null, null);

        Assert.False(report.NeedsFix);
    }

    [Fact(DisplayName = "ZOrder_Grid_ParkedAboveWall_Listed")]
    public void Grid_ParkedAboveWall_Listed()
    {
        ZOrderReport report = ZOrderCheck.Inspect(new long[] { 3, Other, 1, Wall, 2 }, Wall, new long[] { 1, 2, 3 }, null, null);

        Assert.True(report.NeedsFix);
        Assert.Equal(new long[] { 3, 1 }, report.ParkedAboveWall);
        Assert.False(report.ExpandedOrderBroken);
    }

    [Fact(DisplayName = "ZOrder_Expanded_CorrectOrder_Ok")]
    public void Expanded_CorrectOrder_Ok()
    {
        ZOrderReport report = ZOrderCheck.Inspect(
            new long[] { Other, Button, 2, Wall, 1, 3 }, Wall, new long[] { 1, 3 }, 2, Button);

        Assert.False(report.NeedsFix);
    }

    [Fact(DisplayName = "ZOrder_Expanded_OtherParkedOnTop_Listed")]
    public void Expanded_OtherParkedOnTop_Listed()
    {
        // Button, expanded window and wall are still adjacent, but parked window 3 moved to the top
        ZOrderReport report = ZOrderCheck.Inspect(
            new long[] { 3, Button, 2, Wall, 1 }, Wall, new long[] { 1, 3 }, 2, Button);

        Assert.True(report.NeedsFix);
        Assert.Equal(new long[] { 3 }, report.ParkedAboveWall);
        Assert.False(report.ExpandedOrderBroken);
    }

    [Fact(DisplayName = "ZOrder_Expanded_BrowserAboveButton_Broken")]
    public void Expanded_BrowserAboveButton_Broken()
    {
        ZOrderReport report = ZOrderCheck.Inspect(
            new long[] { 2, Button, Wall, 1 }, Wall, new long[] { 1 }, 2, Button);

        Assert.True(report.ExpandedOrderBroken);
        Assert.Empty(report.ParkedAboveWall);
    }

    [Fact(DisplayName = "ZOrder_Expanded_WallNotDirectlyBelow_Broken")]
    public void Expanded_WallNotDirectlyBelow_Broken()
    {
        ZOrderReport report = ZOrderCheck.Inspect(
            new long[] { Button, 2, Other, Wall, 1 }, Wall, new long[] { 1 }, 2, Button);

        Assert.True(report.ExpandedOrderBroken);
    }

    [Fact(DisplayName = "ZOrder_MissingWindowsIgnored")]
    public void MissingWindowsIgnored()
    {
        Assert.False(ZOrderCheck.Inspect(new long[] { Wall, 1 }, Wall, new long[] { 1, 5 }, null, null).NeedsFix);
        Assert.False(ZOrderCheck.Inspect(new long[] { 1, 2 }, Wall, new long[] { 1, 2 }, null, null).NeedsFix);
        Assert.False(ZOrderCheck.Inspect(new long[] { Button, Wall, 1 }, Wall, new long[] { 1 }, 7, Button).NeedsFix);
    }
}
