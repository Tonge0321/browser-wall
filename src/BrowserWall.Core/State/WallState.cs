namespace BrowserWall.Core.State;

/// <summary>
/// The wall's state machine. It only records state and returns the actions the App should perform; it never touches a window.
/// Invariant: <see cref="WallMode.Empty"/> if and only if there are no cells.
/// </summary>
public sealed class WallState
{
    private static readonly IReadOnlyList<WallAction> None = Array.Empty<WallAction>();

    private readonly List<WallSlot> _slots = new();

    public WallMode Mode { get; private set; } = WallMode.Empty;

    private long? _expandedWindowId;

    /// <summary>
    /// The expanded managed window; null when not <see cref="WallMode.Expanded"/>.
    /// Every change (shrink, close, expanding another or expanding again) clears the "expanded because closing timed out" mark,
    /// which ends that timed-out expansion; only <see cref="ExpandTimedOutClose"/> sets the mark again after setting this.
    /// </summary>
    public long? ExpandedWindowId
    {
        get => _expandedWindowId;
        private set
        {
            _expandedWindowId = value;
            _closeExpandedId = null;
        }
    }

    /// <summary>All cells in cell order (initially registration order; <see cref="SwapCells"/> can swap them).</summary>
    public IReadOnlyList<WallSlot> Slots => _slots;

    /// <summary>All managed windows in cell order.</summary>
    public IReadOnlyList<long> WindowIds =>
        _slots.Where(s => s.Kind == WallSlotKind.Window).Select(s => s.WindowId!.Value).ToList();

    /// <summary>
    /// There is a free cell (managed-window cells plus failed cells are below the cell limit); needed by new page,
    /// adopting an existing window and auto-adding.
    /// </summary>
    public bool HasFreeSlot => _slots.Count < WallLimits.MaxCells;

    /// <summary>Remaining cells (cell limit − current cells); auto-adding subtracts one per window.</summary>
    public int FreeSlotCount => WallLimits.MaxCells - _slots.Count;

    /// <summary>
    /// Whether a dragged-out window can be auto-added now: not exiting, wall not minimized, no restore pending.
    /// When not, the dragged-out window keeps waiting and is not given up.
    /// </summary>
    public bool CanAutoAdd => IsAllowed(WallOp.AutoAddWindow);

    /// <summary>
    /// Whether the wall has any managed-window cell. When not, the wall shows "no windows" and the config file location;
    /// failed cells may still be there.
    /// </summary>
    public bool HasManagedWindows => _slots.Any(s => s.Kind == WallSlotKind.Window);

    /// <summary>
    /// Whether the wall is minimized. While minimized, <see cref="Mode"/> and <see cref="ExpandedWindowId"/> keep their values from
    /// before minimizing, as the mode to return to on restore.
    /// </summary>
    public bool IsWallMinimized => _phase is WallPhase.Minimized or WallPhase.ExitingMinimized;

    /// <summary>
    /// The wall is closing. While exiting, every method except <see cref="FinishExit"/> does nothing and changes no state
    /// (handling a cell close that is still open after 2 seconds, <see cref="CloseStillOpen"/>, still moves it to the expand queue).
    /// </summary>
    public bool IsExiting => _phase is WallPhase.Exiting or WallPhase.ExitingMinimized;

    /// <summary>
    /// The wall was restored from minimized and the managed windows' restore actions have not run yet (they are deferred).
    /// Meanwhile the wall becoming active does not count as shrinking, minimized managed windows are not restored automatically,
    /// and cell clicks are ignored; <see cref="SettleRestore"/> ends it.
    /// </summary>
    public bool RestorePending => _phase == WallPhase.RestorePending;

    /// <summary>
    /// The user is pressing the wall's title bar minimize or close button. Meanwhile there is no shrinking and deferred actions do not run;
    /// it ends with a real minimize (<see cref="WallMinimized"/>), a real close (<see cref="BeginExit"/>) or <see cref="CaptionPressEnded"/>.
    /// </summary>
    public bool CaptionPressPending => _captionPress;

    /// <summary>
    /// A deferred park scheduled after the wall became active has not run yet. Meanwhile moving or resizing the wall does not park
    /// synchronously; it joins the deferred park (<see cref="WallResized"/>). Ended by <see cref="DeferredParkTargets"/>,
    /// <see cref="WallMinimized"/> or <see cref="BeginExit"/>.
    /// </summary>
    public bool ActivationParkPending => _phase == WallPhase.ActivationParkPending;

    /// <summary>The wall's phase. The properties above are derived from it (and from the title bar button press mark).</summary>
    public WallPhase Phase => _phase;

