using System.Text.Json;
using BrowserWall.Core.Config;

namespace BrowserWall.Tests;

public class ConfigDefaultsTests
{
    private const string LocalChrome = @"C:\Users\Tester\AppData\Local\Google\Chrome\Application\chrome.exe";
    private const string Brave = @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe";

    [Fact(DisplayName = "Defaults_BrowserSearchOrder")]
    public void BrowserSearchOrder()
    {
        Assert.Equal(FakeConfigEnvironment.ChromePath,
            ConfigDefaults.FindBrowser(new FakeConfigEnvironment(FakeConfigEnvironment.ChromePath, LocalChrome, Brave)));
        Assert.Equal(LocalChrome, ConfigDefaults.FindBrowser(new FakeConfigEnvironment(LocalChrome, Brave)));
        Assert.Equal(Brave, ConfigDefaults.FindBrowser(new FakeConfigEnvironment(Brave)));
        Assert.Equal(string.Empty, ConfigDefaults.FindBrowser(new FakeConfigEnvironment()));
    }

    [Fact(DisplayName = "Defaults_CreatedFile_ParsesBack")]
    public void CreatedFile_ParsesBack()
    {
        var env = new FakeConfigEnvironment(Brave);
        byte[] bytes = ConfigDefaults.CreateBytes(env);

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);

        ConfigParseResult result = ConfigParser.Parse(bytes, env);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.Equal(Brave, result.Config!.BrowserPath);
        Assert.Equal(@"C:\Users\Tester\AppData\Local\BrowserWall\profile", result.Config.ProfileDir);
        Assert.Equal(new[] { "https://www.google.com" }, result.Config.Urls);

        // hotkeyShrink was removed, so the default file no longer writes that field.
        using JsonDocument doc = JsonDocument.Parse(bytes);
        Assert.False(doc.RootElement.TryGetProperty("hotkeyShrink", out _));
    }

    [Fact(DisplayName = "Defaults_NoBrowserFound_FileReportsBrowserPath")]
    public void NoBrowserFound_FileReportsBrowserPath()
    {
        var env = new FakeConfigEnvironment();
        ConfigParseResult result = ConfigParser.Parse(ConfigDefaults.CreateBytes(env), env);

        Assert.False(result.Success);
        Assert.Equal(ConfigParser.FieldBrowserPath, Assert.Single(result.Errors).Field);
    }
}
