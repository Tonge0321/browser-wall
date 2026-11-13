using System.Text;
using System.Text.RegularExpressions;
using BrowserWall.Core;
using BrowserWall.Core.Config;
using BrowserWall.Core.Health;
using BrowserWall.Core.Launch;
using BrowserWall.Core.Text;

namespace BrowserWall.Tests;

/// <summary>UI language, the bilingual string table and English diagnostics lines.</summary>
public class LanguageTests
{
    private const string ValidUrls = "[\"https://example.com/a\"]";

    private static FakeConfigEnvironment Env(string culture) =>
        new(FakeConfigEnvironment.ChromePath) { UiCultureName = culture };

    /// <param name="languageJson">Raw JSON for the "language" value, or null to omit the field.</param>
    private static ConfigParseResult Parse(string culture, string? languageJson, string urlsJson = ValidUrls)
    {
        string browser = System.Text.Json.JsonSerializer.Serialize(FakeConfigEnvironment.ChromePath);
        string language = languageJson is null ? string.Empty : $"\"language\":{languageJson},";
        string json = "{" + language + "\"browserPath\":" + browser + ",\"urls\":" + urlsJson + "}";
        return ConfigParser.Parse(new UTF8Encoding(false).GetBytes(json), Env(culture));
    }

    private static bool HasCjk(string s) => s.Any(ch => ch >= 0x2E80 && ch <= 0x9FFF || ch >= 0xF900 && ch <= 0xFAFF || ch >= 0xFF00 && ch <= 0xFFEF || ch >= 0x3000 && ch <= 0x303F);

    private static int PlaceholderCount(string format) =>
        Regex.Matches(format.Replace("{{", string.Empty).Replace("}}", string.Empty), @"\{(\d+)\}")
            .Select(m => m.Groups[1].Value).Distinct().Count();

    [Fact(DisplayName = "Lang_AutoChineseWhenUiCultureIsZh")]
    public void Lang_AutoChineseWhenUiCultureIsZh()
    {
        foreach (string culture in new[] { "zh-TW", "zh-CN", "zh-Hant-TW", "zh", "ZH-tw" })
        {
            Assert.Equal(UiLanguage.ZhTw, UiLanguageRule.Resolve(null, culture));
            Assert.Equal(UiLanguage.ZhTw, UiLanguageRule.Resolve("auto", culture));
        }

        ConfigParseResult omitted = Parse("zh-TW", null);
        Assert.True(omitted.Success);
        Assert.Equal(UiLanguage.ZhTw, omitted.Language);

        ConfigParseResult auto = Parse("zh-TW", "\"auto\"");
        Assert.True(auto.Success);
        Assert.Equal(UiLanguage.ZhTw, auto.Language);

        // Messages follow the language: Chinese here.
        ConfigParseResult bad = Parse("zh-TW", null, "[\"example.com\"]");
        Assert.False(bad.Success);
        Assert.Equal(UiLanguage.ZhTw, bad.Language);
        ConfigError error = Assert.Single(bad.Errors);
        Assert.Equal("urls[0]", error.Field);
        Assert.Equal("必須以 http:// 或 https:// 開頭：example.com", error.Message);
    }

    [Fact(DisplayName = "Lang_AutoEnglishOtherwise")]
    public void Lang_AutoEnglishOtherwise()
    {
        foreach (string? culture in new[] { "en-US", "ja-JP", "fr-FR", "", null })
        {
            Assert.Equal(UiLanguage.En, UiLanguageRule.Resolve(null, culture));
            Assert.Equal(UiLanguage.En, UiLanguageRule.Resolve("auto", culture));
        }

        ConfigParseResult ok = Parse("en-US", null);
        Assert.True(ok.Success);
        Assert.Equal(UiLanguage.En, ok.Language);

        ConfigParseResult bad = Parse("en-US", null, "[\"example.com\"]");
        Assert.False(bad.Success);
        Assert.Equal(UiLanguage.En, bad.Language);
        ConfigError error = Assert.Single(bad.Errors);
        Assert.Equal("urls[0]", error.Field);
        Assert.Equal("Must start with http:// or https://: example.com", error.Message);

        // Whole-file errors are English too, and the result carries the language.
        ConfigParseResult notJson = ConfigParser.Parse(new UTF8Encoding(false).GetBytes("{ \"urls\": [1, }"), Env("en-US"));
        Assert.Equal(UiLanguage.En, notJson.Language);
        ConfigError whole = Assert.Single(notJson.Errors);
        Assert.Equal(ConfigError.WholeFile, whole.Field);
        Assert.StartsWith("Not valid JSON (around line ", whole.Message);
        Assert.False(HasCjk(whole.Message));
    }

