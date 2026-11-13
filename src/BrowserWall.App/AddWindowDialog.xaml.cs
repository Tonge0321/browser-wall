using System.Windows;
using System.Windows.Threading;
using BrowserWall.Core;
using BrowserWall.Core.Detect;
using BrowserWall.Core.Text;

namespace BrowserWall.App;

/// <summary>
/// The "Add open window" list: picking a row adds it. While open, candidates are fetched again every second;
/// closed or already added windows disappear; when the list becomes empty, "no windows to add" is shown.
/// Display only; <see cref="AddPicker"/> filters the list, and MainWindow rechecks before actually adding.
/// </summary>
public partial class AddWindowDialog : Window
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    private readonly Func<IReadOnlyList<WindowInfo>> _candidates;
    private readonly Func<bool> _slotsFull;
    private readonly DispatcherTimer _refresh;
    private readonly string _emptyHintText;
    private readonly string _fullText;
    private bool _updating;

    /// <param name="candidates">Gets the open windows that can be added now (<see cref="AddPicker.Candidates"/>).</param>
    /// <param name="slotsFull">Whether the wall's cells have reached the limit (failed cells included): when full, picking is refused and the user is told to pop one out first.</param>
    /// <param name="lang">UI language for the title, hint, explanation and button.</param>
    internal AddWindowDialog(Func<IReadOnlyList<WindowInfo>> candidates, Func<bool> slotsFull, UiLanguage lang)
    {
        InitializeComponent();
        Title = UiStrings.Get(lang, UiTextKey.AddDialogTitle);
        CloseButton.Content = UiStrings.Get(lang, UiTextKey.Close);
        _emptyHintText = UiStrings.Get(lang, UiTextKey.AddDialogEmpty);
        _fullText = UiStrings.Format(lang, UiTextKey.AddFull, WallLimits.MaxCells);
        _candidates = candidates;
        _slotsFull = slotsFull;
        Refresh();

        _refresh = new DispatcherTimer { Interval = RefreshInterval };
        _refresh.Tick += (_, _) => Refresh();
        _refresh.Start();
        Closed += (_, _) => _refresh.Stop();
    }

    /// <summary>Handle of the picked window; null when none was picked.</summary>
    public long? SelectedWindowId { get; private set; }

    /// <summary>Fetches candidates again and updates the list (titles are only displayed as text, never interpreted).</summary>
    private void Refresh()
    {
        IReadOnlyList<WindowInfo> list = _candidates();
        _updating = true;
        try
        {
            WindowList.ItemsSource = list;
            WindowList.SelectedItem = null;
        }
        finally
        {
            _updating = false;
        }

        bool empty = list.Count == 0;
        WindowList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyHint.Text = _emptyHintText;
        EmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ProblemText.Text = _slotsFull() ? _fullText : string.Empty;
    }

    /// <summary>Picking a row adds it; no extra button press or double-click needed.</summary>
    private void OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_updating || WindowList.SelectedItem is not WindowInfo w)
        {
            return;
        }

        if (_slotsFull())
        {
            ProblemText.Text = _fullText;
            WindowList.SelectedItem = null;
            return;
        }

        SelectedWindowId = w.Id;
        DialogResult = true;
    }
}