    /// <summary>The only phase field.</summary>
    private WallPhase _phase = WallPhase.Normal;

    /// <summary>
    /// The title bar minimize or close button is pressed. Kept apart from the phase because it can hold together with
    /// <see cref="WallPhase.Normal"/>, <see cref="WallPhase.ActivationParkPending"/> and <see cref="WallPhase.RestorePending"/>.
    /// </summary>
    private bool _captionPress;

    /// <summary>Public operations whose permission depends on the phase.</summary>
    private enum WallOp
    {
        CloseCell,
        CloseStillOpen,
        ExpandTimedOutClose,
        ExpandNewWindow,
        SetWindowUrl,
        BeginExit,
        FinishExit,
        AddWindow,
        AdoptWindow,
        AutoAddWindow,
        SwapCells,
        ReleaseWindow,
        WallMinimized,
        WallRestored,
        SettleRestore,
        DeferredParkTargets,
        CaptionPressStarted,
        CaptionPressEnded,
        AddFailed,
        ReleaseFailed,
        WallActivated,
        WallActivatedShrink,
        ClickCell,
        Shrink,
        WindowClosed,
        SyncAlive,
        WindowMinimized,
        WallResized,
    }

    /// <summary>Sets of phases, one bit per <see cref="WallPhase"/>.</summary>
    [Flags]
    private enum Phases
    {
        Normal = 1 << (int)WallPhase.Normal,
        ActivationParkPending = 1 << (int)WallPhase.ActivationParkPending,
        RestorePending = 1 << (int)WallPhase.RestorePending,
        Minimized = 1 << (int)WallPhase.Minimized,
        Exiting = 1 << (int)WallPhase.Exiting,
        ExitingMinimized = 1 << (int)WallPhase.ExitingMinimized,

        /// <summary>Normal or activation park pending.</summary>
        Active = Normal | ActivationParkPending,

        /// <summary>Not minimized and not exiting.</summary>
        Awake = Active | RestorePending,

        /// <summary>Not exiting.</summary>
        NotExiting = Awake | Minimized,

        /// <summary>Exiting, minimized or not.</summary>
        Closing = Exiting | ExitingMinimized,

        All = NotExiting | Closing,
    }

    /// <summary>
    /// The one place that says in which phases each operation is allowed, and whether a title bar button press blocks it.
    /// Conditions on arguments or the mode stay in each method.
    /// </summary>
    private static readonly IReadOnlyDictionary<WallOp, (Phases Phases, bool BlockedByCaptionPress)> Allowed =
        new Dictionary<WallOp, (Phases, bool)>
        {
            [WallOp.CloseCell] = (Phases.Active, false),
            [WallOp.CloseStillOpen] = (Phases.All, false),
            [WallOp.ExpandTimedOutClose] = (Phases.Active, true),
            [WallOp.ExpandNewWindow] = (Phases.Active, true),
            [WallOp.SetWindowUrl] = (Phases.NotExiting, false),
            [WallOp.BeginExit] = (Phases.NotExiting, false),
            [WallOp.FinishExit] = (Phases.Closing, false),
            [WallOp.AddWindow] = (Phases.NotExiting, false),
            [WallOp.AdoptWindow] = (Phases.Awake, false),
            [WallOp.AutoAddWindow] = (Phases.Active, false),
            [WallOp.SwapCells] = (Phases.Active, false),
            [WallOp.ReleaseWindow] = (Phases.Awake, false),
            [WallOp.WallMinimized] = (Phases.Awake, false),
            [WallOp.WallRestored] = (Phases.Minimized, false),
            [WallOp.SettleRestore] = (Phases.RestorePending, true),
            [WallOp.DeferredParkTargets] = (Phases.Awake, false),
            [WallOp.CaptionPressStarted] = (Phases.Awake, false),
            [WallOp.CaptionPressEnded] = (Phases.Awake, false),
            [WallOp.AddFailed] = (Phases.NotExiting, false),
            [WallOp.ReleaseFailed] = (Phases.Awake, false),
            [WallOp.WallActivated] = (Phases.Awake, false),
            [WallOp.WallActivatedShrink] = (Phases.Active, false),
            [WallOp.ClickCell] = (Phases.Active, false),
            [WallOp.Shrink] = (Phases.Awake, false),
            [WallOp.WindowClosed] = (Phases.NotExiting, false),
            [WallOp.SyncAlive] = (Phases.NotExiting, false),
            [WallOp.WindowMinimized] = (Phases.Active, false),
            [WallOp.WallResized] = (Phases.Awake, false),
        };

