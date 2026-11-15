namespace BrowserWall.Core.Infra;

/// <summary>Broad kind of a step when closing the wall.</summary>
public enum ExitStepKind
{
    /// <summary>Records what later steps need.</summary>
    Prepare,

    /// <summary>Stops timers and background work, cancels drags and deferred actions, removes thumbnails, closes helper windows.</summary>
    Stop,

    /// <summary>Tells the last page list and the state machine that the wall is closing.</summary>
    Notify,

    /// <summary>Acts on windows: close requests, hiding the wall, putting back windows still open.</summary>
    Window,

    /// <summary>Waits, each with an upper limit.</summary>
    Wait,

    /// <summary>Ends the program.</summary>
    Finish,
}

/// <summary>One step when closing the wall.</summary>
public enum ExitStep
{
    /// <summary>Remember the current wall area, used later to put back windows that are still open.</summary>
    CaptureWallArea,

    /// <summary>Restart the exit clock (used to bound the diagnostics log wait).</summary>
    StartExitClock,

    /// <summary>Set the main window's "closing" flag.</summary>
    MarkClosing,

    /// <summary>Stop the once-a-second check timer.</summary>
    StopCheckTimer,

    /// <summary>Stop the dragged-out window detection timer.</summary>
    StopDragOutTimer,

    /// <summary>Stop the background address bar reading without waiting.</summary>
    StopAddressBar,

    /// <summary>Stop the background flat check without waiting.</summary>
    StopFlatCheck,

    /// <summary>Close the short notice above the wall area.</summary>
    CloseNotice,

    /// <summary>Cancel a cell drag.</summary>
    CancelDrag,

    /// <summary>Close the translucent window that follows the mouse while dragging.</summary>
    CloseDragGhost,

    /// <summary>Cancel deferred park or restore actions not yet run.</summary>
    CancelDeferred,

    /// <summary>Stop the title bar button press check.</summary>
    StopCaptionPoll,

    /// <summary>Stop listening for foreground window changes.</summary>
    UnhookForeground,

    /// <summary>Remove all thumbnails.</summary>
    RemoveThumbnails,

    /// <summary>Close the "back to grid" button window.</summary>
    CloseShrinkButton,

    /// <summary>Tell the last page list that windows closed with the wall must not update it.</summary>
    NotifySession,

    /// <summary>Tell the state machine the wall is closing and get the close requests to send.</summary>
    NotifyState,

    /// <summary>Send each managed window a "close window" request without waiting for it.</summary>
    SendCloseRequests,

    /// <summary>Hide the wall.</summary>
    HideWall,

    /// <summary>Wait until every managed window is closed, at most <see cref="ExitSequence.WindowCloseWait"/>.</summary>
    WaitForWindows,

    /// <summary>Move managed windows still open back into the wall area and give their taskbar buttons back.</summary>
    ReleaseRemaining,

    /// <summary>Wait for taskbar background calls, at most <see cref="ExitSequence.TaskbarWait"/>.</summary>
    WaitTaskbar,

    /// <summary>Stop the taskbar background work.</summary>
    StopTaskbar,

    /// <summary>Wait for queued diagnostics lines to reach the file, bounded by <see cref="ExitBudget.LogFlushWait"/>.</summary>
    WaitDiagnostics,

    /// <summary>Record that the exit is done so the next close goes through.</summary>
    MarkExitDone,

    /// <summary>End the program.</summary>
    Shutdown,
}

/// <summary>
/// The only definition of the order of the steps when closing the wall, and of the exit waits' upper limits.
/// The App runs <see cref="Steps"/> in order and does not decide the order itself.
/// </summary>
public static class ExitSequence
{
    /// <summary>Longest wait for managed windows to close after the close requests.</summary>
    public static readonly TimeSpan WindowCloseWait = TimeSpan.FromSeconds(2);

    /// <summary>How often to check, while waiting, whether all managed windows have closed.</summary>
    public static readonly TimeSpan WindowClosePoll = TimeSpan.FromMilliseconds(100);

    /// <summary>Longest wait for taskbar background calls before exiting.</summary>
    public static readonly TimeSpan TaskbarWait = TimeSpan.FromMilliseconds(500);

    private static readonly (ExitStep Step, ExitStepKind Kind)[] Order =
    {
        (ExitStep.CaptureWallArea, ExitStepKind.Prepare),
        (ExitStep.StartExitClock, ExitStepKind.Prepare),
        (ExitStep.MarkClosing, ExitStepKind.Stop),
        (ExitStep.StopCheckTimer, ExitStepKind.Stop),
        (ExitStep.StopDragOutTimer, ExitStepKind.Stop),
        (ExitStep.StopAddressBar, ExitStepKind.Stop),
        (ExitStep.StopFlatCheck, ExitStepKind.Stop),
        (ExitStep.CloseNotice, ExitStepKind.Stop),
        (ExitStep.CancelDrag, ExitStepKind.Stop),
        (ExitStep.CloseDragGhost, ExitStepKind.Stop),
        (ExitStep.CancelDeferred, ExitStepKind.Stop),
        (ExitStep.StopCaptionPoll, ExitStepKind.Stop),
        (ExitStep.UnhookForeground, ExitStepKind.Stop),
        (ExitStep.RemoveThumbnails, ExitStepKind.Stop),
        (ExitStep.CloseShrinkButton, ExitStepKind.Stop),
        (ExitStep.NotifySession, ExitStepKind.Notify),
        (ExitStep.NotifyState, ExitStepKind.Notify),
        (ExitStep.SendCloseRequests, ExitStepKind.Window),
        (ExitStep.HideWall, ExitStepKind.Window),
        (ExitStep.WaitForWindows, ExitStepKind.Wait),
        (ExitStep.ReleaseRemaining, ExitStepKind.Window),
        (ExitStep.WaitTaskbar, ExitStepKind.Wait),
        (ExitStep.StopTaskbar, ExitStepKind.Stop),
        (ExitStep.WaitDiagnostics, ExitStepKind.Wait),
        (ExitStep.MarkExitDone, ExitStepKind.Finish),
        (ExitStep.Shutdown, ExitStepKind.Finish),
    };

    /// <summary>The steps in the order they run.</summary>
    public static IReadOnlyList<ExitStep> Steps { get; } = Order.Select(o => o.Step).ToArray();

    /// <summary>The broad kind of <paramref name="step"/>.</summary>
    public static ExitStepKind KindOf(ExitStep step) => Order.First(o => o.Step == step).Kind;
}
