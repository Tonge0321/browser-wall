using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using BrowserWall.Core;
using BrowserWall.Core.Config;
using BrowserWall.Core.Detect;
using BrowserWall.Core.Health;
using BrowserWall.Core.Infra;
using BrowserWall.Core.Layout;
using BrowserWall.Core.Session;
using BrowserWall.Core.State;
using BrowserWall.Core.Text;

namespace BrowserWall.App;

/// <summary>
/// The wall: cells, DWM thumbnails, titles, failed cells, "no windows"; click a cell to expand, shrink, detect closing and minimizing, and exit.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Title bar height (DIP; about the height of a Windows title bar button).</summary>
    private const double TitleHeightDip = 30;

    /// <summary>Height of the toolbar at the top of the wall (DIP, same as the first row in MainWindow.xaml). Cells sit below the toolbar.</summary>
    private const double ToolbarHeightDip = 32;

    /// <summary>Width of the "Pop out" and ✕ buttons on the right of each title bar (DIP; about the width of a Windows title bar button).</summary>
    private const double TitleButtonWidthDip = 46;

    /// <summary>How long a title bar icon's tooltip stays while the mouse rests on it (avoids WPF's default 5-second auto-hide; it still disappears at once when the mouse leaves).</summary>
    private const int TooltipShowDurationMs = 60_000;

    /// <summary>Cell border thickness (DIP; a 1-pixel light border).</summary>
    private const double BorderDip = 1;

    /// <summary>How long after "Close" a window that still exists gets expanded.</summary>
    private static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(2);

    /// <summary>Maximum wait for taskbar calls to finish when the wall closes (exit must finish within 3 seconds).</summary>
    private static readonly TimeSpan ExitTaskbarWait = TimeSpan.FromMilliseconds(500);

    private static readonly Brush CellBorderBrush = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));

    /// <summary>Title bar height, button width and border of the latest layout (physical pixels), for the drag area check.</summary>
    private int _titleHeightPx;
    private int _buttonWidthPx;
    private int _borderPx;

    /// <summary>"Pop out" and ✕ of each title bar (in cell order); shown only on the cell under the mouse.</summary>
    private readonly List<(Button? Release, Button? Close)> _cellButtons = new();

    /// <summary>When closing the wall started (exit must finish within 3 seconds; used to compute how long to wait for the diagnostics log).</summary>
    private readonly System.Diagnostics.Stopwatch _exitClock = new();

    /// <summary>Diagnostics log.</summary>
    private readonly DiagnosticsLog _diagnostics = DiagnosticsLog.ForDefaultLocation();

    /// <summary>Flat-color check (background threads).</summary>
    private readonly FlatChecker _flat;

    /// <summary>Detection interval: 0.5 seconds, to guarantee "within 1 second".</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(500);

    private static readonly Brush CellBackground = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10));
    private static readonly Brush FailedBackground = new SolidColorBrush(Color.FromRgb(0x3A, 0x1E, 0x1E));
    private static readonly Brush TitleForeground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));

    private readonly WallConfig _config;
    private readonly string _configPath;

    /// <summary>URLs to open at startup (the last page list or the config file's urls).</summary>
    private readonly IReadOnlyList<string> _startupUrls;

    /// <summary>The last page list: synced in <see cref="Apply"/> after every cell change.</summary>
    private readonly SessionTracker _session;
    private readonly WallState _state = new();
    private readonly Dictionary<long, string> _titles = new();

    /// <summary>Title text controls of the managed windows currently on screen (text updated directly every second).</summary>
    private readonly Dictionary<long, TextBlock> _titleBlocks = new();

    /// <summary>Thumbnail areas of the managed windows currently on screen (placed again every second; the source size may change).</summary>
    private readonly Dictionary<long, CellRect> _thumbnailAreas = new();
    private readonly DispatcherTimer _checkTimer;
    private readonly ShrinkButtonWindow _button;

    /// <summary>UI language decided once at startup; all user-facing text comes from the string table in this language.</summary>
    private readonly UiLanguage _lang;

    /// <summary>Callback for foreground events; the reference is kept so it is not collected.</summary>
    private readonly NativeMethods.WinEventProc _foregroundProc;

    private IntPtr _hwnd;
    private IntPtr _foregroundHook;

    /// <summary>
    /// Process id of the wall's browser: taken when the first managed window is found, then never changed or cleared.
    /// Null when every URL failed to open.
    /// </summary>
    private uint? _wallProcessId;
    private Thumbnails? _thumbnails;
    private GridResult? _layout;
    private NativeMethods.RECT? _lastWallRect;
    private double _scale = 1;
    private int _tickCount;
    private bool _started;
    private bool _launching;

    /// <summary>"+ Add page" is still looking for its window (up to 10 seconds); the button is disabled meanwhile, one at a time.</summary>
    private bool _addingPage;

    /// <summary>Delay after the wall becomes active or restores (for example 300 ms).</summary>
    private static readonly TimeSpan DeferDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>While a title bar button is pressed, the interval for checking whether the mouse was released.</summary>
    private static readonly TimeSpan CaptionPollInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>Kind of deferred action. Actions are generated again from the current state when it runs; old actions are not kept.</summary>
    private enum DeferredKind
    {
        None,

        /// <summary>Parking after the wall becomes active: only the scheduled targets are recorded.</summary>
        Park,

        /// <summary>The wall restored from minimized: generated by the state machine's SettleRestore from the current state.</summary>
        Restore,
    }

    /// <summary>How the App runs actions.</summary>
    private enum ApplyMode
    {
        /// <summary>Normal operation (cell clicks, buttons, detected closes, etc.).</summary>
        Normal,

        /// <summary>The moment the wall becomes active: only the wall's own windows move; parking goes into a deferred action.</summary>
        Activation,

        /// <summary>Deferred action: never waits for other processes' windows to respond.</summary>
        Deferred,
    }

    private readonly DispatcherTimer _deferredTimer;
    private readonly DispatcherTimer _captionPoll;
    private DeferredKind _deferredKind = DeferredKind.None;
    private List<long> _deferredParkIds = new();

    /// <summary>The exit flow has started: from now on the wall and managed windows are not operated on; only the exit flow runs.</summary>
    private bool _closed;

    /// <summary>The exit flow is done; the following close is let through.</summary>
    private bool _exitDone;

    /// <summary>Removing and adding back taskbar buttons.</summary>
    private readonly Taskbar _taskbar;

    /// <summary>Snapshot of visible top-level windows before the first URL launch; cleared after leftover windows are handled.</summary>
    private List<WindowInfo>? _launchSnapshot;

    /// <summary>Watches for tabs dragged out to join the wall automatically: polls every half of <see cref="DragOutWatcher.MaxPollInterval"/>.</summary>
    private readonly DispatcherTimer _dragOutTimer;

    /// <summary>Time source for drag-out watching (monotonic).</summary>
    private readonly System.Diagnostics.Stopwatch _dragOutClock = new();

    /// <summary>Reads the address bars (background thread).</summary>
    private readonly AddressBarReader _addressBar;

    /// <summary>Rules for recording the last seen URL (2-second interval per cell).</summary>
    private readonly LastSeenUrl _lastSeen = new();

    /// <summary>Monotonic time for recording URLs.</summary>
    private readonly System.Diagnostics.Stopwatch _urlClock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>The "wall is full" notice.</summary>
    private readonly NoticeWindow _notice;

    /// <summary>Dragged-out window decisions (Core); created before the wall launches the browser, so windows already present never count as new.</summary>
    private DragOutWatcher? _dragOut;

    /// <summary>Drag-to-swap gesture for cells (Core).</summary>
    private readonly CellDragGesture _drag = new();

    /// <summary>Semi-transparent drag image of the dragged cell.</summary>
    private readonly DragGhostWindow _ghost = new();

    /// <summary>Width (logical pixels) and color of the target cell's frame.</summary>
    private const double DragFrameDip = 3;
    private static readonly Brush DragFrameBrush = new SolidColorBrush(Color.FromRgb(0x4F, 0xA3, 0xFF));

    /// <summary>Background blocks of failed cells by cell index (the source is made semi-transparent while dragging). Rebuilt on every <see cref="Render"/>.</summary>
    private readonly Dictionary<int, UIElement> _failedCellElements = new();

    /// <summary>Drag visuals are set up (drag image shown, source semi-transparent).</summary>
    private bool _dragVisualsActive;

    /// <summary>The dragged managed window (skipped by the per-second thumbnail update); null for a failed cell or when not dragging.</summary>
    private long? _dragSourceId;

    /// <summary>Index of the currently framed target cell; -1 when none.</summary>
    private int _dragTargetIndex = -1;

    /// <summary>The currently framed target managed window (skipped by the per-second thumbnail update); null when none.</summary>
    private long? _dragTargetId;

    /// <summary>Frame around the target cell.</summary>
    private Border? _dragFrame;

    internal MainWindow(WallConfig config, string configPath, IReadOnlyList<string> startupUrls, SessionTracker session, UiLanguage lang)
    {
        InitializeComponent();
        _lang = lang;
        _button = new ShrinkButtonWindow(lang);
        _notice = new NoticeWindow(lang);
        AddWindowButton.Content = UiStrings.Get(lang, UiTextKey.AddPage);
        ExistingWindowButton.Content = UiStrings.Format(lang, UiTextKey.AddExisting, 0);
        _config = config;
        _configPath = configPath;
        _startupUrls = startupUrls;
        _session = session;

        // Last page list write failures: one line for the first failure and one for the first success afterwards (no URLs).
        _session.WriteStatusChanged += change =>
            _diagnostics.Write(DiagnosticsFormat.SessionWrite(DateTime.UtcNow, change));

        // Place inside the primary screen's work area first, then maximize, so it maximizes on the primary screen.
        Rect work = SystemParameters.WorkArea;
        Left = work.Left;
        Top = work.Top;
        WindowState = WindowState.Maximized;

        _checkTimer = new DispatcherTimer { Interval = CheckInterval };
        _checkTimer.Tick += (_, _) => OnCheckTick();
        _deferredTimer = new DispatcherTimer { Interval = DeferDelay };
        _deferredTimer.Tick += (_, _) => OnDeferredTick();
        _captionPoll = new DispatcherTimer { Interval = CaptionPollInterval };
        _captionPoll.Tick += (_, _) => OnCaptionPollTick();
        _dragOutTimer = new DispatcherTimer { Interval = DragOutWatcher.MaxPollInterval / 2 };
        _dragOutTimer.Tick += (_, _) => PollDragOut();
        _foregroundProc = OnForegroundChanged;
        _button.ShrinkRequested += OnShrinkRequested;
        _addressBar = new AddressBarReader(Dispatcher, OnAddressBarRound, _diagnostics);
        _taskbar = new Taskbar(_diagnostics);
        _flat = new FlatChecker(
            _diagnostics,
            (id, token) => Dispatcher.BeginInvoke(() => NudgeNow(id, token)),
            (id, token) => Dispatcher.BeginInvoke(() => NudgeRestoreNow(id, token)));

        // Minimum wall size: with 16 cells, both icons still fit completely in every title bar.
        MinWidth = 480;
        MinHeight = 400;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(_hwnd)?.AddHook(WallWndProc);
        };
        ContentRendered += OnContentRendered;
        SizeChanged += (_, _) => OnWallMovedOrResized();
        LocationChanged += (_, _) => OnWallMovedOrResized();
        StateChanged += (_, _) => OnWallStateChanged();
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeave += OnMouseLeave;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        LostMouseCapture += OnLostMouseCapture;
        Closing += OnClosing;
    }

    private async void OnContentRendered(object? sender, EventArgs e)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _thumbnails = new Thumbnails(_hwnd);
        _launching = true;
        Render();

        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _foregroundProc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
        _checkTimer.Start();

        // Snapshot before launching (with process ids), used to find windows left over from an abnormal end last time.
        _launchSnapshot = WindowSystem.VisibleTopLevelInfos();

        // Dragged-out tabs join the wall automatically: windows present before the wall opened never count as new.
        _dragOut = new DragOutWatcher(_launchSnapshot.Select(w => w.Id));
        _dragOutClock.Start();
        _dragOutTimer.Start();

        // Read the address bars every 2 seconds and record the last seen URLs.
        _addressBar.Start();

        // Check for flat-color cells every 10 seconds.
        _flat.Start();

        await BrowserLauncher.LaunchAllAsync(
            _config,
            _startupUrls,
            (id, url, index) => OnWindowFound(id, url, _session.StartupEntry(index)),
            (url, index) => OnWindowFailed(url, _session.StartupEntry(index)),
            () => _closed,
            _diagnostics);

        _launching = false;

        // Leftover windows are handled only at startup; if every URL failed the snapshot goes unused, and "+ Add page" never handles them later.
        _launchSnapshot = null;
        if (!_closed)
        {
            Render();
        }
    }

    /// <summary>A URL's window was found at startup. <paramref name="entry"/> is its index in the last page list.</summary>
    private void OnWindowFound(long windowId, string url, int entry)
    {
        if (_closed)
        {
            return;
        }

        IReadOnlyList<WallAction> actions = _state.AddWindow(windowId, url, entry);
        PrepareRegistered(windowId);
        Apply(actions);
        CloseLeftovers();
    }

    /// <summary>A newly registered managed window: record the wall's browser process (only the first one), the title, and register the thumbnail.</summary>
    private void PrepareRegistered(long windowId)
    {
        _wallProcessId ??= WindowSystem.GetProcessId(new IntPtr(windowId));
        _titles[windowId] = WindowSystem.GetTitle(new IntPtr(windowId));
        _thumbnails?.Register(windowId);
    }

    /// <summary>
    /// After the wall's browser process id is first known, closes unmanaged windows of the same process in the pre-launch
    /// snapshot (left over from an abnormal end last time) and adds their taskbar buttons back. Done only once.
    /// </summary>
    private void CloseLeftovers()
    {
        if (_launchSnapshot is null || _wallProcessId is not uint wallPid)
        {
            return;
        }

        IReadOnlyList<long> leftovers = LeftoverFinder.Find(_launchSnapshot, wallPid, _state.WindowIds);
        _launchSnapshot = null;
        foreach (long id in leftovers)
        {
            WindowSystem.RequestClose(new IntPtr(id));
            _taskbar.Show(id);
        }
    }

    /// <summary>Number of open windows that can be added, as last computed (every second); the N in "Add open window (N)".</summary>
    private int _candidateCount;

    /// <summary>"+ Add page" is still looking for its window, or startup is running: "+ Add page" is disabled.</summary>
    private bool AddBusy => _launching || _addingPage;

    /// <summary>"+ Add page": no dialog; opens a new window directly.</summary>
    private void OnAddWindowClick(object sender, RoutedEventArgs e)
    {
        if (!_closed)
        {
            AddPage();
        }
    }

    /// <summary>
    /// "Add open window (N)": shows the list, refreshed every second while open; picking a row adds it.
    /// </summary>
    private void OnExistingWindowClick(object sender, RoutedEventArgs e)
    {
        if (_closed || !AddPicker.CanAdd(_state, AddBusy))
        {
            return;
        }

        var dialog = new AddWindowDialog(CurrentCandidates, () => !_state.HasFreeSlot, _lang) { Owner = this };
        if (dialog.ShowDialog() != true || _closed)
        {
            return;
        }

        if (dialog.SelectedWindowId is long id)
        {
            AdoptExisting(id);
        }
    }

    /// <summary>Open windows that can be added now (same process and class, with a title, not yet managed).</summary>
    private IReadOnlyList<WindowInfo> CurrentCandidates() =>
        AddPicker.Candidates(WindowSystem.VisibleTopLevelInfos(), _wallProcessId, _state.WindowIds);

    /// <summary>
    /// "+ Add page": opens <see cref="AddPageFlow.NewWindowUrl"/> like a startup launch; when found, registers it (no URL), parks it,
    /// hides its taskbar button, relayouts and expands it; if nothing is found within 10 seconds, no cell is added and "could not add page" is shown at the top of the wall for 3 seconds.
    /// </summary>
    private async void AddPage()
    {
        if (AddPageFlow.Begin(_state, AddBusy) is not null)
        {
            // The button is already disabled (safeguard).
            return;
        }

        string url = AddPageFlow.NewWindowUrl;
        _addingPage = true;
        UpdateToolbar();
        long? found;
        try
        {
            found = await BrowserLauncher.LaunchOneAsync(_config, url, () => _closed, _diagnostics);
        }
        finally
        {
            _addingPage = false;
        }

        if (_closed)
        {
            // Wall closing: not registered; closing the wall no longer changes the list.
            return;
        }

        AddPageResult result = AddPageFlow.Finish(_state, _session, found);
        if (result.Found && found is long id)
        {
            PrepareRegistered(id);
            Apply(result.Actions);
        }
        else if (CurrentWallRect() is NativeMethods.RECT wall)
        {
            _notice.ShowOver(wall, _scale, UiStrings.Get(_lang, UiTextKey.AddPageFailed));
        }

        UpdateToolbar();
    }

    /// <summary>
    /// Adds an already open window: rechecks it can still be added (skipped if it closed while the list was open).
    /// Such a cell starts without a URL and joins the last page list once a URL is recorded.
    /// </summary>
    private void AdoptExisting(long id)
    {
        if (!AddPicker.CanAdd(_state, AddBusy)
            || !AddPicker.IsStillCandidate(id, WindowSystem.VisibleTopLevelInfos(), _wallProcessId, _state.WindowIds))
        {
            return;
        }

        IReadOnlyList<WallAction> actions = _state.AdoptWindow(id);
        if (actions.Count == 0)
        {
            return;
        }

        _titles[id] = WindowSystem.GetTitle(new IntPtr(id));
        _thumbnails?.Register(id);
        Apply(actions);
    }

    /// <summary>
    /// Drag-out watching, every 50 ms: enumerates top-level windows and the left mouse button and lets Core decide, then
    /// handles the decisions in the returned order with no other state change in between. Nothing is added while startup or
    /// "+ Add page" is looking for a new window (<see cref="AddBusy"/>): the detector claims the new window first and
    /// registers it as managed, and the watcher drops it once it sees it is managed.
    /// Windows that are "found but not yet registered" never exist (they are registered in the same code path once found), so the awaited set is empty.
    /// </summary>
    private void PollDragOut()
    {
        if (_closed || _dragOut is null)
        {
            return;
        }

        List<DragOutWindow> windows = WindowSystem.DragOutWindows(_wallProcessId, out List<long> existingIds);
        var snapshot = new DragOutSnapshot(
            _dragOutClock.Elapsed,
            windows,
            WindowSystem.IsLeftButtonDown(),
            _wallProcessId,
            _state.WindowIds,
            Array.Empty<long>(),
            _state.CanAutoAdd && !AddBusy,
            _state.FreeSlotCount,
            existingIds);

        foreach (DragOutDecision decision in _dragOut.Poll(snapshot))
        {
            if (decision.Kind == DragOutDecisionKind.Add)
            {
                AutoAddExisting(decision.WindowId);
            }
            else
            {
                ShowFullNotice();
            }
        }
    }

    /// <summary>
    /// Adds a dragged-out window automatically: parking and taskbar handling do not wait for the other side (<see cref="ApplyMode.Deferred"/>),
    /// and the foreground, expansion and the expanded window are left alone. It starts without a URL and joins the list once a URL is recorded.
    /// </summary>
    private void AutoAddExisting(long id)
    {
        IReadOnlyList<WallAction> actions = _state.AutoAddWindow(id);
        if (actions.Count == 0)
        {
            return;
        }

        _titles[id] = WindowSystem.GetTitle(new IntPtr(id));
        _thumbnails?.Register(id);
        Apply(actions, ApplyMode.Deferred);
    }

    /// <summary>
    /// One round of address bar results (UI thread): records each cell by the rules (not while focused, only http/https,
    /// prepend https://, keep on unreadable, 2 seconds per cell); a newly recorded URL rewrites the whole last page list. Dropped once the wall is closed.
    /// </summary>
    private void OnAddressBarRound(IReadOnlyList<AddressBarReading> readings)
    {
        if (_closed)
        {
            return;
        }

        bool changed = false;
        foreach (AddressBarReading r in readings)
        {
            changed |= _lastSeen.Record(_state, r.WindowId, r.Value, r.Focused, _urlClock.Elapsed);
        }

        if (changed)
        {
            _session.Sync(_state.Slots);
        }
    }

    /// <summary>"Wall is full" for 3 seconds; the dragged-out window stays where it is.</summary>
    private void ShowFullNotice()
    {
        if (CurrentWallRect() is NativeMethods.RECT wall)
        {
            _notice.ShowOver(wall, _scale);
        }
    }

    /// <summary>
    /// "Close" on a cell: sends a close request; if the window still exists after 2 seconds, the state machine expands it.
    /// Operations on other processes' windows do not wait for them to respond (<see cref="ApplyMode.Deferred"/>).
    /// </summary>
    private void OnCloseClick(long windowId)
    {
        if (_closed)
        {
            return;
        }

        IReadOnlyList<WallAction> actions = _state.CloseCell(windowId);
        if (actions.Count == 0)
        {
            return;
        }

        Apply(actions, ApplyMode.Deferred);
        var timer = new DispatcherTimer { Interval = CloseWait };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!_closed)
            {
                Apply(_state.CloseStillOpen(windowId), ApplyMode.Deferred);
            }
        };
        timer.Start();
    }

    /// <summary>"Pop out" on a cell.</summary>
    private void OnReleaseClick(long windowId)
    {
        if (!_closed)
        {
            Apply(_state.ReleaseWindow(windowId));
        }
    }

    /// <summary>A URL failed to open at startup; the URL stays in the last page list.</summary>
    private void OnWindowFailed(string url, int entry) => Apply(_state.AddFailed(url, entry));

    /// <summary>"Pop out" on a failed cell: removes the cell, and the list drops its URL.</summary>
    private void OnReleaseFailedClick(int index)
    {
        if (!_closed)
        {
            Apply(_state.ReleaseFailed(index));
        }
    }

    /// <summary>The "Back to grid" button.</summary>
    private void OnShrinkRequested()
    {
        if (!_closed)
        {
            Apply(_state.Shrink());
        }
    }

    /// <summary>Cell index at wall coordinates (logical pixels); -1 outside the cells, in a gap or outside the wall.</summary>
    private int CellIndexAt(Point p) =>
        _layout is null ? -1 : _layout.IndexAt((int)Math.Floor(p.X * _scale), (int)Math.Floor(p.Y * _scale));

    /// <summary>
    /// Left button pressed on a cell in grid mode: starts the gesture and captures the mouse (so a release outside the wall is still received).
    /// Not started in expanded mode, while the wall is minimized, or while a restore is pending.
    /// </summary>
    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_closed
            || _layout is null
            || _state.Mode != WallMode.Grid
            || _state.IsWallMinimized
            || _state.RestorePending
            || IsInsideButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        Point p = e.GetPosition(this);
        int index = CellIndexAt(p);
        if (index < 0)
        {
            return;
        }

        // The drag area is the whole cell (including the title bar), but ✕ and "Pop out" stay buttons.
        CellRect inner = Inset(_layout.Cells[index], _borderPx);
        int px = (int)Math.Floor(p.X * _scale);
        int py = (int)Math.Floor(p.Y * _scale);
        if (CellContent.HitTest(inner, _titleHeightPx, _buttonWidthPx, px, py)
            is CellHit.CloseButton or CellHit.ReleaseButton)
        {
            return;
        }

        _drag.Press(index, p.X, p.Y);
        CaptureMouse();
    }

    /// <summary>Moving after a press: past 10 logical pixels it becomes a drag and the drag visuals update.</summary>
    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_closed)
        {
            return;
        }

        if (_drag.IsPressed)
        {
            Point p = e.GetPosition(this);
            if (_drag.Move(p.X, p.Y))
            {
                UpdateDragVisuals(p);
            }
        }

        // Title bar buttons follow the cell under the mouse; all hidden once a drag starts.
        UpdateTitleButtons();
    }

    /// <summary>The mouse left the wall (or another window covers it): every cell's buttons are hidden.</summary>
    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (!_closed)
        {
            UpdateTitleButtons();
        }
    }

    /// <summary>
    /// Left button released: take and keep the gesture result first, then release the mouse capture
    /// (releasing synchronously raises <see cref="OnLostMouseCapture"/>; the gesture has ended by then, so it is not treated as a cancel).
    /// Below the threshold → expand; dragged onto another cell → swap, relayout, and rewrite the list in the new order; otherwise → no change.
    /// </summary>
    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_closed || !_drag.IsPressed)
        {
            return;
        }

        int dropIndex = CellIndexAt(e.GetPosition(this));
        CellDragResult result = _drag.Release(dropIndex);
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        bool hadVisuals = EndDragVisuals();
        IReadOnlyList<WallAction> actions = result.Kind switch
        {
            CellDragResultKind.Click => _state.ClickCell(result.From),
            CellDragResultKind.Swap => _state.SwapCells(result.From, result.To),
            _ => Array.Empty<WallAction>(),
        };

        if (actions.Count > 0)
        {
            Apply(actions);
        }
        else if (hadVisuals)
        {
            // No change: put the thumbnails back.
            Render();
        }

        // Drag ended: buttons come back for the cell under the mouse.
        UpdateTitleButtons();
    }

    /// <summary>
    /// Mouse capture lost: cancel only when the gesture has not ended normally (for example, switched to another program); cells do not change.
    /// On a normal release the gesture has already ended, so this just returns.
    /// </summary>
    private void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_drag.IsPressed)
        {
            return;
        }

        CancelDrag();
    }

    /// <summary>Cancels the gesture in progress and the drag visuals, putting the thumbnails back. Safe to call repeatedly.</summary>
    private void CancelDrag()
    {
        bool wasPressed = _drag.IsPressed;
        _drag.Cancel();
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        if (EndDragVisuals() || wasPressed)
        {
            Render();
        }

        UpdateTitleButtons();
    }

    /// <summary>
    /// Drag visuals: the dragged cell follows the mouse as a semi-transparent drag image (a separate window, visible over any thumbnail),
    /// the original cell becomes semi-transparent, and the other cell under the mouse gets a frame (a managed cell's thumbnail is inset so the frame is not covered).
    /// </summary>
    private void UpdateDragVisuals(Point p)
    {
        if (_layout is null || _thumbnails is null)
        {
            return;
        }

        int source = _drag.SourceIndex;
        IReadOnlyList<WallSlot> slots = _state.Slots;
        if (source < 0 || source >= slots.Count || source >= _layout.Cells.Count)
        {
            CancelDrag();
            return;
        }

        if (!_dragVisualsActive)
        {
            _dragVisualsActive = true;
            WallSlot slot = slots[source];
            CellRect cell = _layout.Cells[source];
            string caption = slot.Kind == WallSlotKind.Window
                ? (_titles.TryGetValue(slot.WindowId!.Value, out string? t) ? t : string.Empty)
                : UiStrings.Format(_lang, UiTextKey.OpenFailedTitle, slot.Url);
            _ghost.Begin(cell.Width, cell.Height, _scale, caption);

            if (slot.Kind == WallSlotKind.Window
                && _thumbnailAreas.TryGetValue(slot.WindowId!.Value, out CellRect area))
            {
                _dragSourceId = slot.WindowId.Value;
                _thumbnails.Place(_dragSourceId.Value, area, opacity: 128);
            }
            else if (_failedCellElements.TryGetValue(source, out UIElement? failed))
            {
                failed.Opacity = 0.5;
            }
        }

        Point screen = PointToScreen(p);
        _ghost.MoveCenterTo((int)Math.Round(screen.X), (int)Math.Round(screen.Y));

        int target = CellIndexAt(p);
        if (target == source)
        {
            target = -1;
        }

        if (target != _dragTargetIndex)
        {
            ClearDragTarget();
            if (target >= 0 && target < slots.Count && target < _layout.Cells.Count)
            {
                SetDragTarget(target, slots[target], _layout.Cells[target]);
            }
        }
    }

    /// <summary>Frames the target cell; a managed cell's thumbnail is inset by the frame width.</summary>
    private void SetDragTarget(int index, WallSlot slot, CellRect cell)
    {
        int frame = (int)Math.Round(DragFrameDip * _scale);
        _dragFrame = new Border
        {
            Width = cell.Width / _scale,
            Height = cell.Height / _scale,
            BorderBrush = DragFrameBrush,
            BorderThickness = new Thickness(DragFrameDip),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(_dragFrame, cell.X / _scale);
        Canvas.SetTop(_dragFrame, cell.Y / _scale);
        CellCanvas.Children.Add(_dragFrame);
        _dragTargetIndex = index;

        if (slot.Kind == WallSlotKind.Window
            && _thumbnailAreas.TryGetValue(slot.WindowId!.Value, out CellRect area))
        {
            _dragTargetId = slot.WindowId.Value;
            var inset = new CellRect(
                area.X + frame,
                area.Y + frame,
                Math.Max(0, area.Width - (2 * frame)),
                Math.Max(0, area.Height - frame));
            _thumbnails?.Place(_dragTargetId.Value, inset);
        }
    }

    /// <summary>Removes the target cell's frame and puts the thumbnail back.</summary>
    private void ClearDragTarget()
    {
        if (_dragFrame is not null)
        {
            CellCanvas.Children.Remove(_dragFrame);
            _dragFrame = null;
        }

        if (_dragTargetId is long id && _thumbnailAreas.TryGetValue(id, out CellRect area))
        {
            _thumbnails?.Place(id, area);
        }

        _dragTargetId = null;
        _dragTargetIndex = -1;
    }

    /// <summary>Ends the drag visuals (hide the drag image, remove the frame). Safe to call repeatedly; returns true when there were drag visuals.</summary>
    private bool EndDragVisuals()
    {
        bool had = _dragVisualsActive || _dragFrame is not null;
        _ghost.End();
        ClearDragTarget();
        _dragVisualsActive = false;
        _dragSourceId = null;
        return had;
    }

    /// <summary>Whether the click source is inside a button (clicks on "Pop out" or "+ Add page" do not count as cell clicks).</summary>
    private static bool IsInsideButton(DependencyObject? source)
    {
        for (DependencyObject? d = source; d is not null;
             d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is System.Windows.Controls.Primitives.ButtonBase)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Runs the state machine's actions in order. <paramref name="mode"/>: normal, wall becoming active (only our own windows move, parking deferred),
    /// or deferred (no waiting on other processes' windows).
    /// </summary>
    private void Apply(IReadOnlyList<WallAction> actions, ApplyMode mode = ApplyMode.Normal)
    {
        if (_closed || actions.Count == 0)
        {
            return;
        }

        // The state machine changed (for example, pop out or wall minimized): notify the flat-color check first, then act on windows,
        // so after the notification the checker never nudges a window that was popped out or minimized.
        UpdateFlatChecker();

        bool shrunk = false;
        bool deferred = mode == ApplyMode.Deferred;
        var parkLater = new List<long>();
        NativeMethods.RECT? wallRect = CurrentWallRect();
        foreach (WallAction action in actions)
        {
            if (mode == ApplyMode.Activation
                && action.Kind is not (WallActionKind.HideShrinkButton or WallActionKind.ParkDeferred
                    or WallActionKind.Relayout or WallActionKind.ShowEmpty))
            {
                // At the moment the wall becomes active, other programs' windows are not touched; the state machine does not produce these actions then either.
                continue;
            }

            switch (action.Kind)
            {
                case WallActionKind.Park:
                    if (wallRect is NativeMethods.RECT parkRect)
                    {
                        if (deferred)
                        {
                            WindowSystem.ParkAsync(new IntPtr(action.WindowId!.Value), _hwnd, parkRect);
                        }
                        else
                        {
                            WindowSystem.Park(new IntPtr(action.WindowId!.Value), _hwnd, parkRect);
                        }
                    }

                    break;
                case WallActionKind.ParkDeferred:
                    parkLater.Add(action.WindowId!.Value);
                    break;
                case WallActionKind.Restore:
                    if (deferred)
                    {
                        WindowSystem.RestoreAsync(new IntPtr(action.WindowId!.Value));
                    }
                    else
                    {
                        WindowSystem.Restore(new IntPtr(action.WindowId!.Value));
                    }

                    break;
                case WallActionKind.Expand:
                    if (wallRect is NativeMethods.RECT expandRect)
                    {
                        if (deferred)
                        {
                            WindowSystem.ExpandAsync(new IntPtr(action.WindowId!.Value), expandRect);
                        }
                        else
                        {
                            WindowSystem.Expand(new IntPtr(action.WindowId!.Value), expandRect);
                        }
                    }

                    break;
                case WallActionKind.ShowShrinkButton:
                    if (wallRect is NativeMethods.RECT buttonRect)
                    {
                        _button.ShowAt(buttonRect);
                    }

                    if (deferred)
                    {
                        ArrangeExpandedAsync();
                    }
                    else
                    {
                        ArrangeExpanded();
                    }

                    break;
                case WallActionKind.HideShrinkButton:
                    _button.Hide();
                    shrunk = true;
                    break;
                case WallActionKind.Release:
                    {
                        // Unregister the thumbnail and title first, then put the window at the center of the wall.
                        long id = action.WindowId!.Value;
                        _thumbnails?.Unregister(id);
                        _titles.Remove(id);
                        if (wallRect is NativeMethods.RECT releaseRect)
                        {
                            WindowSystem.ReleaseToCenter(new IntPtr(id), releaseRect);
                        }

                        _taskbar.Show(id);
                        break;
                    }

                case WallActionKind.Minimize:
                    WindowSystem.Minimize(new IntPtr(action.WindowId!.Value));
                    break;
                case WallActionKind.CloseWindow:
                    // Cell "Close": PostMessage, without waiting for the other side to respond.
                    WindowSystem.RequestClose(new IntPtr(action.WindowId!.Value));
                    break;
                case WallActionKind.HideTaskbarButton:
                    _taskbar.Hide(action.WindowId!.Value, CellOf(action.WindowId!.Value));
                    break;

                case WallActionKind.Relayout:
                case WallActionKind.ShowEmpty:
                    Render();
                    break;
            }
        }

        // The last page list follows the cells: a popped-out or closed cell drops its URL, a swap uses the new order, and the whole list is rewritten.
        _session.Sync(_state.Slots);
        _addressBar.SetWindows(_state.WindowIds);
        UpdateFlatChecker();
        UpdateToolbar();

        // When the mode changes (expand, shrink, wall minimized) the buttons follow (shown only in grid mode on the cell under the mouse).
        UpdateTitleButtons();

        // No longer in grid mode while dragging (expanded, wall minimized, etc.): cancel the drag; cells do not change.
        if (_drag.IsPressed && (_state.Mode != WallMode.Grid || _state.IsWallMinimized || _state.RestorePending))
        {
            CancelDrag();
        }

        if (parkLater.Count > 0)
        {
            ScheduleDeferredPark(parkLater);
        }

        // When the wall becomes active it is already in front, so no Activate; deferred runs do not take the foreground either.
        if (mode == ApplyMode.Normal && shrunk && _state.Mode != WallMode.Expanded)
        {
            // After shrinking, the wall returns to the foreground so the cells are visible right away.
            Activate();
        }
    }

    /// <summary>Whether the wall is minimized.</summary>
    private bool WallMinimized => _hwnd != IntPtr.Zero && NativeMethods.IsIconic(_hwnd);

    /// <summary>
    /// The currently usable wall area (screen coordinates, physical pixels). Updated and remembered while the wall is not minimized and has an area;
    /// while minimized, a minimized window's coordinates are not its visible position, so the last valid wall area is used.
    /// Null when no valid wall area was ever obtained.
    /// </summary>
    private NativeMethods.RECT? CurrentWallRect()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return _lastWallRect;
        }

        if (!WallMinimized)
        {
            NativeMethods.RECT r = WindowSystem.GetWallScreenRect(_hwnd);
            if (r.Right > r.Left && r.Bottom > r.Top)
            {
                _lastWallRect = r;
            }
        }

        return _lastWallRect;
    }

    /// <summary>While expanded: button → expanded window → wall.</summary>
    private void ArrangeExpanded()
    {
        if (_state.ExpandedWindowId is long id)
        {
            WindowSystem.ArrangeExpanded(_button.Handle, new IntPtr(id), _hwnd);
        }
    }

    /// <summary>Same as above, without waiting for the browser window to respond (for deferred actions).</summary>
    private void ArrangeExpandedAsync()
    {
        if (_state.ExpandedWindowId is long id)
        {
            WindowSystem.ArrangeExpandedAsync(_button.Handle, new IntPtr(id), _hwnd);
        }
    }

    /// <summary>A deferred action or a title bar button press is in progress: the periodic Z-order fix pauses to avoid interference.</summary>
    private bool DeferredBusy =>
        _deferredKind != DeferredKind.None || _state.CaptionPressPending || _state.RestorePending;

    /// <summary>
    /// Schedules a deferred park: after the wall becomes active, or when the wall is moved or resized meanwhile. Merges with targets not yet parked
    /// and restarts the timer from the latest scheduling (no parking halfway while the wall is dragged); when it runs, it parks on the current wall without waiting.
    /// A pending restore already includes all parking, so nothing extra is scheduled.
    /// </summary>
    private void ScheduleDeferredPark(IReadOnlyList<long> ids)
    {
        if (_deferredKind == DeferredKind.Restore)
        {
            return;
        }

        _deferredKind = DeferredKind.Park;
        _deferredParkIds = _deferredParkIds.Union(ids).ToList();
        _deferredTimer.Stop();
        _deferredTimer.Start();
    }

    /// <summary>Schedules a deferred restore; replaces any deferred park not yet run (the restore includes all parking).</summary>
    private void ScheduleDeferredRestore()
    {
        _deferredKind = DeferredKind.Restore;
        _deferredParkIds = new List<long>();
        _deferredTimer.Stop();
        _deferredTimer.Start();
    }

    /// <summary>Cancels a deferred action not yet run (wall minimized, wall closing).</summary>
    private void CancelDeferred()
    {
        _deferredTimer.Stop();
        _deferredKind = DeferredKind.None;
        _deferredParkIds = new List<long>();
    }

    /// <summary>
    /// A deferred action is due: actions are generated again from the current state and run without waiting.
    /// Not run while a title bar button is pressed; checked again later.
    /// </summary>
    private void OnDeferredTick()
    {
        _deferredTimer.Stop();
        if (_closed)
        {
            return;
        }

        if (WallMinimized || _state.IsWallMinimized)
        {
            CancelDeferred();
            return;
        }

        if (_state.CaptionPressPending)
        {
            _deferredTimer.Start();
            return;
        }

        DeferredKind kind = _deferredKind;
        List<long> scheduled = _deferredParkIds;
        _deferredKind = DeferredKind.None;
        _deferredParkIds = new List<long>();

        switch (kind)
        {
            case DeferredKind.Park:
                if (CurrentWallRect() is NativeMethods.RECT rect)
                {
                    foreach (long id in _state.DeferredParkTargets(scheduled))
                    {
                        WindowSystem.ParkAsync(new IntPtr(id), _hwnd, rect);
                    }
                }

                break;
            case DeferredKind.Restore:
                Apply(_state.SettleRestore(), ApplyMode.Deferred);
                break;
        }
    }

    /// <summary>
    /// The wall's window procedure: records a button press when the mouse is pressed on the title bar minimize or close button,
    /// regardless of foreground events, also during a restore. Messages are not intercepted; Windows handles them as usual.
    /// </summary>
    private IntPtr WallWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_NCLBUTTONDOWN && !_closed
            && wParam.ToInt32() is NativeMethods.HTMINBUTTON or NativeMethods.HTCLOSE)
        {
            _state.CaptionPressStarted();
            StartCaptionPoll();
        }

        return IntPtr.Zero;
    }

    private void StartCaptionPoll()
    {
        if (_state.CaptionPressPending && !_captionPoll.IsEnabled)
        {
            _captionPoll.Start();
        }
    }

    /// <summary>
    /// While a title bar button is pressed, checks every 50 ms whether the mouse was released; after release, waits for Windows to finish handling it (a real minimize or close) before deciding whether it was cancelled.
    /// </summary>
    private void OnCaptionPollTick()
    {
        if (_closed || !_state.CaptionPressPending)
        {
            _captionPoll.Stop();
            return;
        }

        if (WindowSystem.IsLeftButtonDown())
        {
            return;
        }

        _captionPoll.Stop();
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(EndCaptionPress));
    }

    /// <summary>
    /// Still in the button press = neither minimized nor closed = cancelled: if the wall is still active, shrink (with deferred parking).
    /// </summary>
    private void EndCaptionPress()
    {
        if (_closed || !_state.CaptionPressPending)
        {
            return;
        }

        bool wallActive = NativeMethods.GetForegroundWindow() == _hwnd;
        Apply(_state.CaptionPressEnded(wallActive), ApplyMode.Activation);
    }

    /// <summary>Every 0.5 seconds: close detection, restore from minimized, Z order; every second: update titles and thumbnail positions.</summary>
    private void OnCheckTick()
    {
        if (_closed)
        {
            return;
        }

        CheckClosed();
        CheckMinimized();

        // No synchronous Z-order fix while a deferred action or title bar button press is in progress.
        if (!DeferredBusy)
        {
            EnforceZOrder();
        }

        _tickCount++;
        if (_tickCount % 2 == 0)
        {
            foreach (long id in _state.WindowIds)
            {
                _titles[id] = WindowSystem.GetTitle(new IntPtr(id));

                // Hide again: the taskbar may add the button back when the window is restored or its show state changes.
                _taskbar.Hide(id, CellOf(id));
            }

            RefreshTitlesAndThumbnails();

            // Windows whose close timed out but could not expand yet: expanded within 1 second after things change.
            Apply(_state.ExpandTimedOutClose(), ApplyMode.Deferred);

            // Recompute N for "Add open window (N)" every second.
            // Counted with no cells too, so the last cell can be added back after it was popped out.
            _candidateCount = _state.Mode != WallMode.Expanded && !_state.IsWallMinimized ? CurrentCandidates().Count : 0;
            UpdateToolbar();
        }
    }

    /// <summary>
    /// Per-second update: changes existing title text directly and updates thumbnail positions without rebuilding cells and buttons
    /// (a rebuild would lose a half-pressed "Pop out" click). A full redraw is triggered by state machine actions or a wall resize when the cell structure changes.
    /// </summary>
    private void RefreshTitlesAndThumbnails()
    {
        if (_closed || _hwnd == IntPtr.Zero || _thumbnails is null || WallMinimized)
        {
            return;
        }

        foreach ((long id, TextBlock block) in _titleBlocks)
        {
            block.Text = _titles.TryGetValue(id, out string? title) ? title : string.Empty;
        }

        foreach ((long id, CellRect area) in _thumbnailAreas)
        {
            // While dragging, the dragged and framed cells are placed by the drag visuals, not put back.
            if (id == _dragSourceId || id == _dragTargetId)
            {
                continue;
            }

            _thumbnails.Place(id, area);
        }
    }

    /// <summary>Removes the cell when a managed window is closed (its handle becomes invalid).</summary>
    private void CheckClosed()
    {
        IReadOnlyList<long> before = _state.WindowIds;
        var alive = before.Where(id => NativeMethods.IsWindow(new IntPtr(id))).ToList();
        if (alive.Count == before.Count)
        {
            return;
        }

        IReadOnlyList<WallAction> actions = _state.SyncAlive(alive);
        foreach (long id in before.Except(_state.WindowIds))
        {
            _thumbnails?.Unregister(id);
            _titles.Remove(id);
        }

        Apply(actions);
    }

    /// <summary>Restores and parks again a managed window that was minimized.</summary>
    private void CheckMinimized()
    {
        foreach (long id in _state.WindowIds)
        {
            if (NativeMethods.IsIconic(new IntPtr(id)))
            {
                Apply(_state.WindowMinimized(id));
            }
        }
    }

    /// <summary>
    /// Parks again a parked managed window that got above the wall; while expanded, keeps the order button → expanded window → wall.
    /// </summary>
    private void EnforceZOrder()
    {
        // While the wall is minimized there is no wall area to cover, so no reordering; WallResized parks again after the wall restores.
        if (_closed || _hwnd == IntPtr.Zero || _state.Mode == WallMode.Empty || WallMinimized)
        {
            return;
        }

        NativeMethods.RECT? wallRect = CurrentWallRect();
        if (wallRect is not NativeMethods.RECT parkRect)
        {
            return;
        }

        long? expanded = _state.ExpandedWindowId;
        long? button = expanded is null ? null : _button.Handle.ToInt64();
        ZOrderReport report = ZOrderCheck.Inspect(
            WindowSystem.ZOrder(), _hwnd.ToInt64(), _state.ParkedWindowIds, expanded, button);
        if (!report.NeedsFix)
        {
            return;
        }

        long foreground = NativeMethods.GetForegroundWindow().ToInt64();
        bool parkedWasForeground = false;
        foreach (long id in report.ParkedAboveWall)
        {
            WindowSystem.Park(new IntPtr(id), _hwnd, parkRect);
            parkedWasForeground |= id == foreground;
        }

        if (expanded is long e)
        {
            ArrangeExpanded();
            if (parkedWasForeground)
            {
                NativeMethods.SetForegroundWindow(new IntPtr(e));
                ArrangeExpanded();
            }
        }
        else if (parkedWasForeground)
        {
            Activate();
        }
    }

    /// <summary>
    /// The foreground window changed (received on the UI thread). When the wall becomes active: no other window is made foreground and other programs' windows are not reordered synchronously;
    /// no shrinking while the title bar minimize or close button is pressed; otherwise the state machine decides (expanded mode counts as shrink, parking deferred).
    /// </summary>
    private void OnForegroundChanged(
        IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (_closed || _hwnd == IntPtr.Zero || WallMinimized)
        {
            return;
        }

        if (hwnd == _hwnd)
        {
            // The foreground already changed by the time the event arrived (for example, the deferred restore gave it to the expanded browser): not the wall becoming active, so ignore.
            if (NativeMethods.GetForegroundWindow() != _hwnd)
            {
                return;
            }

            bool captionButton = _state.CaptionPressPending || WindowSystem.IsCaptionButtonAt(_hwnd);
            Apply(_state.WallActivated(captionButton), ApplyMode.Activation);
            StartCaptionPoll();
            return;
        }

        if (!DeferredBusy)
        {
            EnforceZOrder();
        }
    }

    /// <summary>
    /// Wall minimize and restore: managed windows minimize together; on restore, back to the mode before minimizing. Wall area changes are then handled as usual.
    /// </summary>
    private void OnWallStateChanged()
    {
        if (_closed || _hwnd == IntPtr.Zero || _thumbnails is null)
        {
            return;
        }

        if (WindowState == WindowState.Minimized)
        {
            if (!_state.IsWallMinimized)
            {
                // Really minimized: cancel deferred actions not yet run.
                CancelDeferred();
                _captionPoll.Stop();
                Apply(_state.WallMinimized());
            }

            return;
        }

        if (_state.IsWallMinimized)
        {
            // On restore the wall also becomes active: only the wall's own display changes now; restoring managed windows is deferred without waiting,
            // and generated from the state at that time (SettleRestore).
            IReadOnlyList<WallAction> restored = _state.WallRestored();
            Apply(restored.Where(a => a.Kind is WallActionKind.Relayout or WallActionKind.ShowEmpty).ToList());
            if (_state.RestorePending)
            {
                ScheduleDeferredRestore();
            }
        }

        OnWallMovedOrResized();
    }

    /// <summary>The wall was moved or resized. While minimized, browsers are not rearranged with an invalid wall area.</summary>
    private void OnWallMovedOrResized()
    {
        if (_closed || _hwnd == IntPtr.Zero || _thumbnails is null || WallMinimized)
        {
            return;
        }

        Apply(_state.WallResized());
    }

    /// <summary>Redraws cells and titles from the current state and updates thumbnail positions.</summary>
    private void Render()
    {
        // Invisible and without a valid size while minimized; SizeChanged / StateChanged redraw on restore.
        if (_closed || _hwnd == IntPtr.Zero || _thumbnails is null || WallMinimized)
        {
            return;
        }

        // Indexes may change when cells are rebuilt: any drag in progress is cancelled (no change).
        if (_drag.IsPressed)
        {
            _drag.Cancel();
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }
        }

        EndDragVisuals();
        CellCanvas.Children.Clear();
        _titleBlocks.Clear();
        _thumbnailAreas.Clear();
        _failedCellElements.Clear();
        _cellButtons.Clear();
        UpdateToolbar();

        if (_state.Mode == WallMode.Empty)
        {
            _layout = null;
            StatusText.Visibility = Visibility.Visible;
            StatusText.Text = _launching
                ? UiStrings.Get(_lang, UiTextKey.Launching)
                : UiStrings.Format(_lang, UiTextKey.NoWindows, _configPath);
            return;
        }

        StatusText.Visibility = Visibility.Collapsed;

        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (scale <= 0)
        {
            scale = 1;
        }

        _scale = scale;

        NativeMethods.RECT wall = WindowSystem.GetWallScreenRect(_hwnd);
        int wallWidth = wall.Right - wall.Left;
        int wallHeight = wall.Bottom - wall.Top;
        int titleHeight = (int)Math.Round(TitleHeightDip * scale);
        int toolbarHeight = Math.Min(Math.Max(0, wallHeight), (int)Math.Round(ToolbarHeightDip * scale));
        int releaseWidth = (int)Math.Round(TitleButtonWidthDip * scale);

        // Cells sit below the toolbar: compute with the height minus the toolbar, then shift everything down.
        // Gap of 12 logical pixels.
        IReadOnlyList<WallSlot> slots = _state.Slots;
        int gap = (int)Math.Round(GridLayout.DefaultGap * scale);
        int border = Math.Max(1, (int)Math.Round(BorderDip * scale));
        GridResult computed = GridLayout.Compute(slots.Count, wallWidth, wallHeight - toolbarHeight, gap);
        GridResult layout = computed with
        {
            Cells = computed.Cells.Select(c => c with { Y = c.Y + toolbarHeight }).ToList(),
        };
        _layout = layout;
        _titleHeightPx = titleHeight;
        _buttonWidthPx = releaseWidth;
        _borderPx = border;

        for (int i = 0; i < slots.Count; i++)
        {
            WallSlot slot = slots[i];
            CellRect cell = layout.Cells[i];

            // A 1-pixel light border per cell; content goes inside the border.
            AddBorder(cell, scale);
            CellRect inner = Inset(cell, border);
            CellParts parts = CellContent.Split(inner, titleHeight);

            // Title bar on top, with "Pop out" then ✕ on the right (✕ rightmost); both buttons always keep their place;
            // the text area always excludes the buttons, so the title does not jump when they appear or hide. Buttons are hidden by default and shown only on the cell under the mouse.
            // No text labels: text area, "Pop out", ✕ from left to right.
            CellTitleButtons buttons = CellContent.TitleButtons(parts.Title, releaseWidth);

            if (slot.Kind == WallSlotKind.Window)
            {
                long id = slot.WindowId!.Value;
                AddRect(parts.Thumbnail, CellBackground, scale);
                _thumbnails.Place(id, parts.Thumbnail);
                _thumbnailAreas[id] = parts.Thumbnail;

                _cellButtons.Add((
                    AddTitleButton(buttons.Release, ReleaseGlyph, UiStrings.Get(_lang, UiTextKey.PopOut), "TitleReleaseButton", () => OnReleaseClick(id), scale),
                    AddTitleButton(buttons.Close, CloseGlyph, UiStrings.Get(_lang, UiTextKey.Close), "TitleCloseButton", () => OnCloseClick(id), scale)));
                _titleBlocks[id] = AddText(
                    buttons.Text, _titles.TryGetValue(id, out string? title) ? title : string.Empty, scale, 13);
            }
            else
            {
                _failedCellElements[i] = AddRect(parts.Thumbnail, FailedBackground, scale);
                AddCenteredText(parts.Thumbnail, UiStrings.Format(_lang, UiTextKey.OpenFailedCell, slot.Url), scale);

                // A failed cell has no window to close: ✕ and "Pop out" do the same thing, removing the cell and its URL.
                int index = i;
                _cellButtons.Add((
                    AddTitleButton(buttons.Release, ReleaseGlyph, UiStrings.Get(_lang, UiTextKey.PopOut), "TitleReleaseButton", () => OnReleaseFailedClick(index), scale),
                    AddTitleButton(buttons.Close, CloseGlyph, UiStrings.Get(_lang, UiTextKey.Close), "TitleCloseButton", () => OnReleaseFailedClick(index), scale)));
                AddText(buttons.Text, slot.Url, scale, 13);
            }
        }

        // Cells changed but the mouse did not move: recompute the shown cell from the current mouse position.
        UpdateTitleButtons();
    }

    /// <summary>
    /// Switches the title bar buttons by the cell under the mouse: only that cell shows them; none are shown when the mouse is not on a cell,
    /// not in grid mode, or a drag-to-swap is in progress. The rule is in <see cref="TitleBarButtons"/>.
    /// </summary>
    private void UpdateTitleButtons()
    {
        int hovered = -1;
        if (!_closed && _layout is not null && IsMouseOver)
        {
            hovered = CellIndexAt(Mouse.GetPosition(this));
        }

        bool gridMode = _state.Mode == WallMode.Grid && !_state.IsWallMinimized && !_state.RestorePending;
        int visible = TitleBarButtons.VisibleCell(hovered, _cellButtons.Count, gridMode, _drag.IsDragging);
        for (int i = 0; i < _cellButtons.Count; i++)
        {
            Visibility v = i == visible ? Visibility.Visible : Visibility.Hidden;
            (Button? release, Button? close) = _cellButtons[i];
            if (release is not null)
            {
                release.Visibility = v;
            }

            if (close is not null)
            {
                close.Visibility = v;
            }
        }
    }

    private static CellRect Inset(CellRect r, int by) =>
        new(r.X + by, r.Y + by, Math.Max(0, r.Width - 2 * by), Math.Max(0, r.Height - 2 * by));

    /// <summary>Cell frame (a 1-pixel light border); does not block the mouse.</summary>
    private void AddBorder(CellRect r, double scale)
    {
        var frame = new Border
        {
            Width = r.Width / scale,
            Height = r.Height / scale,
            BorderBrush = CellBorderBrush,
            BorderThickness = new Thickness(BorderDip),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(frame, r.X / scale);
        Canvas.SetTop(frame, r.Y / scale);
        CellCanvas.Children.Add(frame);
    }

    /// <summary>
    /// Toolbar: "+ Add page" is shown only in grid mode, enabled per <see cref="AddPicker.CanAdd"/>;
    /// "no windows" and the config file location are shown when there are no managed windows but failed cells remain.
    /// </summary>
    private void UpdateToolbar()
    {
        AddWindowButton.Visibility = _state.Mode == WallMode.Expanded ? Visibility.Collapsed : Visibility.Visible;
        // Disabled while startup URLs launch and while "+ Add page" looks for its window; also disabled when the cells (failed cells included) are full.
        AddWindowButton.IsEnabled = !_closed && AddPageFlow.Begin(_state, AddBusy) is null;

        ExistingButtonState existing = AddPicker.ExistingButton(_state, _candidateCount, AddBusy);
        ExistingWindowButton.Content = UiStrings.Format(_lang, UiTextKey.AddExisting, existing.Count);
        ExistingWindowButton.Visibility = !_closed && existing.Visible ? Visibility.Visible : Visibility.Collapsed;
        ExistingWindowButton.IsEnabled = !_closed && existing.Enabled;

        bool onlyFailed = _state.Mode != WallMode.Empty && !_state.HasManagedWindows && !_launching;
        NoWindowText.Text = onlyFailed ? UiStrings.Format(_lang, UiTextKey.NoWindowsInline, _configPath) : string.Empty;
        NoWindowText.Visibility = onlyFailed ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>"Pop out" icon: a box with a diagonal arrow (open in new window, Segoe MDL2 Assets / Segoe Fluent Icons U+E8A7).</summary>
    private const string ReleaseGlyph = "";

    /// <summary>✕ icon: the window close button (Segoe MDL2 Assets / Segoe Fluent Icons U+E8BB).</summary>
    private const string CloseGlyph = "";

    /// <summary>
    /// A title bar icon button, ✕ or "Pop out": styles are resources in MainWindow.xaml (flat; ✕ turns red with a white glyph on hover);
    /// after the mouse rests on it for about 0.5 seconds a text tooltip appears below the icon, and it disappears when the mouse leaves.
    /// Hidden by default; <see cref="UpdateTitleButtons"/> decides which cell shows it. Not placed when the width is 0 (returns null).
    /// </summary>
    private Button? AddTitleButton(CellRect r, string glyph, string tip, string styleKey, Action onClick, double scale)
    {
        if (r.Width <= 0 || r.Height <= 0)
        {
            return null;
        }

        var button = new Button
        {
            Content = glyph,
            ToolTip = tip,
            Width = r.Width / scale,
            Height = r.Height / scale,
            Style = (Style)FindResource(styleKey),
            Visibility = Visibility.Hidden,
        };
        // Tooltip: appears after resting about 0.5 seconds; BetweenShowDelay = 0 makes it wait again even when moving quickly to another icon
        // right after a tooltip was shown, so it never pops up at once; placed below the icon.
        ToolTipService.SetInitialShowDelay(button, (int)TitleBarButtons.TooltipDelay.TotalMilliseconds);
        ToolTipService.SetBetweenShowDelay(button, 0);
        ToolTipService.SetPlacement(button, System.Windows.Controls.Primitives.PlacementMode.Bottom);
        ToolTipService.SetShowDuration(button, TooltipShowDurationMs);
        System.Windows.Automation.AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => onClick();
        Canvas.SetLeft(button, r.X / scale);
        Canvas.SetTop(button, r.Y / scale);
        CellCanvas.Children.Add(button);
        return button;
    }

    private Border AddRect(CellRect r, Brush background, double scale)
    {
        var border = new Border
        {
            Width = r.Width / scale,
            Height = r.Height / scale,
            Background = background,
        };
        Canvas.SetLeft(border, r.X / scale);
        Canvas.SetTop(border, r.Y / scale);
        CellCanvas.Children.Add(border);
        return border;
    }

    /// <summary>Single-line text, truncated with "…" when too long.</summary>
    private TextBlock AddText(CellRect r, string text, double scale, double fontSize)
    {
        var block = new TextBlock
        {
            Text = text,
            Width = r.Width / scale,
            Height = r.Height / scale,
            Foreground = TitleForeground,
            FontSize = fontSize,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Padding = new Thickness(4, 3, 4, 0),
        };
        Canvas.SetLeft(block, r.X / scale);
        Canvas.SetTop(block, r.Y / scale);
        CellCanvas.Children.Add(block);
        return block;
    }

    /// <summary>Multi-line text in the center of a cell; each line is single-line and truncated with "…" when too long.</summary>
    private void AddCenteredText(CellRect r, string text, double scale)
    {
        var inner = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (string line in text.Split('\n'))
        {
            inner.Children.Add(new TextBlock
            {
                Text = line,
                Foreground = TitleForeground,
                FontSize = 14,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Padding = new Thickness(8, 2, 8, 2),
            });
        }

        var grid = new Grid { Width = r.Width / scale, Height = r.Height / scale };
        grid.Children.Add(inner);
        Canvas.SetLeft(grid, r.X / scale);
        Canvas.SetTop(grid, r.Y / scale);
        CellCanvas.Children.Add(grid);
    }

    /// <summary>Maximum wait after sending close requests; windows still present are left for the user.</summary>
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(2);

    /// <summary>Interval for checking, while waiting, whether all managed windows have closed.</summary>
    private static readonly TimeSpan ExitPoll = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Closing the wall. The first close is cancelled and the exit flow starts: remove thumbnails, send each managed window a "close window" request,
    /// hide the wall; once all are closed or 2 seconds pass, move windows still present back to a visible position, add their taskbar buttons back, then exit (within 3 seconds).
    /// Operations on browser windows never wait for them to respond; no process is ever forcibly ended.
    /// </summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_exitDone)
        {
            return;
        }

        e.Cancel = true;
        if (_closed)
        {
            return; // Exit flow in progress
        }

        // While the wall is minimized, a minimized window's coordinates are not a visible position; use the last valid wall area. Taken first, since the wall is hidden afterwards.
        NativeMethods.RECT? wall = CurrentWallRect();
        _exitClock.Restart();
        _closed = true;
        _checkTimer.Stop();
        _dragOutTimer.Stop();
        _addressBar.Stop(); // Only sets the stop flag; does not wait for the background thread
        _flat.Stop();
        _notice.Shutdown();
        _drag.Cancel();
        _ghost.Close();

        // Cancel deferred actions not yet run and the title bar button press check.
        CancelDeferred();
        _captionPoll.Stop();

        if (_foregroundHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }

        _thumbnails?.Dispose();
        _thumbnails = null;
        _button.Close();

        // Windows closed together with the wall do not update the last page list.
        _session.BeginExit();
        foreach (WallAction action in _state.BeginExit())
        {
            if (action.Kind == WallActionKind.CloseWindow)
            {
                WindowSystem.RequestClose(new IntPtr(action.WindowId!.Value));
            }
        }

        Hide();

        if (_state.WindowIds.Count == 0)
        {
            FinishExit(wall);
            return;
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = ExitPoll };
        timer.Tick += (_, _) =>
        {
            bool anyAlive = _state.WindowIds.Any(id => NativeMethods.IsWindow(new IntPtr(id)));
            if (anyAlive && clock.Elapsed < ExitWait)
            {
                return;
            }

            timer.Stop();
            FinishExit(wall);
        };
        timer.Start();
    }

    /// <summary>
    /// Moves managed windows still present back to a visible position on the wall area, adds their taskbar buttons back, then exits.
    /// Taskbar calls run in the background; waits at most <see cref="ExitTaskbarWait"/> more (without blocking the UI thread), so exit finishes within 3 seconds.
    /// </summary>
    private async void FinishExit(NativeMethods.RECT? wall)
    {
        var alive = _state.WindowIds.Where(id => NativeMethods.IsWindow(new IntPtr(id))).ToList();
        foreach (WallAction action in _state.FinishExit(alive))
        {
            if (action.Kind == WallActionKind.ReleaseOnExit)
            {
                long id = action.WindowId!.Value;
                WindowSystem.ReleaseOnExitAsync(new IntPtr(id), wall);
                _taskbar.Show(id, CellOf(id));
            }
        }

        await _taskbar.WhenIdle(ExitTaskbarWait);
        _taskbar.Dispose();

        // Before closing, let queued diagnostics lines reach the file: at most 500 ms, and the whole exit stays within 3 seconds.
        // If the wait itself fails, exit as usual.
        try
        {
            await _diagnostics.WhenFlushed(ExitBudget.LogFlushWait(_exitClock.Elapsed));
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        _exitDone = true;
        Application.Current.Shutdown();
    }

    /// <summary>
    /// The nudge's widening, decided on the UI thread at the moment it runs: only for the same nudge and when allowed (<see cref="NudgeGate.CanNudge"/>)
    /// is the window set to the current wall area plus 1 pixel of width; otherwise it is cancelled without using up the repair chance.
    /// Asynchronous SetWindowPos; does not change Z order or the foreground, and does not wait for the other side.
    /// </summary>
    private void NudgeNow(long windowId, long token)
    {
        CellRect? wall = CurrentWallCellRect();
        bool allowed = !_closed
            && wall is not null
            && _flat.IsCurrentNudge(windowId, token)
            && NudgeGate.CanNudge(_state, windowId);

        // Recorded as nudged only when really sent (window exists, not minimized, SetWindowPos succeeded); otherwise cancelled without using up the repair chance.
        NudgeGate.Dispatch(
            allowed,
            () => WindowSystem.SetRectAsync(new IntPtr(windowId), NudgeGate.Target(wall!.Value, widened: true)),
            () => _flat.NudgeSent(windowId, token, CellOf(windowId)),
            () => _flat.NudgeNotSent(windowId, token));
    }

    /// <summary>
    /// The restore 500 ms after a nudge: on the UI thread, at the moment it runs, puts the window back to the current wall area's size (never a remembered old size).
    /// Skipped while the wall is minimized or a restore is pending (the wall parks again on restore anyway).
    /// </summary>
    private void NudgeRestoreNow(long windowId, long token)
    {
        if (_closed
            || !_flat.IsCurrentNudge(windowId, token)
            || !NudgeGate.CanRestore(_state, windowId)
            || CurrentWallCellRect() is not CellRect wall)
        {
            return;
        }

        WindowSystem.SetRectAsync(new IntPtr(windowId), NudgeGate.Target(wall, widened: false));
    }

    /// <summary>The current wall area (screen coordinates, physical pixels); null when unavailable.</summary>
    private CellRect? CurrentWallCellRect()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return null;
        }

        NativeMethods.RECT r = WindowSystem.GetWallScreenRect(_hwnd);
        return r.Right > r.Left && r.Bottom > r.Top ? new CellRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) : null;
    }

    /// <summary>Passes the current managed windows (with cell positions) and whether the wall can be checked to the flat-color check.</summary>
    private void UpdateFlatChecker()
    {
        var cells = new List<(long WindowId, int Cell)>();
        for (int i = 0; i < _state.Slots.Count; i++)
        {
            if (_state.Slots[i].Kind == WallSlotKind.Window)
            {
                cells.Add((_state.Slots[i].WindowId!.Value, i + 1));
            }
        }

        _flat.SetWindows(cells);
        _flat.SetActive(!_closed && !_state.IsWallMinimized);
    }

    /// <summary>The window's cell position (1-based), null when not on a cell (for the diagnostics log).</summary>
    private int? CellOf(long windowId)
    {
        int index = _state.Slots.ToList().FindIndex(s => s.Kind == WallSlotKind.Window && s.WindowId == windowId);
        return index >= 0 ? index + 1 : null;
    }
}