    /// <summary>Whether <paramref name="op"/> is allowed in the current phase (and title bar button press state).</summary>
    private bool IsAllowed(WallOp op)
    {
        (Phases phases, bool blockedByCaptionPress) = Allowed[op];
        return (phases & (Phases)(1 << (int)_phase)) != 0 && !(blockedByCaptionPress && _captionPress);
    }

    /// <summary>Managed windows that were sent a close request (the managed windows at <see cref="BeginExit"/>); cleared after <see cref="FinishExit"/>.</summary>
    private List<long>? _closeRequested;

    /// <summary>Windows whose cell "close" was sent less than 2 seconds ago.</summary>
    private readonly HashSet<long> _cellCloseSent = new();

    /// <summary>Windows still alive 2 seconds after the cell "close" was sent, waiting to be expanded, in timeout order.</summary>
    private readonly List<long> _closeExpandQueue = new();

    /// <summary>A window expanded because closing timed out, which the user has not finished handling.</summary>
    private long? _closeExpandedId;

    /// <summary>
    /// Cell "close": in grid mode, wall not minimized, no restore pending, not exiting, and a managed window →
    /// returns <see cref="WallAction.CloseWindow"/> (the App sends it without waiting for the other window to respond). The cell is not
    /// removed now: after the window disappears <see cref="WindowClosed"/> / <see cref="SyncAlive"/> remove it and the list drops its URL.
    /// Not sent again for the same window while one is pending and not timed out. A failed cell's "close" is handled by the App calling <see cref="ReleaseFailed"/>.
    /// </summary>
    public IReadOnlyList<WallAction> CloseCell(long windowId)
    {
        if (!IsAllowed(WallOp.CloseCell)
            || Mode != WallMode.Grid
            || IndexOfWindow(windowId) < 0
            || _cellCloseSent.Contains(windowId)
            || _closeExpandQueue.Contains(windowId))
        {
            return None;
        }

        _cellCloseSent.Add(windowId);
        return new[] { WallAction.CloseWindow(windowId) };
    }

    /// <summary>
    /// The window still exists 2 seconds after its cell "close" was sent (called by the App): moves it to the expand queue and returns
    /// the result of <see cref="ExpandTimedOutClose"/>. Not dropped just because the wall is not in grid mode right now.
    /// </summary>
    public IReadOnlyList<WallAction> CloseStillOpen(long windowId)
    {
        if (!IsAllowed(WallOp.CloseStillOpen) || !_cellCloseSent.Remove(windowId) || IndexOfWindow(windowId) < 0)
        {
            return None;
        }

        _closeExpandQueue.Add(windowId);
        return ExpandTimedOutClose();
    }

    /// <summary>Windows whose close timed out and are still waiting to be expanded, in order.</summary>
    public IReadOnlyList<long> CloseExpandQueue => _closeExpandQueue.ToList();

    /// <summary>
    /// Handles the first window in the expand queue. The App calls it after <see cref="CloseStillOpen"/> and on the per-second liveness check.
    /// Kept while exiting, wall minimized, restore pending, or the title bar minimize / close button is pressed; while another cell expanded for a
    /// timed-out close is still open, waits for it to be handled. In grid mode expands directly; with another window expanded, switches
    /// (park the current one, expand the target), so only one is expanded at a time.
    /// </summary>
    public IReadOnlyList<WallAction> ExpandTimedOutClose()
    {
        _closeExpandQueue.RemoveAll(id => IndexOfWindow(id) < 0);
        if (_closeExpandedId is long c && !(Mode == WallMode.Expanded && ExpandedWindowId == c))
        {
            // Safety net: the mark is cleared when the expanded window changes (ExpandedWindowId setter), so this should not happen.
            _closeExpandedId = null;
        }

        if (_closeExpandQueue.Count == 0
            || !IsAllowed(WallOp.ExpandTimedOutClose)
            || _closeExpandedId is not null)
        {
            return None;
        }

        long target = _closeExpandQueue[0];
        if (Mode == WallMode.Expanded && ExpandedWindowId == target)
        {
            // The user already expanded it.
            _closeExpandQueue.RemoveAt(0);
            _closeExpandedId = target;
            return None;
        }

        if (Mode == WallMode.Grid)
        {
            _closeExpandQueue.RemoveAt(0);
            Mode = WallMode.Expanded;
            ExpandedWindowId = target;
            _closeExpandedId = target; // Set after the expanded window (setting it clears the mark)
            return new[] { WallAction.Expand(target), WallAction.ShowShrinkButton };
        }

        if (Mode == WallMode.Expanded && ExpandedWindowId is long current)
        {
            _closeExpandQueue.RemoveAt(0);
            ExpandedWindowId = target;
            _closeExpandedId = target;
            return new[] { WallAction.Park(current), WallAction.Expand(target), WallAction.ShowShrinkButton };
        }

        return None;
    }

