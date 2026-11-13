using BrowserWall.Core.Config;
using BrowserWall.Core.Text;

namespace BrowserWall.Tests;

public class UrlRuleTests
{
    /// <summary>
    /// The rule shared by the config file and the page list: only the prefix is checked, and nothing is prepended.
    /// </summary>
    [Fact(DisplayName = "UrlRule_CheckPrefixOnly")]
    public void UrlRule_CheckPrefixOnly()
    {
        foreach (string text in new[]
                 {
                     "https://", "https://example.com/\"x", "HTTP://Example.com", "https://example.com/a b",
                     "http://example.com", "HTTPS://x",
                 })
        {
            Assert.Null(UrlRule.Check(text, UiLanguage.ZhTw));
            Assert.True(UrlRule.IsAllowed(text), text);
        }

        foreach (string text in new[] { "", "   ", "ftp://x", "time.is", "abc", "example.com" })
        {
            string? problem = UrlRule.Check(text, UiLanguage.ZhTw);
            Assert.False(string.IsNullOrWhiteSpace(problem), text);
            Assert.Contains("http://", problem);
            Assert.False(UrlRule.IsAllowed(text), text);
        }
    }
}
