using BrowserWall.Core.Detect;
using BrowserWall.Core.State;

namespace BrowserWall.Tests;

public class AddPickerTests
{
    private const string Chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
    private const string Cls = WindowDetector.BrowserWindowClass;
    private const uint WallPid = 1000;

    [Fact(DisplayName = "Pick_OnlyWallBrowserProcess")]
    public void OnlyWallBrowserProcess()
    {
        var windows = new[]
        {
            new WindowInfo(1, @"C:\Users\user\AppData\Local\Programs\ChatGPT\ChatGPT.exe", Cls, "ChatGPT", true, 2000),
            new WindowInfo(2, Chrome, Cls, "Wall new window", true, WallPid),
            new WindowInfo(3, @"C:\Users\user\AppData\Local\AnthropicClaude\claude.exe", Cls, "Claude", true, 3000),
            new WindowInfo(4, Chrome, Cls, "Everyday Chrome", true, 4000),
            new WindowInfo(5, Chrome, Cls, "Unknown process", true, null),
        };

        IReadOnlyList<WindowInfo> picked = AddPicker.Candidates(windows, WallPid, Array.Empty<long>());

        Assert.Equal(new long[] { 2 }, picked.Select(w => w.Id));

        // No wall browser process (every URL failed to open) → empty list
        Assert.Empty(AddPicker.Candidates(windows, null, Array.Empty<long>()));
    }

    [Fact(DisplayName = "Pick_ExcludesManagedWindows")]
    public void ExcludesManagedWindows()
    {
        var windows = new[]
        {
            new WindowInfo(10, Chrome, Cls, "Managed A", true, WallPid),
            new WindowInfo(11, Chrome, Cls, "New window B", true, WallPid),
            new WindowInfo(12, Chrome, Cls, "Managed C", true, WallPid),
            new WindowInfo(13, Chrome, Cls, "New window D", true, WallPid),
            new WindowInfo(14, Chrome, Cls, "Invisible", false, WallPid),
            new WindowInfo(15, Chrome, Cls, "   ", true, WallPid),
            new WindowInfo(16, Chrome, "Chrome_RenderWidgetHostHWND", "Other class", true, WallPid),
        };

        IReadOnlyList<WindowInfo> picked = AddPicker.Candidates(windows, WallPid, new long[] { 10, 12 });

        Assert.Equal(new long[] { 11, 13 }, picked.Select(w => w.Id));
    }

    [Fact(DisplayName = "Pick_ClosedWhileListOpen_Skipped")]
    public void ClosedWhileListOpen_Skipped()
    {
        var whenOpened = new[]
        {
            new WindowInfo(21, Chrome, Cls, "A", true, WallPid),
            new WindowInfo(22, Chrome, Cls, "B", true, WallPid),
        };
        Assert.Equal(new long[] { 21, 22 }, AddPicker.Candidates(whenOpened, WallPid, Array.Empty<long>()).Select(w => w.Id));

        // A is closed while the list is open
        var afterClose = new[] { new WindowInfo(22, Chrome, Cls, "B", true, WallPid) };
        Assert.False(AddPicker.IsStillCandidate(21, afterClose, WallPid, Array.Empty<long>()));
        Assert.True(AddPicker.IsStillCandidate(22, afterClose, WallPid, Array.Empty<long>()));

        // Already became managed
        Assert.False(AddPicker.IsStillCandidate(22, afterClose, WallPid, new long[] { 22 }));

        // The window handle was reused by another process's window
        var reused = new[] { new WindowInfo(22, Chrome, Cls, "Everyday Chrome", true, 4000) };
        Assert.False(AddPicker.IsStillCandidate(22, reused, WallPid, Array.Empty<long>()));

        // No wall browser process
        Assert.False(AddPicker.IsStillCandidate(22, afterClose, null, Array.Empty<long>()));
    }

    [Fact(DisplayName = "Pick_WhileLaunching_AddDisabled")]
    public void WhileLaunching_AddDisabled()
    {
        // 16 URLs configured, 15 registered, the 16th still pending: no adding during startup
        var state = new WallState();
        for (long id = 1; id <= 15; id++)
        {
            state.AddWindow(id, $"https://example.com/{id}");
        }

        Assert.False(AddPicker.CanAdd(state, launching: true));

        // The 16th times out: the failed cell registers normally and does not fail because cells are full
        state.AddFailed("https://example.com/16");
        Assert.Equal(16, state.Slots.Count);

        // Adding is possible only after startup ends (15 managed, below 16)
        Assert.True(AddPicker.CanAdd(state, launching: false));

        // Also disabled during startup when there are no windows
        Assert.False(AddPicker.CanAdd(new WallState(), launching: true));
        Assert.True(AddPicker.CanAdd(new WallState(), launching: false));
    }

    [Fact(DisplayName = "Pick_FullWallDisablesAdd")]
    public void FullWallDisablesAdd()
    {
        var state = new WallState();
        for (long id = 1; id <= 15; id++)
        {
            state.AddWindow(id, $"https://example.com/{id}");
        }

        Assert.True(AddPicker.CanAdd(state));

        state.AdoptWindow(16);
        Assert.Equal(16, state.WindowIds.Count);
        Assert.False(AddPicker.CanAdd(state));

        // Disabled while expanded or the wall is minimized
        var small = new WallState();
        small.AddWindow(1, "https://example.com/1");
        small.ClickCell(0);
        Assert.False(AddPicker.CanAdd(small));
        small.Shrink();
        Assert.True(AddPicker.CanAdd(small));
        small.WallMinimized();
        Assert.False(AddPicker.CanAdd(small));
        small.WallRestored();
        Assert.True(AddPicker.CanAdd(small));

        // 15 managed + 1 failed cell: managed is below 16, so the button is usable;
        // but the cells are full, so adding an open window does nothing and does not push out the failed cell
        var withFailed = new WallState();
        withFailed.AddFailed("https://failed.example.com");
        for (long id = 1; id <= 15; id++)
        {
            withFailed.AddWindow(id, $"https://example.com/{id}");
        }

        Assert.Equal(16, withFailed.Slots.Count);
        Assert.True(AddPicker.CanAdd(withFailed));

        Assert.Empty(withFailed.AdoptWindow(20));
        Assert.Equal(16, withFailed.Slots.Count);
        Assert.Equal(WallSlot.ForFailed("https://failed.example.com"), withFailed.Slots[0]);
        Assert.Equal(Enumerable.Range(1, 15).Select(i => (long)i), withFailed.WindowIds);
        Assert.True(AddPicker.CanAdd(withFailed));
    }
}
