using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using BrowserWall.Core.Text;

namespace BrowserWall.App;

/// <summary>
/// The "wall is full" and "could not add page" notice: this program's own small window, shown centered at the top of the wall for 3 seconds.
/// Visible in grid and expanded mode: placed in front through SetWindowPos's insert-after position (in expanded mode above the
/// "Back to grid" button, without breaking the button → expanded window → wall adjacency), not through the topmost attribute.
/// It does not take focus, does not appear on the taskbar, and does not touch other programs' windows.
/// </summary>
public partial class NoticeWindow : Window
{
    /// <summary>How long it is shown.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(3);

    /// <summary>Size and distance from the top of the wall, in logical pixels.</summary>
    private const double WidthDip = 360;
    private const double HeightDip = 40;
    private const double TopInsetDip = 8;

    private readonly DispatcherTimer _hideTimer;
    private readonly string _fullText;
    private IntPtr _hwnd;

    /// <param name="lang">UI language; the default message is "wall is full" (<see cref="UiTextKey.WallFull"/>).</param>
    public NoticeWindow(UiLanguage lang)
    {
        InitializeComponent();
        _fullText = UiStrings.Get(lang, UiTextKey.WallFull);
        MessageText.Text = _fullText;
        _hideTimer = new DispatcherTimer { Interval = Duration };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };
    }

    /// <summary>
    /// Shows it centered at the top of the wall (screen coordinates, physical pixels) and brings it to the front; <paramref name="scale"/> is the wall's DPI scale.
    /// When already shown, the timer restarts (repeated triggers only extend it, never overlap).
    /// </summary>
    internal void ShowOver(NativeMethods.RECT wall, double scale, string? text = null)
    {
        MessageText.Text = text ?? _fullText;
        if (_hwnd == IntPtr.Zero)
        {
            _hwnd = new WindowInteropHelper(this).EnsureHandle();
        }

        if (!IsVisible)
        {
            Show(); // ShowActivated = false: does not take focus
        }

        int width = (int)Math.Round(WidthDip * scale);
        int height = (int)Math.Round(HeightDip * scale);
        int left = wall.Left + ((wall.Right - wall.Left - width) / 2);
        int top = wall.Top + (int)Math.Round(TopInsetDip * scale);
        WindowSystem.PlaceOnTop(_hwnd, new NativeMethods.RECT
        {
            Left = left,
            Top = top,
            Right = left + width,
            Bottom = top + height,
        });

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    /// <summary>When the wall closes: stop the timer and close.</summary>
    internal void Shutdown()
    {
        _hideTimer.Stop();
        Close();
    }
}
