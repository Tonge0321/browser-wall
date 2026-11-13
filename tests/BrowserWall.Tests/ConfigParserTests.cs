using System.Text;
using BrowserWall.Core.Config;

namespace BrowserWall.Tests;

public class ConfigParserTests
{
    private const string ValidJson = """
        {
          "browserPath": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
          "profileDir": "D:\\WallProfile",
          "urls": ["https://example.com/a", "http://example.org/b"],
          "hotkeyShrink": "Ctrl+Shift+F2"
        }
        """;

    private static readonly FakeConfigEnvironment Env = new(FakeConfigEnvironment.ChromePath);

    private static ConfigParseResult Parse(string json, bool bom = false)
    {
        byte[] body = new UTF8Encoding(false).GetBytes(json);
        if (bom)
        {
            body = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray();
        }

        return ConfigParser.Parse(body, Env);
    }

    private static string UrlsJson(int count) =>
        "[" + string.Join(",", Enumerable.Range(1, count).Select(i => $"\"https://example.com/{i}\"")) + "]";

    private static string WithUrls(string urlsJson) => $$"""
        {
          "browserPath": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
          "urls": {{urlsJson}}
        }
        """;

    private static void AssertValid(ConfigParseResult result)
    {
        Assert.True(result.Success, string.Join("\n", result.Errors));
        WallConfig c = result.Config!;
        Assert.Equal(FakeConfigEnvironment.ChromePath, c.BrowserPath);
        Assert.Equal(@"D:\WallProfile", c.ProfileDir);
        Assert.Equal(new[] { "https://example.com/a", "http://example.org/b" }, c.Urls);
    }

    [Fact(DisplayName = "Config_ValidFile_Parses")]
    public void ValidFile_Parses() => AssertValid(Parse(ValidJson));

    [Fact(DisplayName = "Config_ValidFileWithBom_Parses")]
    public void ValidFileWithBom_Parses() => AssertValid(Parse(ValidJson, bom: true));

    [Fact(DisplayName = "Config_InvalidJson_ReportsError")]
    public void InvalidJson_ReportsError()
    {
        ConfigParseResult result = Parse("{ \"urls\": [\"https://a.com\", }");

        Assert.False(result.Success);
        ConfigError error = Assert.Single(result.Errors);
        Assert.Equal(ConfigError.WholeFile, error.Field);
        Assert.Contains("JSON", error.Message);
    }

    [Fact(DisplayName = "Config_RootNotObject_ReportsError")]
    public void RootNotObject_ReportsError()
    {
        ConfigParseResult result = Parse("[1, 2]");

        Assert.False(result.Success);
        Assert.Equal(ConfigError.WholeFile, Assert.Single(result.Errors).Field);
    }

    [Fact(DisplayName = "Config_UrlCountOutOfRange_ReportsError")]
    public void UrlCountOutOfRange_ReportsError()
    {
        foreach (int count in new[] { 0, 17 })
        {
            ConfigParseResult result = Parse(WithUrls(UrlsJson(count)));

            Assert.False(result.Success);
            ConfigError error = Assert.Single(result.Errors);
            Assert.Equal(ConfigParser.FieldUrls, error.Field);
            Assert.Contains($"{count} 個", error.Message);
        }
    }

    [Fact(DisplayName = "Config_UrlCountOneAndSixteen_Accepted")]
    public void UrlCountOneAndSixteen_Accepted()
    {
        foreach (int count in new[] { 1, 16 })
        {
            ConfigParseResult result = Parse(WithUrls(UrlsJson(count)));

            Assert.True(result.Success, string.Join("\n", result.Errors));
            Assert.Equal(count, result.Config!.Urls.Count);
        }
    }

    [Theory(DisplayName = "Config_BadUrl_ReportsError")]
    [InlineData("ftp://example.com")]
    [InlineData("example.com")]
    public void BadUrl_ReportsError(string url)
    {
        string urls = "[\"https://ok.com\", " + System.Text.Json.JsonSerializer.Serialize(url) + "]";
        ConfigParseResult result = Parse(WithUrls(urls));

        Assert.False(result.Success);
        Assert.Equal("urls[1]", Assert.Single(result.Errors).Field);
    }

    [Fact(DisplayName = "Config_UrlUppercaseScheme_Accepted")]
    public void UrlUppercaseScheme_Accepted()
    {
        ConfigParseResult result = Parse(WithUrls("[\"HTTPS://Example.com\"]"));

        Assert.True(result.Success, string.Join("\n", result.Errors));
    }

    [Fact(DisplayName = "Config_UrlNotString_ReportsError")]
    public void UrlNotString_ReportsError()
    {
        ConfigParseResult result = Parse(WithUrls("[\"https://ok.com\", 5]"));

        Assert.False(result.Success);
        Assert.Equal("urls[1]", Assert.Single(result.Errors).Field);
    }