    /// <summary>
    /// Expands the new window after "+ Add page" found and registered it. When managed, wall not minimized, no restore pending,
    /// title bar minimize / close button not pressed, and not exiting: grid mode → expand; expanded mode (the user opened another cell while waiting)
    /// → switch (park the current one, expand the new one), so only one is expanded at a time. Otherwise returns nothing and does not expand.
    /// </summary>
    public IReadOnlyList<WallAction> ExpandNewWindow(long windowId)
    {
        if (!IsAllowed(WallOp.ExpandNewWindow) || IndexOfWindow(windowId) < 0)
        {
            return None;
        }

        if (Mode == WallMode.Grid)
        {
            Mode = WallMode.Expanded;
            ExpandedWindowId = windowId;
            return new[] { WallAction.Expand(windowId), WallAction.ShowShrinkButton };
        }

        if (Mode == WallMode.Expanded && ExpandedWindowId is long current && current != windowId)
        {
            ExpandedWindowId = windowId;
            return new[] { WallAction.Park(current), WallAction.Expand(windowId), WallAction.ShowShrinkButton };
        }

        return None;
    }

    /// <summary>
    /// Replaces a managed-window cell's URL with the last seen URL. Returns false while exiting, when not found, or when unchanged.
    /// Failed cells have no window and are never changed. Called by <see cref="Session.LastSeenUrl"/>.
    /// </summary>
    public bool SetWindowUrl(long windowId, string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        int index = IndexOfWindow(windowId);
        if (!IsAllowed(WallOp.SetWindowUrl) || index < 0 || string.Equals(_slots[index].Url, url, StringComparison.Ordinal))
        {
            return false;
        }

        _slots[index] = _slots[index] with { Url = url };
        return true;
    }

    /// <summary>
    /// Starts closing the wall: sends a "close window" request to each managed window in registration order. Failed cells have no window
    /// and get nothing. Only the first call has an effect.
    /// </summary>
    public IReadOnlyList<WallAction> BeginExit()
    {
        if (!IsAllowed(WallOp.BeginExit))
        {
            return None;
        }

        // Closing while minimized still counts as minimized; pending restore, activation park and title bar press all end.
        _phase = IsWallMinimized ? WallPhase.ExitingMinimized : WallPhase.Exiting;
        _captionPress = false;
        _closeRequested = WindowIds.ToList();
        return _closeRequested.Select(WallAction.CloseWindow).ToList();
    }

    /// <summary>
    /// After the close requests (the App waits until all are closed or 2 seconds pass): managed windows still alive are moved back to a
    /// visible position and get their taskbar button back, in registration order. Valid only after <see cref="BeginExit"/>, and only the first call has an effect.
    /// </summary>
    public IReadOnlyList<WallAction> FinishExit(IEnumerable<long> aliveIds)
    {
        ArgumentNullException.ThrowIfNull(aliveIds);
        if (!IsAllowed(WallOp.FinishExit) || _closeRequested is null)
        {
            return None;
        }

        var alive = new HashSet<long>(aliveIds);
        List<WallAction> actions = _closeRequested.Where(alive.Contains).Select(WallAction.ReleaseOnExit).ToList();
        _closeRequested = null;
        return actions;
    }

    /// <summary>
    /// Registers a managed window. The new window is parked and its taskbar button removed; an expanded window is not affected.
    /// If the wall is minimized (minimized during startup) the new window is minimized with it.
    /// Not registered while exiting (wall closed during startup). <paramref name="sessionEntry"/> is the matching id in the last page list.
    /// </summary>
    public IReadOnlyList<WallAction> AddWindow(long windowId, string url, int? sessionEntry = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!IsAllowed(WallOp.AddWindow))
        {
            return None;
        }

        if (IndexOfWindow(windowId) >= 0)
        {
            throw new InvalidOperationException($"Window {windowId} is already registered.");
        }

