namespace BrowserWall.Core.State;

/// <summary>
/// The wall's phase. Independent of <see cref="WallMode"/> (no windows / grid / expanded): any mode can be in any phase.
/// Pressing the wall's title bar minimize or close button is tracked separately (<see cref="WallState.CaptionPressPending"/>)
/// and can hold together with <see cref="Normal"/>, <see cref="ActivationParkPending"/> and <see cref="RestorePending"/>.
/// </summary>
public enum WallPhase
{
    /// <summary>Nothing pending.</summary>
    Normal,

    /// <summary>A deferred park scheduled after the wall became active has not run yet.</summary>
    ActivationParkPending,

    /// <summary>The wall was restored from minimized and the managed windows' deferred restore has not run yet.</summary>
    RestorePending,

    /// <summary>The wall is minimized.</summary>
    Minimized,

    /// <summary>The wall is closing; it was not minimized when closing started.</summary>
    Exiting,

    /// <summary>The wall is closing; closing started while it was minimized, so it still counts as minimized.</summary>
    ExitingMinimized,
}