    [Fact(DisplayName = "Lang_ConfigOverridesAuto")]
    public void Lang_ConfigOverridesAuto()
    {
        Assert.Equal(UiLanguage.En, UiLanguageRule.Resolve("en", "zh-TW"));
        Assert.Equal(UiLanguage.ZhTw, UiLanguageRule.Resolve("zh-TW", "en-US"));

        ConfigParseResult en = Parse("zh-TW", "\"en\"");
        Assert.True(en.Success);
        Assert.Equal(UiLanguage.En, en.Language);

        ConfigParseResult zh = Parse("en-US", "\"zh-TW\"");
        Assert.True(zh.Success);
        Assert.Equal(UiLanguage.ZhTw, zh.Language);

        // Error messages use the language from the config file, not the Windows language.
        ConfigParseResult enBad = Parse("zh-TW", "\"en\"", "[\"ftp://x\"]");
        Assert.Equal(UiLanguage.En, enBad.Language);
        Assert.Equal("Must start with http:// or https://: ftp://x", Assert.Single(enBad.Errors).Message);

        ConfigParseResult zhBad = Parse("en-US", "\"zh-TW\"", "[]");
        Assert.Equal(UiLanguage.ZhTw, zhBad.Language);
        Assert.Equal("網址數量 0 個，必須 1～16 個", Assert.Single(zhBad.Errors).Message);
    }

    [Fact(DisplayName = "Lang_InvalidValueIsConfigError")]
    public void Lang_InvalidValueIsConfigError()
    {
        Assert.False(UiLanguageRule.IsValidSetting("EN"));
        Assert.False(UiLanguageRule.IsValidSetting("zh-tw"));
        Assert.False(UiLanguageRule.IsValidSetting("Auto"));
        Assert.True(UiLanguageRule.IsValidSetting("auto"));
        Assert.True(UiLanguageRule.IsValidSetting("zh-TW"));
        Assert.True(UiLanguageRule.IsValidSetting("en"));

        foreach (string value in new[] { "\"xx\"", "\"EN\"", "\"zh-tw\"", "\"\"", "123", "null", "[]", "{}", "true" })
        {
            foreach ((string culture, UiLanguage auto) in new[] { ("zh-TW", UiLanguage.ZhTw), ("en-US", UiLanguage.En) })
            {
                ConfigParseResult result = Parse(culture, value);
                Assert.False(result.Success, value);
                ConfigError error = Assert.Single(result.Errors);
                Assert.Equal("language", error.Field);
                Assert.Equal(UiStrings.Get(auto, UiTextKey.ConfigLanguageInvalid), error.Message);
                Assert.Equal(auto, result.Language);
            }
        }

        // Other errors at the same time are also in the auto language.
        ConfigParseResult both = Parse("en-US", "\"xx\"", "[\"example.com\"]");
        Assert.Equal(2, both.Errors.Count);
        Assert.Equal("language", both.Errors[0].Field);
        Assert.Equal("Must start with http:// or https://: example.com", both.Errors[1].Message);

        // The default config file has no "language" field and still parses.
        var env = new FakeConfigEnvironment(FakeConfigEnvironment.ChromePath);
        string defaults = ConfigDefaults.CreateJson(env);
        Assert.DoesNotContain("language", defaults);
        Assert.True(ConfigParser.Parse(ConfigDefaults.CreateBytes(env), env).Success);
    }

