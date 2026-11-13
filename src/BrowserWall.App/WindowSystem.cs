using System.Text;
using BrowserWall.Core.Detect;
using BrowserWall.Core.Layout;

namespace BrowserWall.App;

/// <summary>Enumerating, reading and parking windows. All Windows API calls go through <see cref="NativeMethods"/>.</summary>
internal static class WindowSystem
{
    /// <summary>Handles of all top-level windows in enumeration order (Z order, top to bottom).</summary>
    public static List<IntPtr> EnumTopLevel()
    {
        var list = new List<IntPtr>();
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            list.Add(hWnd);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Handles of all visible top-level windows (recorded before a launch).</summary>
    public static List<long> VisibleTopLevelIds() =>
        EnumTopLevel().Where(NativeMethods.IsWindowVisible).Select(h => h.ToInt64()).ToList();

    /// <summary>Data of all visible top-level windows (including the owning process id), for new-window matching and the add list.</summary>
    public static List<WindowInfo> VisibleTopLevelInfos()
    {
        var result = new List<WindowInfo>();
        foreach (IntPtr hWnd in EnumTopLevel())
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
            {
                continue;
            }

            result.Add(new WindowInfo(
                hWnd.ToInt64(), GetExecutablePath(hWnd), GetClassName(hWnd), GetTitle(hWnd), true, GetProcessId(hWnd)));
        }

        return result;
    }

    /// <summary>
    /// Lightweight enumeration for drag-out watching (every 50 ms): all visible top-level windows and their process ids.
    /// Only windows of <paramref name="wallPid"/> also get their class name and bounds read; other processes get an empty
    /// class and zero bounds (Core will not add them because the process differs; they only need to be recorded as "seen").
    /// Reads system data only, sends no messages to those windows and does not wait for them; opens no processes and reads
    /// no titles. <paramref name="existingIds"/> are all top-level windows of the same enumeration (including invisible
    /// ones), so Core can drop the "seen" record of windows that no longer exist.
    /// </summary>
    public static List<DragOutWindow> DragOutWindows(uint? wallPid, out List<long> existingIds)
    {
        var result = new List<DragOutWindow>();
        List<IntPtr> all = EnumTopLevel();
        existingIds = all.Select(h => h.ToInt64()).ToList();
        foreach (IntPtr hWnd in all)
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
            {
                continue;
            }

            uint? pid = GetProcessId(hWnd);
            string cls = string.Empty;
            var bounds = new CellRect(0, 0, 0, 0);
            if (wallPid is uint wall && pid == wall)
            {
                cls = GetClassName(hWnd);
                if (NativeMethods.GetWindowRect(hWnd, out NativeMethods.RECT r))
                {
                    bounds = new CellRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                }
            }

            result.Add(new DragOutWindow(new WindowInfo(hWnd.ToInt64(), null, cls, string.Empty, true, pid), bounds));
        }

