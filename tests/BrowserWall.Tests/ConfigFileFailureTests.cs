using BrowserWall.Core.Config;
using BrowserWall.Core.Text;

namespace BrowserWall.Tests;

public class ConfigFileFailureTests
{
    [Fact(DisplayName = "Config_FileFailureTextIsLocalized")]
    public void Config_FileFailureTextIsLocalized()
    {
        Assert.Equal(ConfigFileFailureKind.NoAccess, ConfigFileFailure.Classify(new UnauthorizedAccessException("x")));
        Assert.Equal(ConfigFileFailureKind.Unavailable, ConfigFileFailure.Classify(new IOException("x")));
        Assert.Equal(ConfigFileFailureKind.Unavailable, ConfigFileFailure.Classify(new FileNotFoundException("x")));
        Assert.Equal(ConfigFileFailureKind.Unavailable, ConfigFileFailure.Classify(new DirectoryNotFoundException("x")));

        var expected = new (UiLanguage Lang, bool Creating, ConfigFileFailureKind Kind, string Text)[]
        {
            (UiLanguage.ZhTw, true, ConfigFileFailureKind.NoAccess, "無法建立預設設定檔：沒有存取權限"),
            (UiLanguage.ZhTw, true, ConfigFileFailureKind.Unavailable, "無法建立預設設定檔：檔案無法讀寫（可能正被其他程式使用）"),
            (UiLanguage.ZhTw, false, ConfigFileFailureKind.NoAccess, "無法讀取設定檔：沒有存取權限"),
            (UiLanguage.ZhTw, false, ConfigFileFailureKind.Unavailable, "無法讀取設定檔：檔案無法讀寫（可能正被其他程式使用）"),
            (UiLanguage.En, true, ConfigFileFailureKind.NoAccess, "Could not create the default config file: Access denied"),
            (UiLanguage.En, true, ConfigFileFailureKind.Unavailable,
                "Could not create the default config file: The file cannot be read or written (it may be in use by another program)"),
            (UiLanguage.En, false, ConfigFileFailureKind.NoAccess, "Could not read the config file: Access denied"),
            (UiLanguage.En, false, ConfigFileFailureKind.Unavailable,
                "Could not read the config file: The file cannot be read or written (it may be in use by another program)"),
        };

        foreach ((UiLanguage lang, bool creating, ConfigFileFailureKind kind, string text) in expected)
        {
            Assert.Equal(text, ConfigFileFailure.Message(lang, creating, kind));
        }

        // The system's own message never reaches the text.
        const string systemText = "SYSTEM-MESSAGE-1234";
        foreach (Exception ex in new Exception[] { new UnauthorizedAccessException(systemText), new IOException(systemText) })
        {
            foreach (UiLanguage lang in new[] { UiLanguage.ZhTw, UiLanguage.En })
            {
                Assert.DoesNotContain(systemText, ConfigFileFailure.Message(lang, false, ConfigFileFailure.Classify(ex)));
                Assert.DoesNotContain(systemText, ConfigFileFailure.Message(lang, true, ConfigFileFailure.Classify(ex)));
            }
        }
    }
}
