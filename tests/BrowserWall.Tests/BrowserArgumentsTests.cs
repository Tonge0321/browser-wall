using BrowserWall.Core.Launch;

namespace BrowserWall.Tests;

public class BrowserArgumentsTests
{
    [Fact(DisplayName = "Launch_Arguments_FixedOrder")]
    public void Arguments_FixedOrder()
    {
        IReadOnlyList<string> args = BrowserArguments.For(@"C:\Users\Tester\AppData\Local\BrowserWall\profile", "https://example.com/x?a=1&b=2");

        Assert.Equal(
            new[]
            {
                @"--user-data-dir=C:\Users\Tester\AppData\Local\BrowserWall\profile",
                "--disable-features=CalculateNativeWinOcclusion",
                "--new-window",
                "https://example.com/x?a=1&b=2",
            },
            args);
    }
}