        AddSlot(WallSlot.ForWindow(windowId, url, sessionEntry));
        WallAction place = IsWallMinimized ? WallAction.Minimize(windowId) : WallAction.Park(windowId);
        return new[] { place, WallAction.HideTaskbarButton(windowId), WallAction.Relayout };
    }

    /// <summary>
    /// Adds an existing window to the wall: park, remove the taskbar button, lay out again.
    /// Does nothing when the wall is minimized, in expanded mode, already registered, or the cells (including failed cells) are at the limit;
    /// does not push out failed cells.
    /// An adopted cell starts with no URL (empty string) and joins the last page list once a URL is recorded.
    /// </summary>
    public IReadOnlyList<WallAction> AdoptWindow(long windowId)
    {
        if (!IsAllowed(WallOp.AdoptWindow)
            || Mode == WallMode.Expanded
            || IndexOfWindow(windowId) >= 0
            || !HasFreeSlot)
        {
            return None;
        }

        AddSlot(WallSlot.ForWindow(windowId, string.Empty));
        return new[] { WallAction.Park(windowId), WallAction.HideTaskbarButton(windowId), WallAction.Relayout };
    }

    /// <summary>
    /// Auto-adds a dragged-out window: park, remove the taskbar button, lay out again. Works in grid and expanded mode;
    /// in expanded mode the expanded window stays expanded (<see cref="Mode"/> and <see cref="ExpandedWindowId"/> unchanged).
    /// Returns no action that takes the foreground or expands. The cell starts with no URL and joins the list once a URL is recorded.
    /// Does nothing when <see cref="CanAutoAdd"/> is false, already registered, or there is no free cell.
    /// </summary>
    public IReadOnlyList<WallAction> AutoAddWindow(long windowId)
    {
        if (!IsAllowed(WallOp.AutoAddWindow) || IndexOfWindow(windowId) >= 0 || !HasFreeSlot)
        {
            return None;
        }

        AddSlot(WallSlot.ForWindow(windowId, string.Empty));
        return new[] { WallAction.Park(windowId), WallAction.HideTaskbarButton(windowId), WallAction.Relayout };
    }

    /// <summary>
    /// Cell drag swap: swaps cells <paramref name="from"/> and <paramref name="to"/> and returns Relayout.
    /// Failed cells can be swapped too. Valid only in grid mode, wall not minimized, no restore pending, not exiting;
    /// the same cell or an index out of range (dropped outside the cells or the wall) changes nothing and returns nothing.
    /// </summary>
    public IReadOnlyList<WallAction> SwapCells(int from, int to)
    {
        if (!IsAllowed(WallOp.SwapCells)
            || Mode != WallMode.Grid
            || from < 0
            || from >= _slots.Count
            || to < 0
            || to >= _slots.Count
            || from == to)
        {
            return None;
        }

        (_slots[from], _slots[to]) = (_slots[to], _slots[from]);
        return new[] { WallAction.Relayout };
    }

    /// <summary>
    /// Pops a managed window out of the wall. The expanded window cannot be popped out; does nothing while the wall is minimized.
    /// </summary>
    public IReadOnlyList<WallAction> ReleaseWindow(long windowId)
    {
        if (!IsAllowed(WallOp.ReleaseWindow) || ExpandedWindowId == windowId || IndexOfWindow(windowId) < 0)
        {
            return None;
        }

        var actions = new List<WallAction> { WallAction.Release(windowId) };
        RemoveWindow(windowId, actions);
        actions.Add(LayoutAction());
        return actions;
    }

    /// <summary>The wall was minimized: all managed windows are minimized with it and the "back to grid" button is hidden.</summary>
    public IReadOnlyList<WallAction> WallMinimized()
    {
        if (!IsAllowed(WallOp.WallMinimized))
        {
            return None;
        }

        // A real minimize: the pending restore or activation park is cancelled and the title bar button press ends.
        // The mode is kept as the mode to return to.
        _phase = WallPhase.Minimized;
        _captionPress = false;
        var actions = WindowIds.Select(WallAction.Minimize).ToList();
        if (Mode == WallMode.Expanded)
        {
            actions.Add(WallAction.HideShrinkButton);
        }

        return actions;
    }

    /// <summary>
    /// The wall was restored from minimized: returns what things should look like after restoring — all managed windows restored and
    /// parked again, back to the mode before minimizing (when expanded, the same window expanded again with the "back to grid" button).
    /// With cells, sets <see cref="RestorePending"/>: the App runs only the actions that touch the wall's own window now, and the
    /// managed windows' actions are produced later by <see cref="SettleRestore"/> from the state at that time.
    /// </summary>
    public IReadOnlyList<WallAction> WallRestored()
    {
        if (!IsAllowed(WallOp.WallRestored))
        {
            return None;
        }

        if (Mode == WallMode.Empty)
        {
            _phase = WallPhase.Normal;
            return new[] { WallAction.ShowEmpty };
        }

        _phase = WallPhase.RestorePending;
        return RestoreActions();
    }

    /// <summary>
    /// The deferred restore: produces the restore actions from the current managed windows and mode, and ends <see cref="RestorePending"/>.
    /// Windows popped out or closed meanwhile are not included. During a title bar button press returns nothing and keeps <see cref="RestorePending"/>
    /// (no expanding, no foreground change); returns nothing when no restore is pending, the wall is minimized, or exiting.
    /// </summary>
    public IReadOnlyList<WallAction> SettleRestore()
    {
        if (!IsAllowed(WallOp.SettleRestore))
        {
            return None;
        }

        _phase = WallPhase.Normal;
        return Mode == WallMode.Empty ? new[] { WallAction.ShowEmpty } : RestoreActions();
    }

    /// <summary>
    /// When the deferred park runs (the App's deferred action fires): ends <see cref="ActivationParkPending"/> and returns those of the
    /// scheduled windows (<paramref name="scheduled"/>) that are still parked managed windows now. Windows popped out, closed or expanded again since are excluded.
    /// </summary>
    public IReadOnlyList<long> DeferredParkTargets(IEnumerable<long> scheduled)
    {
        ArgumentNullException.ThrowIfNull(scheduled);
        if (_phase == WallPhase.ActivationParkPending)
        {
            _phase = WallPhase.Normal;
        }

        if (!IsAllowed(WallOp.DeferredParkTargets))
        {
            return Array.Empty<long>();
        }

        var parked = new HashSet<long>(ParkedWindowIds);
        return scheduled.Where(parked.Contains).Distinct().ToList();
    }

    /// <summary>
    /// The user pressed the mouse on the wall's title bar minimize or close button: records the press, also while a restore is pending.
    /// Recorded only with cells, not minimized, and not exiting.
    /// </summary>
    public void CaptionPressStarted()
    {
        if (IsAllowed(WallOp.CaptionPressStarted) && Mode != WallMode.Empty)
        {
            _captionPress = true;
        }
    }

    /// <summary>
    /// The title bar button press ended (the App calls this after the mouse is released and Windows has handled the release). Still pending
    /// means neither minimize nor close happened = cancelled. Cancelled and the wall is still active: shrink as when the wall becomes active —
    /// with no pending restore, returns the same as <see cref="WallActivated"/>; with a pending restore, changes the target mode to grid and
    /// returns HideShrinkButton, and the managed windows are restored and parked by the later <see cref="SettleRestore"/>.
    /// If the wall is no longer active, only ends the record.
    /// </summary>
    public IReadOnlyList<WallAction> CaptionPressEnded(bool wallStillActive)
    {
        if (!_captionPress)
        {
            return None;
        }

        _captionPress = false;
        if (!wallStillActive || !IsAllowed(WallOp.CaptionPressEnded))
        {
            return None;
        }

        if (RestorePending)
        {
            if (Mode == WallMode.Expanded)
            {
                Mode = WallMode.Grid;
                ExpandedWindowId = null;
            }

            return new[] { WallAction.HideShrinkButton };
        }

        return WallActivated();
    }

    /// <summary>What things should look like after restoring: parked windows restored and parked; the expanded window restored, expanded, button shown; lay out again.</summary>
    private List<WallAction> RestoreActions()
    {
        var actions = new List<WallAction>();
        foreach (long id in ParkedWindowIds)
        {
            actions.Add(WallAction.Restore(id));
            actions.Add(WallAction.Park(id));
        }

        if (ExpandedWindowId is long expanded)
        {
            actions.Add(WallAction.Restore(expanded));
            actions.Add(WallAction.Expand(expanded));
            actions.Add(WallAction.ShowShrinkButton);
        }

        actions.Add(WallAction.Relayout);
        return actions;
    }

    /// <summary>
    /// Registers a failed cell. <paramref name="sessionEntry"/> is the matching id in the last page list.
    /// </summary>
    public IReadOnlyList<WallAction> AddFailed(string url, int? sessionEntry = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!IsAllowed(WallOp.AddFailed))
        {
            return None;
        }

        AddSlot(WallSlot.ForFailed(url, sessionEntry));
        return new[] { WallAction.Relayout };
    }

    /// <summary>
    /// "Pop out" and "close" on a failed cell (there is no window to close, so both buttons do the same): in grid mode removes cell <paramref name="index"/> (must be a failed cell).
    /// Returns Relayout when cells remain; when the last cell is removed the wall shows "no windows" and returns ShowEmpty.
    /// No window, so no window restore and no taskbar work. Does nothing otherwise.
    /// </summary>
    public IReadOnlyList<WallAction> ReleaseFailed(int index)
    {
        if (!IsAllowed(WallOp.ReleaseFailed)
            || Mode != WallMode.Grid
            || index < 0
            || index >= _slots.Count
            || _slots[index].Kind != WallSlotKind.Failed)
        {
            return None;
        }

        _slots.RemoveAt(index);
        if (_slots.Count == 0)
        {
            Mode = WallMode.Empty;
        }

        return new[] { LayoutAction() };
    }

    /// <summary>
    /// The wall became the active window. Expanded mode: counts as shrinking, and the expanded window is parked later (deferred);
    /// grid mode: each parked window's order is checked later (deferred). Returns only <see cref="WallActionKind.HideShrinkButton"/> and
    /// <see cref="WallActionKind.ParkDeferred"/>, never an action that brings another window to the foreground or rearranges windows synchronously right now.
    /// Does nothing with no windows, wall minimized, or exiting.
    /// <paramref name="captionButton"/>: the user is pressing the title bar minimize or close button, same as <see cref="CaptionPressStarted"/>; no shrinking
    /// (handled as minimize or close instead). No shrinking while a restore is pending (<see cref="RestorePending"/>).
    /// </summary>
    public IReadOnlyList<WallAction> WallActivated(bool captionButton = false)
    {
        if (!IsAllowed(WallOp.WallActivated))
        {
            return None;
        }

        if (captionButton)
        {
            CaptionPressStarted();
            return None;
        }

        if (!IsAllowed(WallOp.WallActivatedShrink))
        {
            return None;
        }

        if (Mode == WallMode.Expanded)
        {
            long id = ExpandedWindowId!.Value;
            Mode = WallMode.Grid;
            ExpandedWindowId = null;
            _phase = WallPhase.ActivationParkPending;
            return new[] { WallAction.HideShrinkButton, WallAction.ParkDeferred(id) };
        }

        if (Mode == WallMode.Grid)
        {
            List<WallAction> parks = ParkedWindowIds.Select(WallAction.ParkDeferred).ToList();
            if (parks.Count > 0)
            {
                _phase = WallPhase.ActivationParkPending;
            }

            return parks;
        }

        return None;
    }

    /// <summary>Click cell <paramref name="index"/> in grid mode.</summary>
    /// <remarks>Ignored while a restore is pending (the managed windows are still minimized).</remarks>
    public IReadOnlyList<WallAction> ClickCell(int index)
    {
        if (!IsAllowed(WallOp.ClickCell) || Mode != WallMode.Grid || index < 0 || index >= _slots.Count)
        {
            return None;
        }

        WallSlot slot = _slots[index];
        if (slot.Kind != WallSlotKind.Window)
        {
            return None;
        }

        long id = slot.WindowId!.Value;
        Mode = WallMode.Expanded;
        ExpandedWindowId = id;
        return new[] { WallAction.Expand(id), WallAction.ShowShrinkButton };
    }

    /// <summary>Shrink (the "back to grid" button).</summary>
    public IReadOnlyList<WallAction> Shrink()
    {
        if (!IsAllowed(WallOp.Shrink) || Mode != WallMode.Expanded)
        {
            return None;
        }

        long id = ExpandedWindowId!.Value;
        Mode = WallMode.Grid;
        ExpandedWindowId = null;
        return new[] { WallAction.Park(id), WallAction.HideShrinkButton };
    }

    /// <summary>A managed window was closed by the user.</summary>
    public IReadOnlyList<WallAction> WindowClosed(long windowId)
    {
        if (!IsAllowed(WallOp.WindowClosed))
        {
            return None;
        }

        var actions = new List<WallAction>();
        if (!RemoveWindow(windowId, actions))
        {
            return None;
        }

        actions.Add(LayoutAction());
        return actions;
    }

    /// <summary>
    /// Removes every managed window that no longer exists, given the list of windows still alive (the App checks once a second).
    /// The layout action is emitted only once.
    /// </summary>
    public IReadOnlyList<WallAction> SyncAlive(IEnumerable<long> aliveIds)
    {
        ArgumentNullException.ThrowIfNull(aliveIds);
        if (!IsAllowed(WallOp.SyncAlive))
        {
            return None;
        }

        var alive = new HashSet<long>(aliveIds);

        var actions = new List<WallAction>();
        bool removed = false;
        foreach (long id in WindowIds)
        {
            if (!alive.Contains(id))
            {
                removed |= RemoveWindow(id, actions);
            }
        }

        if (!removed)
        {
            return None;
        }

        actions.Add(LayoutAction());
        return actions;
    }

    /// <summary>
    /// A managed window was minimized by the user: restore it and park it again. The expanded window being minimized counts as shrinking.
    /// Automatic restore is paused while the wall is minimized.
    /// </summary>
    public IReadOnlyList<WallAction> WindowMinimized(long windowId)
    {
        // While a restore is pending the managed windows are still minimized anyway; the user did not minimize them.
        if (!IsAllowed(WallOp.WindowMinimized) || IndexOfWindow(windowId) < 0)
        {
            return None;
        }

        if (Mode == WallMode.Expanded && ExpandedWindowId == windowId)
        {
            Mode = WallMode.Grid;
            ExpandedWindowId = null;
            return new[] { WallAction.Restore(windowId), WallAction.Park(windowId), WallAction.HideShrinkButton };
        }

        return new[] { WallAction.Restore(windowId), WallAction.Park(windowId) };
    }

    /// <summary>
    /// The wall was moved or resized: parked windows are parked again, the expanded one expanded again to the new wall area, and the layout redone. State unchanged.
    /// </summary>
    public IReadOnlyList<WallAction> WallResized()
    {
        if (!IsAllowed(WallOp.WallResized))
        {
            return None;
        }

        if (Mode == WallMode.Empty)
        {
            return new[] { WallAction.ShowEmpty };
        }

        // While a restore is pending, only lay out again; SettleRestore handles the managed windows' positions.
        if (RestorePending)
        {
            return new[] { WallAction.Relayout };
        }

        // The deferred park after activation has not run yet, or a title bar button is pressed (e.g. dragging the wall by its title bar while expanded):
        // do not park synchronously; parked windows join the deferred park, which parks without waiting using the wall area at that time.
        if (ActivationParkPending || CaptionPressPending)
        {
            var deferredActions = ParkedWindowIds.Select(WallAction.ParkDeferred).ToList();
            if (deferredActions.Count > 0)
            {
                _phase = WallPhase.ActivationParkPending;
            }

            if (Mode == WallMode.Expanded && !CaptionPressPending)
            {
                // After shrinking the user clicked a cell to expand again: expanding is a normal operation and is adjusted to the new wall area as usual.
                deferredActions.Add(WallAction.Expand(ExpandedWindowId!.Value));
                deferredActions.Add(WallAction.ShowShrinkButton);
            }

            deferredActions.Add(WallAction.Relayout);
            return deferredActions;
        }

        var actions = new List<WallAction>();
        foreach (long id in WindowIds)
        {
            if (id != ExpandedWindowId)
            {
                actions.Add(WallAction.Park(id));
            }
        }

        if (Mode == WallMode.Expanded)
        {
            actions.Add(WallAction.Expand(ExpandedWindowId!.Value));
            actions.Add(WallAction.ShowShrinkButton);
        }

        actions.Add(WallAction.Relayout);
        return actions;
    }

    /// <summary>Parked managed windows (excluding the expanded one), in registration order.</summary>
    public IReadOnlyList<long> ParkedWindowIds => WindowIds.Where(id => id != ExpandedWindowId).ToList();

    private void AddSlot(WallSlot slot)
    {
        if (_slots.Count >= WallLimits.MaxCells)
        {
            throw new InvalidOperationException($"At most {WallLimits.MaxCells} cells.");
        }

        _slots.Add(slot);
        if (Mode == WallMode.Empty)
        {
            Mode = WallMode.Grid;
        }
    }

    /// <summary>
    /// Removes a managed window's cell; the expanded window counts as shrinking (not parked, since the window is gone). Emits no layout action.
    /// Removes only this cell; remaining failed cells stay (only this cell's URL is dropped from the list). With no cells left, becomes "no windows".
    /// </summary>
    private bool RemoveWindow(long windowId, List<WallAction> actions)
    {
        int index = IndexOfWindow(windowId);
        if (index < 0)
        {
            return false;
        }

        _slots.RemoveAt(index);
        _cellCloseSent.Remove(windowId);
        _closeExpandQueue.Remove(windowId);

        if (Mode == WallMode.Expanded && ExpandedWindowId == windowId)
        {
            Mode = WallMode.Grid;
            ExpandedWindowId = null;
            actions.Add(WallAction.HideShrinkButton);
        }

        if (_slots.Count == 0)
        {
            Mode = WallMode.Empty;
            ExpandedWindowId = null;
        }

        return true;
    }

    private WallAction LayoutAction() => Mode == WallMode.Empty ? WallAction.ShowEmpty : WallAction.Relayout;

    private int IndexOfWindow(long windowId) =>
        _slots.FindIndex(s => s.Kind == WallSlotKind.Window && s.WindowId == windowId);
}