        return result;
    }

    /// <summary>
    /// Places one of this program's own windows (for example the "wall is full" notice) at <paramref name="r"/> and brings it to the front.
    /// Uses SetWindowPos's insert-after position, not the topmost attribute; does not take focus or touch other programs' windows.
    /// </summary>
    public static void PlaceOnTop(IntPtr own, NativeMethods.RECT r)
    {
        NativeMethods.SetWindowPos(own, NativeMethods.HWND_TOP, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOOWNERZORDER);
    }

    /// <summary>The window's owning process id; null when unreadable (the window no longer exists).</summary>
    public static uint? GetProcessId(IntPtr hWnd)
    {
        NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
        return pid == 0 ? null : pid;
    }

    public static string GetClassName(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        int n = NativeMethods.GetClassName(hWnd, sb, sb.Capacity);
        return n > 0 ? sb.ToString(0, n) : string.Empty;
    }

    public static string GetTitle(IntPtr hWnd)
    {
        int length = NativeMethods.GetWindowTextLength(hWnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(length + 1);
        int n = NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
        return n > 0 ? sb.ToString(0, Math.Min(n, sb.Length)) : string.Empty;
    }

    /// <summary>Full executable path of the window's owning process; null when unreadable.</summary>
    public static string? GetExecutablePath(IntPtr hWnd)
    {
        NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
        if (pid == 0)
        {
            return null;
        }

        IntPtr process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            return NativeMethods.QueryFullProcessImageName(process, 0, sb, ref size)
                ? sb.ToString(0, (int)size)
                : null;
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    /// <summary>The wall area: the wall's client area in screen coordinates (physical pixels).</summary>
    public static NativeMethods.RECT GetWallScreenRect(IntPtr wall)
    {
        NativeMethods.GetClientRect(wall, out NativeMethods.RECT client);
        var origin = new NativeMethods.POINT { X = 0, Y = 0 };
        NativeMethods.ClientToScreen(wall, ref origin);
        return new NativeMethods.RECT
        {
            Left = origin.X,
            Top = origin.Y,
            Right = origin.X + (client.Right - client.Left),
            Bottom = origin.Y + (client.Bottom - client.Top),
        };
    }

    /// <summary>
    /// Park: not minimized; size and position equal the wall area <paramref name="r"/>; Z order right below the wall. Does not take focus.
    /// <paramref name="r"/> comes from the caller (the last valid wall area while the wall is minimized).
    /// </summary>
    public static void Park(IntPtr window, IntPtr wall, NativeMethods.RECT r)
    {
        if (!NativeMethods.IsWindow(window))
        {
            return;
        }

        RestoreToNormal(window);
        NativeMethods.SetWindowPos(window, wall, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
    }

    /// <summary>
    /// Expand: size and position equal the wall area <paramref name="r"/>, brought to the top with foreground focus. The caller then orders the button and the wall with <see cref="ArrangeExpanded"/>.
    /// </summary>
    public static void Expand(IntPtr window, NativeMethods.RECT r)
    {
        if (!NativeMethods.IsWindow(window))
        {
            return;
        }

        RestoreToNormal(window);
        NativeMethods.SetWindowPos(window, NativeMethods.HWND_TOP, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
            NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOOWNERZORDER);
        NativeMethods.SetForegroundWindow(window);
    }

    /// <summary>Restores a minimized window to normal without taking focus.</summary>
    public static void Restore(IntPtr window)
    {
        if (NativeMethods.IsWindow(window) && NativeMethods.IsIconic(window))
        {
            NativeMethods.ShowWindow(window, NativeMethods.SW_SHOWNOACTIVATE);
        }
    }

    /// <summary>
    /// Order when expanded, all set through SetWindowPos's insert-after position, not the topmost attribute:
    /// the button right above the expanded window → the expanded window right after the button → the wall right after the expanded window.
    /// </summary>
    public static void ArrangeExpanded(IntPtr button, IntPtr window, IntPtr wall)
    {
        if (!NativeMethods.IsWindow(window))
        {
            return;
        }

        const uint keep = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE
            | NativeMethods.SWP_NOOWNERZORDER;

        IntPtr prev = NativeMethods.GetWindow(window, NativeMethods.GW_HWNDPREV);
        if (prev != button)
        {
            NativeMethods.SetWindowPos(button, prev == IntPtr.Zero ? NativeMethods.HWND_TOP : prev, 0, 0, 0, 0, keep);
        }

        NativeMethods.SetWindowPos(window, button, 0, 0, 0, 0, keep);
        NativeMethods.SetWindowPos(wall, window, 0, 0, 0, 0, keep);
    }

    /// <summary>
    /// Windows still present after the wall closes: restore from minimized or maximized and place them within the wall area, without changing Z order or closing them.
    /// Never waits for the other side: ShowWindowAsync and SWP_ASYNCWINDOWPOS; IsWindow / IsIconic / IsZoomed only read.
    /// When <paramref name="wallRect"/> is null (no valid wall area was ever obtained), only restore without moving.
    /// </summary>
    public static void ReleaseOnExitAsync(IntPtr window, NativeMethods.RECT? wallRect)
    {
        if (!NativeMethods.IsWindow(window))
        {
            return;
        }

        if (NativeMethods.IsIconic(window) || NativeMethods.IsZoomed(window))
        {
            NativeMethods.ShowWindowAsync(window, NativeMethods.SW_SHOWNOACTIVATE);
        }

        if (wallRect is NativeMethods.RECT r)
        {
            NativeMethods.SetWindowPos(window, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
                NativeMethods.SWP_ASYNCWINDOWPOS | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE
                | NativeMethods.SWP_NOOWNERZORDER);
        }
    }

    /// <summary>
    /// Sends a "close window" request (same as pressing ✕). PostMessage does not wait for the other side to respond,
    /// and the process is never forcibly ended.
    /// </summary>
    public static void RequestClose(IntPtr window)
    {
        if (NativeMethods.IsWindow(window))
        {
            NativeMethods.PostMessage(window, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
    }

    /// <summary>Minimizes a managed window without taking focus when the wall is minimized. Does not wait for the other side.</summary>
    public static void Minimize(IntPtr window)
    {
        if (NativeMethods.IsWindow(window) && !NativeMethods.IsIconic(window))
        {
            NativeMethods.ShowWindowAsync(window, NativeMethods.SW_SHOWMINNOACTIVE);
        }
    }

    /// <summary>
    /// Non-waiting park: size and position equal the wall area, Z order right below the wall, does not take focus.
    /// </summary>
    public static void ParkAsync(IntPtr window, IntPtr wall, NativeMethods.RECT r)
    {
        if (!NativeMethods.IsWindow(window))
        {
            return;
        }

        if (NativeMethods.IsZoomed(window))
        {
            NativeMethods.ShowWindowAsync(window, NativeMethods.SW_SHOWNOACTIVATE);
        }

        NativeMethods.SetWindowPos(window, wall, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER | NativeMethods.SWP_ASYNCWINDOWPOS);
    }

    /// <summary>
    /// For the nudge: changes only position and size, not Z order; does not take focus or wait for the other side.
    /// Minimized windows are left alone (they are parked again when the wall restores). Returns whether it was really sent (false when the window is gone, minimized, or SetWindowPos fails).
    /// </summary>
    public static bool SetRectAsync(IntPtr window, CellRect r)
    {
        if (!NativeMethods.IsWindow(window) || NativeMethods.IsIconic(window))
        {
            return false;
        }

        return NativeMethods.SetWindowPos(window, IntPtr.Zero, r.X, r.Y, r.Width, r.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER
            | NativeMethods.SWP_ASYNCWINDOWPOS);
    }

    /// <summary>Non-waiting restore: restores a minimized window to normal without taking focus.</summary>
    public static void RestoreAsync(IntPtr window)
    {
        if (NativeMethods.IsWindow(window) && NativeMethods.IsIconic(window))
        {
            NativeMethods.ShowWindowAsync(window, NativeMethods.SW_SHOWNOACTIVATE);
        }
    }

    /// <summary>
    /// Non-waiting expand (deferred action after the wall restores): restore, bring to the top, size equal to the wall area, then take the foreground.
    /// Called only from deferred actions, never at the moment the wall becomes active.
    /// </summary>
    public static void ExpandAsync(IntPtr window, NativeMethods.RECT r)
    {
        if (!NativeMethods.IsWindow(window))
        {
            return;
        }

        if (NativeMethods.IsIconic(window) || NativeMethods.IsZoomed(window))
        {
            NativeMethods.ShowWindowAsync(window, NativeMethods.SW_SHOWNOACTIVATE);
        }

        NativeMethods.SetWindowPos(window, NativeMethods.HWND_TOP, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
            NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOOWNERZORDER | NativeMethods.SWP_ASYNCWINDOWPOS);
        NativeMethods.SetForegroundWindow(window);
    }

    /// <summary>
    /// Non-waiting expanded order: SetWindowPos on the browser window adds SWP_ASYNCWINDOWPOS;
    /// the button and the wall are our own windows and stay as before.
    /// </summary>
    public static void ArrangeExpandedAsync(IntPtr button, IntPtr window, IntPtr wall)
    {
        if (!NativeMethods.IsWindow(window))
        {
            return;
        }

        const uint keep = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE
            | NativeMethods.SWP_NOOWNERZORDER;

        IntPtr prev = NativeMethods.GetWindow(window, NativeMethods.GW_HWNDPREV);
        if (prev != button)
        {
            NativeMethods.SetWindowPos(button, prev == IntPtr.Zero ? NativeMethods.HWND_TOP : prev, 0, 0, 0, 0, keep);
        }

        NativeMethods.SetWindowPos(window, button, 0, 0, 0, 0, keep | NativeMethods.SWP_ASYNCWINDOWPOS);
        NativeMethods.SetWindowPos(wall, window, 0, 0, 0, 0, keep);
    }

    /// <summary>
    /// Whether the user is holding the wall's title bar minimize or close button: the left button is down and the wall's
    /// hit test at the cursor is HTMINBUTTON or HTCLOSE. WM_NCHITTEST goes to the wall itself (same thread), not to other processes' windows.
    /// </summary>
    public static bool IsCaptionButtonAt(IntPtr wall)
    {
        if (!IsLeftButtonDown() || !NativeMethods.GetCursorPos(out NativeMethods.POINT p))
        {
            return false;
        }

        IntPtr lParam = new IntPtr((p.Y << 16) | (p.X & 0xFFFF));
        int hit = NativeMethods.SendMessage(wall, NativeMethods.WM_NCHITTEST, IntPtr.Zero, lParam).ToInt32();
        return hit is NativeMethods.HTMINBUTTON or NativeMethods.HTCLOSE;
    }

    /// <summary>Whether the left mouse button is down right now (read only).</summary>
    public static bool IsLeftButtonDown() => (NativeMethods.GetAsyncKeyState(NativeMethods.VK_LBUTTON) & 0x8000) != 0;

    /// <summary>
    /// Pop out of the wall: restore from minimized or maximized, center on the wall area at 80% of its width and height, bring to the front with focus. The window is not closed.
    /// </summary>
    public static void ReleaseToCenter(IntPtr window, NativeMethods.RECT wallRect)
    {
        if (!NativeMethods.IsWindow(window))
        {
            return;
        }

        Restore(window);
        RestoreToNormal(window);

        int wallWidth = wallRect.Right - wallRect.Left;
        int wallHeight = wallRect.Bottom - wallRect.Top;
        int width = wallWidth * 4 / 5;
        int height = wallHeight * 4 / 5;
        int left = wallRect.Left + (wallWidth - width) / 2;
        int top = wallRect.Top + (wallHeight - height) / 2;

        NativeMethods.SetWindowPos(window, NativeMethods.HWND_TOP, left, top, width, height,
            NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOOWNERZORDER);
        NativeMethods.SetForegroundWindow(window);
    }

    /// <summary>Handles of all top-level windows, top to bottom (including hidden ones).</summary>
    public static List<long> ZOrder() => EnumTopLevel().Select(h => h.ToInt64()).ToList();

    /// <summary>SetWindowPos sizes do not apply while maximized; restore to normal first (without taking focus or minimizing).</summary>
    private static void RestoreToNormal(IntPtr window)
    {
        if (NativeMethods.IsZoomed(window))
        {
            NativeMethods.ShowWindow(window, NativeMethods.SW_SHOWNOACTIVATE);
        }
    }
}