    [Fact(DisplayName = "Config_BrowserMissing_ReportsError")]
    public void BrowserMissing_ReportsError()
    {
        string json = """
            { "browserPath": "C:\\nowhere\\chrome.exe", "urls": ["https://a.com"] }
            """;
        ConfigParseResult result = Parse(json);

        Assert.False(result.Success);
        ConfigError error = Assert.Single(result.Errors);
        Assert.Equal(ConfigParser.FieldBrowserPath, error.Field);
        Assert.Contains(@"C:\nowhere\chrome.exe", error.Message);
    }

    [Fact(DisplayName = "Config_BrowserRelativePath_ReportsError")]
    public void BrowserRelativePath_ReportsError()
    {
        var env = new FakeConfigEnvironment("chrome.exe");
        byte[] bytes = Encoding.UTF8.GetBytes("""{ "browserPath": "chrome.exe", "urls": ["https://a.com"] }""");
        ConfigParseResult result = ConfigParser.Parse(bytes, env);

        Assert.False(result.Success);
        Assert.Equal(ConfigParser.FieldBrowserPath, Assert.Single(result.Errors).Field);
    }

    [Fact(DisplayName = "Config_WrongTypes_ReportEachField")]
    public void WrongTypes_ReportEachField()
    {
        string json = """
            { "browserPath": 1, "profileDir": true, "urls": "https://a.com", "hotkeyShrink": 5 }
            """;
        ConfigParseResult result = Parse(json);

        Assert.False(result.Success);
        // A wrong type for hotkeyShrink is also ignored, not an error.
        Assert.Equal(
            new[] { "browserPath", "profileDir", "urls" },
            result.Errors.Select(e => e.Field).ToArray());
    }

    [Fact(DisplayName = "Config_MissingRequiredFields_ReportsError")]
    public void MissingRequiredFields_ReportsError()
    {
        ConfigParseResult result = Parse("{}");

        Assert.False(result.Success);
        Assert.Equal(new[] { "browserPath", "urls" }, result.Errors.Select(e => e.Field).ToArray());
    }

    [Fact(DisplayName = "Config_ProfileDirRelative_ReportsError")]
    public void ProfileDirRelative_ReportsError()
    {
        string json = """
            {
              "browserPath": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
              "profileDir": "profile",
              "urls": ["https://a.com"]
            }
            """;
        ConfigParseResult result = Parse(json);

        Assert.False(result.Success);
        Assert.Equal(ConfigParser.FieldProfileDir, Assert.Single(result.Errors).Field);
    }

    [Fact(DisplayName = "Config_OptionalFieldsMissing_UseDefaults")]
    public void OptionalFieldsMissing_UseDefaults()
    {
        ConfigParseResult result = Parse(WithUrls("[\"https://a.com\"]"));

        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.Equal(@"C:\Users\Tester\AppData\Local\BrowserWall\profile", result.Config!.ProfileDir);
    }

    // Sample source: ValidJson in this file (a valid sample that already contains hotkeyShrink); only the hotkeyShrink value changes.
    [Fact(DisplayName = "Config_HotkeyShrinkIgnored")]
    public void HotkeyShrinkIgnored()
    {
        foreach (string hotkeyValue in new[] { "\"Ctrl+Alt+W\"", "123", "\"garbage\"" })
        {
            string json = ValidJson.Replace("\"Ctrl+Shift+F2\"", hotkeyValue);
            Assert.Contains("\"hotkeyShrink\": " + hotkeyValue, json);

            AssertValid(Parse(json));
        }
    }

    // Sample source: WithUrls in this file; only the prefix is required, so these 4 values are valid.
    [Theory(DisplayName = "Config_UrlPrefixOnly_Accepted")]
    [InlineData("https://")]
    [InlineData("https://example.com/a b")]
    [InlineData("https://example.com/\" --evil")]
    [InlineData("https://example.com/\t")]
    public void UrlPrefixOnly_Accepted(string url)
    {
        string urls = "[\"https://ok.com\", " + System.Text.Json.JsonSerializer.Serialize(url) + "]";
        ConfigParseResult result = Parse(WithUrls(urls));

        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.Equal(new[] { "https://ok.com", url }, result.Config!.Urls);
    }

    [Fact(DisplayName = "Config_InvalidUtf8_ReportsError")]
    public void InvalidUtf8_ReportsError()
    {
        ConfigParseResult result = ConfigParser.Parse(new byte[] { 0x7B, 0xFF, 0xFE, 0x7D }, Env);

        Assert.False(result.Success);
        Assert.Equal(ConfigError.WholeFile, Assert.Single(result.Errors).Field);
    }
}
