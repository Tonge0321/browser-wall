using System.Windows;
using System.Windows.Interop;

namespace BrowserWall.App;

/// <summary>
/// The drag image while dragging a cell: this program's own semi-transparent small window, the same size as the dragged
/// cell, its center following the mouse. Live thumbnails are drawn above the wall's normal content, so the drag image is
/// a separate window placed above the wall, visible over any cell.
/// Ordered through SetWindowPos's insert-after position, not the topmost attribute; it does not take focus, does not
/// appear on the taskbar, does not receive the mouse (the wall captures it), and does not touch other programs' windows.
/// </summary>
public partial class DragGhostWindow : Window
{
    private IntPtr _hwnd;
    private int _widthPx;
    private int _heightPx;

    public DragGhostWindow()
    {
        InitializeComponent();
    }

    /// <summary>Drag starts: sets the size (physical pixels) and text. <see cref="MoveCenterTo"/> shows it.</summary>
    internal void Begin(int widthPx, int heightPx, double scale, string caption)
    {
        _widthPx = Math.Max(1, widthPx);
        _heightPx = Math.Max(1, heightPx);
        Width = _widthPx / scale;
        Height = _heightPx / scale;
        CaptionText.Text = caption;
    }

    /// <summary>Moves the drag image's center to screen coordinates (physical pixels) <paramref name="x"/>, <paramref name="y"/> and brings it to the front.</summary>
    internal void MoveCenterTo(int x, int y)
    {
        if (_hwnd == IntPtr.Zero)
        {
            _hwnd = new WindowInteropHelper(this).EnsureHandle();
        }

        if (!IsVisible)
        {
            Show(); // ShowActivated = false: does not take focus
        }

        int left = x - (_widthPx / 2);
        int top = y - (_heightPx / 2);
        WindowSystem.PlaceOnTop(_hwnd, new NativeMethods.RECT
        {
            Left = left,
            Top = top,
            Right = left + _widthPx,
            Bottom = top + _heightPx,
        });
    }

    /// <summary>Drag ends: hide. Does nothing when not shown.</summary>
    internal void End()
    {
        if (IsVisible)
        {
            Hide();
        }
    }
}
