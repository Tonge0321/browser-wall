# BrowserWall

BrowserWall tiles several Chrome (or Brave) windows on one screen as live thumbnails. Click a tile and that browser window grows to fill the wall, so you can click, type and scroll in it. Click "Back to grid" to shrink it again.

Windows only. The interface is in English or Traditional Chinese and follows your Windows display language. A Chinese version of this README is at the end.

## Features

- Tiles are live Windows thumbnails (DWM thumbnails). When a tile is expanded you are using the real browser window, and the other tiles keep updating.
- Up to 16 tiles, arranged in a grid by count: 4 tiles in 2×2, 12 in 4×3, 16 in 4×4. Two tiles are stacked top and bottom.
- Drag a tile onto another tile to swap them.
- "+ Add page" opens a new tab and expands it; type the address in Chrome's address bar. Dragging a tab out of a browser window also adds a tile.
- Each tile remembers the last URL it showed, and the next launch reopens them in the same order.
- The browsers on the wall stay off the taskbar. When you minimize or close the wall, they go with it.
- A tile sometimes turns into a single solid color (the cause is still unknown). The wall checks every 10 seconds; when it finds one, it asks that window to redraw and writes a line to the diagnostics log.

## Requirements

- Windows 10 or 11
- Google Chrome or Brave
- .NET 8 SDK, if you build it yourself

## Build and run

```
git clone https://github.com/Tonge0321/browser-wall.git
cd browser-wall
dotnet build BrowserWall.sln -c Release
dotnet run --project src/BrowserWall.App -c Release
```

Run the tests:

```
dotnet test BrowserWall.sln
```

The executable is not signed. If Smart App Control is on (Windows Security > App & browser control), Windows may block it.

## Configuration

The file is `%LOCALAPPDATA%\BrowserWall\config.json`. If it is missing on first run, the app creates one and tells you where it is. Edit it in Notepad, save, and restart the app.

```json
{
  "browserPath": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
  "profileDir": "C:\\Users\\<you>\\AppData\\Local\\BrowserWall\\profile",
  "language": "auto",
  "urls": [
    "https://www.google.com",
    "https://time.is",
    "https://www.youtube.com",
    "https://news.google.com"
  ]
}
```

| Field | Meaning |
|---|---|
| `browserPath` | Full path to the browser executable. Required. Write each `\` as `\\` in JSON. |
| `profileDir` | Browser data folder used only by the wall. Optional; defaults to `%LOCALAPPDATA%\BrowserWall\profile`. |
| `language` | `auto`, `en` or `zh-TW`. Optional; defaults to `auto`, which picks Traditional Chinese when the Windows display language is Chinese and English otherwise. |
| `urls` | 1 to 16 addresses, each starting with `http://` or `https://`. Required. When a saved page list exists, the wall opens that list instead. |

If the file has an error, the app names the field and the problem, then exits without changing your file.

The wall uses its own browser data folder and leaves your everyday Chrome profile alone. You will need to sign in to sites once inside the wall; after that the browser remembers you.

## Using it

1. On launch the wall maximizes on the main screen and opens each address in turn, waiting up to 10 seconds for each. A tile that fails shows "Failed to open" and the address.
2. Click a tile to expand it. Click "Back to grid", or click the wall's title bar while a tile is expanded, to return.
3. When the mouse is over a tile, two icons appear in its top-right corner: pop out (a box with a diagonal arrow) and ✕. Hold the mouse on an icon for about half a second to see a tooltip.
4. ✕ closes that browser window. The tile disappears and won't reopen next time. If the page asks "Leave site?" or similar, the tile expands after 2 seconds so you can answer.
5. Pop out moves the browser off the wall to the middle of the screen, and it shows on the taskbar again.
6. Pop-ups opened by a page and windows opened with Ctrl+N are not added automatically. A button "Add open window (N)" appears at the top right of the wall; click it and pick a window. Popped-out windows can be put back the same way.
7. If you drag a tab out while a tile is expanded, the expanded page stays in front. The new tile shows up after you go back to the grid.
8. The wall's minimize button, or clicking the wall on the taskbar while it is in front, sends the wall and all its browsers to the taskbar. Click again to bring everything back.
9. Closing the wall closes its browsers, and the app exits within 3 seconds. If the wall was killed last time, the next launch first closes the browser windows left behind.

## Where data is stored

Everything is under `%LOCALAPPDATA%\BrowserWall\`:

| File | Contents |
|---|---|
| `config.json` | Settings |
| `session.json` | The last page list, in tile order |
| `diagnostics.log` | Diagnostics in English: time, tile number, color or event, result. No titles, no URLs, no screenshots. Over 1 MB, the old file is renamed `diagnostics.old.log`. |
| `Preferences.backup.json`, `Preferences.backup.old.json` | Copies of Chrome's settings saved before the wall changed them (latest and previous) |
| `profile\` | The wall's browser data folder (default `profileDir`) |

`session.json` stores full URLs in plain text, query strings included. If a URL carries a sign-in token, that is saved too.

To go back to the addresses in the config file, delete `session.json` and start the app.

The wall needs to read full URLs (with `https://` and `www.`), so it turns on Chrome's "Always show full URLs" in the wall's own browser folder. If that option is already on, nothing changes; otherwise the wall saves a backup first. If Chrome's settings go wrong, close the wall, copy `Preferences.backup.json` (or `Preferences.backup.old.json` if that fails) to `<profileDir>\Default\Preferences` with no file extension, and start the wall again.

## Known limitations

- When a window has several tabs, only the visible tab is remembered and reopened.
- A new tab with no address typed has nothing to remember, so it won't come back next time.
- With 12 or more tiles that all redraw constantly, the wall gets less smooth: about 42 to 46 frames per second at 12 tiles, about 25 at 16. Ordinary pages don't redraw all the time.
- Only Chrome has been tested. Brave has not.
- When the config file can't be read at all, the system's own error text is shown as is, so it may not match the interface language.

## Code layout

- `src/BrowserWall.Core`: logic that doesn't call Windows APIs, such as layout math, tile state, and the rules for the config file and page list. The interface text for both languages lives here too.
- `src/BrowserWall.App`: the WPF window and all Windows API calls.
- `tests/BrowserWall.Tests`: xUnit tests, Core only.

## License

MIT. See [LICENSE](LICENSE).

## 中文說明

BrowserWall 把好幾個 Chrome（或 Brave）視窗排成一面牆，每一格都是即時畫面。點一格，那個瀏覽器就放大到整面牆，可以直接點、打字、捲動；按「回到格子」再縮回去。只支援 Windows。

介面語言跟著 Windows 的顯示語言：中文顯示繁體中文，其他語言顯示英文。想固定一種，在 `config.json` 加上 `"language": "zh-TW"` 或 `"language": "en"`。診斷紀錄一律是英文。

編譯、設定檔欄位、操作方式和資料位置都跟上面英文說明相同。幾個要注意的地方：

- 執行檔沒有數位簽章，開著「智慧型應用程式控制」時 Windows 可能會擋下它。
- 牆用專用的瀏覽器資料夾，不會動到你平常用的 Chrome，第一次使用要在牆裡重新登入網站。
- `session.json` 以明文存完整網址；網址帶登入代碼時也會一起存下來。想回到設定檔的網址，刪掉 `session.json` 再啟動。
- 牆會在專用資料夾裡打開 Chrome 的「一律顯示完整網址」，改之前先備份成 `Preferences.backup.json`。
- 目前只在 Chrome 上實際測過。