    [Fact(DisplayName = "Strings_EveryKeyHasBothLanguages")]
    public void Strings_EveryKeyHasBothLanguages()
    {
        foreach (UiTextKey key in Enum.GetValues<UiTextKey>())
        {
            Assert.True(UiStrings.Table.ContainsKey(key), key.ToString());
            (string zh, string en) = UiStrings.Table[key];
            Assert.False(string.IsNullOrWhiteSpace(zh), key.ToString());
            Assert.False(string.IsNullOrWhiteSpace(en), key.ToString());
            Assert.True(HasCjk(zh), key.ToString());
            Assert.False(HasCjk(en), key.ToString());
            Assert.Equal(PlaceholderCount(zh), PlaceholderCount(en));

            object[] args = Enumerable.Range(0, PlaceholderCount(zh)).Select(i => (object)$"<{i}>").ToArray();
            string zhText = UiStrings.Format(UiLanguage.ZhTw, key, args);
            string enText = UiStrings.Format(UiLanguage.En, key, args);
            Assert.NotEqual(zhText, enText);
            Assert.Equal(string.Format(System.Globalization.CultureInfo.InvariantCulture, zh, args), zhText);
            Assert.Equal(string.Format(System.Globalization.CultureInfo.InvariantCulture, en, args), enText);
            if (args.Length == 0)
            {
                Assert.Equal(zhText, UiStrings.Get(UiLanguage.ZhTw, key));
                Assert.Equal(enText, UiStrings.Get(UiLanguage.En, key));
            }
        }

        Assert.Equal(Enum.GetValues<UiTextKey>().Length, UiStrings.Table.Count);
        Assert.Equal("Back to grid", UiStrings.Get(UiLanguage.En, UiTextKey.BackToGrid));
        Assert.Equal("+ Add page", UiStrings.Get(UiLanguage.En, UiTextKey.AddPage));
        Assert.Equal("Pop out", UiStrings.Get(UiLanguage.En, UiTextKey.PopOut));
        Assert.Equal("Close", UiStrings.Get(UiLanguage.En, UiTextKey.Close));
        Assert.Equal("Add open window (3)", UiStrings.Format(UiLanguage.En, UiTextKey.AddExisting, 3));
    }

    /// <summary>The Traditional Chinese text is unchanged from before the string table existed (formatted with the same values).</summary>
    [Fact(DisplayName = "Strings_ChineseMatchesVersion8")]
    public void Strings_ChineseMatchesVersion8()
    {
        const string url = "https://example.com/x";
        const string path = @"C:\Users\Tester\AppData\Local\BrowserWall\config.json";
        int max = WallLimits.MaxCells;
        var expected = new (UiTextKey Key, object[] Args, string Text)[]
        {
            (UiTextKey.AddPage, Array.Empty<object>(), "＋ 新增網頁"),
            (UiTextKey.AddExisting, new object[] { 2 }, "加入已開著的視窗（2）"),
            (UiTextKey.BackToGrid, Array.Empty<object>(), "回到格子"),
            (UiTextKey.PopOut, Array.Empty<object>(), "移出"),
            (UiTextKey.Close, Array.Empty<object>(), "關閉"),
            (UiTextKey.OpenFailedTitle, new object[] { url }, $"開啟失敗　{url}"),
            (UiTextKey.OpenFailedCell, new object[] { url }, $"開啟失敗\n{url}"),
            (UiTextKey.Launching, Array.Empty<object>(), "正在開啟瀏覽器…"),
            (UiTextKey.NoWindows, new object[] { path }, $"沒有視窗\n\n設定檔位置：\n{path}"),
            (UiTextKey.NoWindowsInline, new object[] { path }, $"沒有視窗　設定檔位置：{path}"),
            (UiTextKey.WallFull, Array.Empty<object>(), "牆已滿，新視窗沒有加入"),
            (UiTextKey.AddPageFailed, Array.Empty<object>(), "新增網頁失敗"),
            (UiTextKey.AddDialogTitle, Array.Empty<object>(), "加入已開著的視窗"),
            (UiTextKey.AddDialogEmpty, Array.Empty<object>(), "沒有可加入的視窗"),
            (UiTextKey.AddFull, new object[] { max }, $"牆上已經有 {max} 格（含開啟失敗的格子），請先移出一格再新增。"),
            (UiTextKey.AddNotAllowed, Array.Empty<object>(), "目前不能新增網頁。"),
            (UiTextKey.ConfigCreated, new object[] { path, max }, $"已建立預設設定檔：\n{path}\n\n請用記事本開啟，填入要開的網址（1～{max} 個）。"),
            (UiTextKey.ConfigInvalidHeader, Array.Empty<object>(), "設定檔有問題，程式將結束："),
            (UiTextKey.ConfigErrorLine, new object[] { "urls", "必須是字串" }, "・urls：必須是字串"),
            (UiTextKey.ConfigWholeFile, Array.Empty<object>(), "（整個檔案）"),
            (UiTextKey.ConfigCreateFailed, new object[] { "denied" }, "無法建立預設設定檔：denied"),
            (UiTextKey.ConfigReadFailed, new object[] { "denied" }, "無法讀取設定檔：denied"),
            (UiTextKey.ConfigNotUtf8, Array.Empty<object>(), "不是合法的 UTF-8 文字"),
            (UiTextKey.ConfigNotJson, new object[] { "（約第 3 行）" }, "不是合法的 JSON（約第 3 行）"),
            (UiTextKey.ConfigJsonLine, new object[] { 3 }, "（約第 3 行）"),
            (UiTextKey.ConfigRootNotObject, Array.Empty<object>(), "最外層必須是 JSON 物件 { … }"),
            (UiTextKey.ConfigMissingBrowserPath, Array.Empty<object>(), "缺少這個欄位（瀏覽器執行檔的絕對路徑）"),
            (UiTextKey.ConfigMustBeString, Array.Empty<object>(), "必須是字串"),
            (UiTextKey.ConfigEmptyBrowserPath, Array.Empty<object>(), "不可為空字串，請填入 Chrome 或 Brave 執行檔的絕對路徑"),
            (UiTextKey.ConfigMustBeAbsolute, new object[] { "x" }, "必須是絕對路徑：x"),
            (UiTextKey.ConfigExeNotFound, new object[] { "x" }, "找不到執行檔：x"),
            (UiTextKey.ConfigNoDoubleQuote, Array.Empty<object>(), "不可包含雙引號"),
            (UiTextKey.ConfigMissingUrls, Array.Empty<object>(), "缺少這個欄位（要開的網址陣列）"),
            (UiTextKey.ConfigUrlsMustBeArray, Array.Empty<object>(), "必須是陣列，例如 [\"https://www.google.com\"]"),
            (UiTextKey.ConfigUrlCount, new object[] { 17, 1, max }, $"網址數量 17 個，必須 1～{max} 個"),
            (UiTextKey.UrlMustStartWithHttp, new object[] { "abc" }, "必須以 http:// 或 https:// 開頭：abc"),
            (UiTextKey.SessionInvalid, Array.Empty<object>(), "上次的網頁清單無法讀取，已改用設定檔"),
        };

        foreach ((UiTextKey key, object[] args, string text) in expected)
        {
            Assert.Equal(text, UiStrings.Format(UiLanguage.ZhTw, key, args));
        }

        // Every key except the newly added "language" error is checked.
        Assert.Equal(
            Enum.GetValues<UiTextKey>().Where(k => k != UiTextKey.ConfigLanguageInvalid).OrderBy(k => k),
            expected.Select(e => e.Key).OrderBy(k => k));
    }

