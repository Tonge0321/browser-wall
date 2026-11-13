using System.Windows;
using System.Windows.Interop;
using BrowserWall.Core.Text;

namespace BrowserWall.App;

/// <summary>
/// The "Back to grid" button: a separate small window, 120×32 physical pixels, its right edge 160 pixels left of the
/// wall's right edge and its top at the wall's top. No owner, and no topmost attribute.
/// </summary>
public partial class ShrinkButtonWindow : Window
{
    public const int WidthPx = 120;
    public const int HeightPx = 32;
    public const int RightInsetPx = 160;

    private IntPtr _hwnd;

    public ShrinkButtonWindow(UiLanguage lang)
    {
        InitializeComponent();
        string text = UiStrings.Get(lang, UiTextKey.BackToGrid);
        Title = text;
        ShrinkButton.Content = text;
        ShrinkButton.Click += (_, _) => ShrinkRequested?.Invoke();
    }

    /// <summary>The button was pressed.</summary>
    public event Action? ShrinkRequested;

    /// <summary>Handle of the button window (created on first call, but not shown).</summary>
    public IntPtr Handle
    {
        get
        {
            if (_hwnd == IntPtr.Zero)
            {
                _hwnd = new WindowInteropHelper(this).EnsureHandle();
            }

            return _hwnd;
        }
    }

    /// <summary>Positions it from the wall (screen coordinates, physical pixels) and shows it without taking focus.</summary>
    internal void ShowAt(NativeMethods.RECT wall)
    {
        IntPtr hwnd = Handle;
        if (!IsVisible)
        {
            Show(); // ShowActivated = false: does not take focus
        }

        int left = wall.Right - RightInsetPx - WidthPx;
        int top = wall.Top;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOP, left, top, WidthPx, HeightPx,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }
}
