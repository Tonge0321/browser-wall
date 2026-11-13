using System.Globalization;

namespace BrowserWall.Core.Text;

/// <summary>One key per piece of text shown to the user.</summary>
public enum UiTextKey
{
    AddPage,
    AddExisting,
    BackToGrid,
    PopOut,
    Close,
    OpenFailedTitle,
    OpenFailedCell,
    Launching,
    NoWindows,
    NoWindowsInline,
    WallFull,
    AddPageFailed,
    AddDialogTitle,
    AddDialogEmpty,
    AddFull,
    AddNotAllowed,
    ConfigCreated,
    ConfigInvalidHeader,
    ConfigErrorLine,
    ConfigWholeFile,
    ConfigCreateFailed,
    ConfigReadFailed,
    ConfigNotUtf8,
    ConfigNotJson,
    ConfigJsonLine,
    ConfigRootNotObject,
    ConfigMissingBrowserPath,
    ConfigMustBeString,
    ConfigEmptyBrowserPath,
    ConfigMustBeAbsolute,
    ConfigExeNotFound,
    ConfigNoDoubleQuote,
    ConfigMissingUrls,
    ConfigUrlsMustBeArray,
    ConfigUrlCount,
    UrlMustStartWithHttp,
    ConfigLanguageInvalid,
    SessionInvalid,
}

/// <summary>
/// The single string table for all user-facing text. Every entry has Traditional Chinese and English.
/// No other place in the program may contain user-facing text. Placeholders use <see cref="string.Format(IFormatProvider, string, object[])"/>
/// with the invariant culture.
/// </summary>
public static class UiStrings
{
    private static readonly Dictionary<UiTextKey, (string ZhTw, string En)> Entries = new()
    {
        [UiTextKey.AddPage] = ("＋ 新增網頁", "+ Add page"),
        [UiTextKey.AddExisting] = ("加入已開著的視窗（{0}）", "Add open window ({0})"),
        [UiTextKey.BackToGrid] = ("回到格子", "Back to grid"),
        [UiTextKey.PopOut] = ("移出", "Pop out"),
        [UiTextKey.Close] = ("關閉", "Close"),
        [UiTextKey.OpenFailedTitle] = ("開啟失敗　{0}", "Failed to open  {0}"),
        [UiTextKey.OpenFailedCell] = ("開啟失敗\n{0}", "Failed to open\n{0}"),
        [UiTextKey.Launching] = ("正在開啟瀏覽器…", "Opening browser…"),
        [UiTextKey.NoWindows] = ("沒有視窗\n\n設定檔位置：\n{0}", "No windows\n\nConfig file:\n{0}"),
        [UiTextKey.NoWindowsInline] = ("沒有視窗　設定檔位置：{0}", "No windows  Config file: {0}"),
        [UiTextKey.WallFull] = ("牆已滿，新視窗沒有加入", "Wall is full; the new window was not added"),
        [UiTextKey.AddPageFailed] = ("新增網頁失敗", "Could not add page"),
        [UiTextKey.AddDialogTitle] = ("加入已開著的視窗", "Add open window"),
        [UiTextKey.AddDialogEmpty] = ("沒有可加入的視窗", "No windows to add"),
        [UiTextKey.AddFull] = (
            "牆上已經有 {0} 格（含開啟失敗的格子），請先移出一格再新增。",
            "The wall already has {0} cells (including failed ones). Pop one out before adding."),
        [UiTextKey.AddNotAllowed] = ("目前不能新增網頁。", "Cannot add a page right now."),
        [UiTextKey.ConfigCreated] = (
            "已建立預設設定檔：\n{0}\n\n請用記事本開啟，填入要開的網址（1～{1} 個）。",
            "Created a default config file:\n{0}\n\nOpen it in Notepad and enter the URLs to open (1–{1})."),
        [UiTextKey.ConfigInvalidHeader] = ("設定檔有問題，程式將結束：", "The config file has problems. BrowserWall will exit:"),
        [UiTextKey.ConfigErrorLine] = ("・{0}：{1}", "• {0}: {1}"),
        [UiTextKey.ConfigWholeFile] = ("（整個檔案）", "(whole file)"),
        [UiTextKey.ConfigCreateFailed] = ("無法建立預設設定檔：{0}", "Could not create the default config file: {0}"),
        [UiTextKey.ConfigReadFailed] = ("無法讀取設定檔：{0}", "Could not read the config file: {0}"),
        [UiTextKey.ConfigNotUtf8] = ("不是合法的 UTF-8 文字", "Not valid UTF-8 text"),
        [UiTextKey.ConfigNotJson] = ("不是合法的 JSON{0}", "Not valid JSON{0}"),
        [UiTextKey.ConfigJsonLine] = ("（約第 {0} 行）", " (around line {0})"),
        [UiTextKey.ConfigRootNotObject] = ("最外層必須是 JSON 物件 {{ … }}", "The top level must be a JSON object {{ … }}"),
        [UiTextKey.ConfigMissingBrowserPath] = ("缺少這個欄位（瀏覽器執行檔的絕對路徑）", "Missing (absolute path of the browser executable)"),
        [UiTextKey.ConfigMustBeString] = ("必須是字串", "Must be a string"),
        [UiTextKey.ConfigEmptyBrowserPath] = (
            "不可為空字串，請填入 Chrome 或 Brave 執行檔的絕對路徑",
            "Must not be empty; enter the absolute path of chrome.exe or brave.exe"),
        [UiTextKey.ConfigMustBeAbsolute] = ("必須是絕對路徑：{0}", "Must be an absolute path: {0}"),
        [UiTextKey.ConfigExeNotFound] = ("找不到執行檔：{0}", "Executable not found: {0}"),
        [UiTextKey.ConfigNoDoubleQuote] = ("不可包含雙引號", "Must not contain double quotes"),
        [UiTextKey.ConfigMissingUrls] = ("缺少這個欄位（要開的網址陣列）", "Missing (array of URLs to open)"),
        [UiTextKey.ConfigUrlsMustBeArray] = (
            "必須是陣列，例如 [\"https://www.google.com\"]",
            "Must be an array, e.g. [\"https://www.google.com\"]"),
        [UiTextKey.ConfigUrlCount] = ("網址數量 {0} 個，必須 {1}～{2} 個", "{0} URLs; must be {1}–{2}"),
        [UiTextKey.UrlMustStartWithHttp] = ("必須以 http:// 或 https:// 開頭：{0}", "Must start with http:// or https://: {0}"),
        [UiTextKey.ConfigLanguageInvalid] = ("只能是 \"auto\"、\"zh-TW\" 或 \"en\"", "Must be \"auto\", \"zh-TW\" or \"en\""),
        [UiTextKey.SessionInvalid] = ("上次的網頁清單無法讀取，已改用設定檔", "Could not read the last page list; using the config file instead"),
    };

    /// <summary>Key → (Traditional Chinese, English). Format strings: literal braces are doubled.</summary>
    public static IReadOnlyDictionary<UiTextKey, (string ZhTw, string En)> Table => Entries;

    /// <summary>Text for <paramref name="key"/> with no placeholders filled in (literal braces un-doubled).</summary>
    public static string Get(UiLanguage lang, UiTextKey key) => Format(lang, key);

    /// <summary>Text for <paramref name="key"/> with placeholders filled in using the invariant culture.</summary>
    public static string Format(UiLanguage lang, UiTextKey key, params object[] args)
    {
        (string zh, string en) = Entries[key];
        return string.Format(CultureInfo.InvariantCulture, lang == UiLanguage.ZhTw ? zh : en, args);
    }
}