    [Fact(DisplayName = "Diag_LinesAreEnglish")]
    public void Diag_LinesAreEnglish()
    {
        var t = new DateTime(2026, 11, 13, 8, 0, 0, DateTimeKind.Utc);
        var c = new Rgb(1, 2, 3);
        var lines = new List<string>();
        foreach (int? cell in new int?[] { null, 1, 16 })
        {
            lines.Add(DiagnosticsFormat.Flat(t, cell, c));
            lines.Add(DiagnosticsFormat.Nudge(t, cell, c));
            foreach (NudgeOutcome o in Enum.GetValues<NudgeOutcome>())
            {
                lines.Add(DiagnosticsFormat.NudgeResult(t, cell, c, o));
            }

            lines.Add(DiagnosticsFormat.CaptureTimeout(t, cell));
            lines.Add(DiagnosticsFormat.TaskbarTimeout(t, cell, true));
            lines.Add(DiagnosticsFormat.TaskbarTimeout(t, cell, false));
        }

        lines.Add(DiagnosticsFormat.SlowRound(t, TimeSpan.FromMilliseconds(1500)));
        foreach (BackgroundWorkKind k in Enum.GetValues<BackgroundWorkKind>())
        {
            lines.Add(DiagnosticsFormat.StuckCap(t, k, true));
            lines.Add(DiagnosticsFormat.StuckCap(t, k, false));
        }

        foreach (PreferencesWriteResult r in Enum.GetValues<PreferencesWriteResult>())
        {
            lines.Add(DiagnosticsFormat.PreferencesFailure(t, r));
        }

        lines.Add(DiagnosticsFormat.PreferencesTempDeleteFailed(t));
        lines.Add(DiagnosticsFormat.SessionWrite(t, EpisodeChange.FirstFailure));
        lines.Add(DiagnosticsFormat.SessionWrite(t, EpisodeChange.Recovered));

        foreach (string line in lines)
        {
            Assert.True(line.All(ch => ch < 0x80), line);
            string[] fields = line.Split('\t');
            Assert.True(fields.Length >= 3, line);
            Assert.Equal("2026-11-13T08:00:00Z", fields[0]);
            Assert.Matches(@"^(-|cell \d+)$", fields[1]);
        }

        // Full English samples.
        const string T = "2026-11-13T08:00:00Z";
        var samples = new (string Expected, string Actual)[]
        {
            ($"{T}\tcell 1\tflat\t#010203", DiagnosticsFormat.Flat(t, 1, c)),
            ($"{T}\tcell 1\tnudge\t#010203", DiagnosticsFormat.Nudge(t, 1, c)),
            ($"{T}\tcell 1\tnudge result\t#010203\trecovered", DiagnosticsFormat.NudgeResult(t, 1, c, NudgeOutcome.Recovered)),
            ($"{T}\tcell 1\tnudge result\t#010203\tnot recovered", DiagnosticsFormat.NudgeResult(t, 1, c, NudgeOutcome.NotRecovered)),
            ($"{T}\t-\tnudge result\t#010203\tnot checked", DiagnosticsFormat.NudgeResult(t, null, c, NudgeOutcome.NotChecked)),
            ($"{T}\t-\tcheck round over 1 s\t1500 ms", DiagnosticsFormat.SlowRound(t, TimeSpan.FromMilliseconds(1500))),
            ($"{T}\tcell 16\tcapture timed out", DiagnosticsFormat.CaptureTimeout(t, 16)),
            ($"{T}\t-\ttaskbar show timed out, skipped", DiagnosticsFormat.TaskbarTimeout(t, null, true)),
            ($"{T}\tcell 2\ttaskbar hide timed out, skipped", DiagnosticsFormat.TaskbarTimeout(t, 2, false)),
            ($"{T}\t-\tbackground taskbar stuck cap reached, paused", DiagnosticsFormat.StuckCap(t, BackgroundWorkKind.Taskbar, true)),
            ($"{T}\t-\tbackground address bar resumed", DiagnosticsFormat.StuckCap(t, BackgroundWorkKind.AddressBar, false)),
            ($"{T}\t-\tbackground flat check stuck cap reached, paused", DiagnosticsFormat.StuckCap(t, BackgroundWorkKind.FlatCheck, true)),
            ($"{T}\t-\tpreferences read failed, not written", DiagnosticsFormat.PreferencesFailure(t, PreferencesWriteResult.ReadFailed)),
            ($"{T}\t-\tpreferences backup failed, not written", DiagnosticsFormat.PreferencesFailure(t, PreferencesWriteResult.BackupFailed)),
            ($"{T}\t-\tpreferences temp file failed, not written", DiagnosticsFormat.PreferencesFailure(t, PreferencesWriteResult.TempFailed)),
            ($"{T}\t-\tpreferences replace failed, original kept", DiagnosticsFormat.PreferencesFailure(t, PreferencesWriteResult.ReplaceFailedOriginalKept)),
            ($"{T}\t-\tpreferences replace failed, restore failed", DiagnosticsFormat.PreferencesFailure(t, PreferencesWriteResult.ReplaceFailedRestoreFailed)),
            ($"{T}\t-\tpreferences written", DiagnosticsFormat.PreferencesFailure(t, PreferencesWriteResult.Written)),
            ($"{T}\t-\tpreferences not needed", DiagnosticsFormat.PreferencesFailure(t, PreferencesWriteResult.NotNeeded)),
            ($"{T}\t-\tpreferences temp file delete failed", DiagnosticsFormat.PreferencesTempDeleteFailed(t)),
            ($"{T}\t-\tsession write failed", DiagnosticsFormat.SessionWrite(t, EpisodeChange.FirstFailure)),
            ($"{T}\t-\tsession write recovered", DiagnosticsFormat.SessionWrite(t, EpisodeChange.Recovered)),
        };

        foreach ((string expectedLine, string actual) in samples)
        {
            Assert.Equal(expectedLine, actual);
        }
    }
}
