using BrowserWall.Core.Layout;
using BrowserWall.Core.State;

namespace BrowserWall.Core.Health;

/// <summary>
/// Whether a nudge may act "right now" and to what size. The App calls it on the UI thread at the moment it is about to act on the window;
/// it always uses the current state machine and the current wall area, never a size remembered earlier, so moving, resizing or relaying out the wall in between never applies a stale size.
/// </summary>
public static class NudgeGate
{
    /// <summary>
    /// Whether "widen" may be sent: still a managed window, wall not minimized, no pending restore, not pressing the wall's title bar minimize or close button,
    /// no pending deferred park, wall not closing. Otherwise the App voids this nudge (the repair chance is not used up).
    /// </summary>
    public static bool CanNudge(WallState state, long windowId)
    {
        ArgumentNullException.ThrowIfNull(state);
        return IsManaged(state, windowId)
            && !state.IsExiting
            && !state.IsWallMinimized
            && !state.RestorePending
            && !state.CaptionPressPending
            && !state.ActivationParkPending;
    }

    /// <summary>
    /// Whether "restore" may run: still a managed window, wall not minimized, no pending restore, wall not closing.
    /// Not while the wall is minimized or a restore is pending: restoring the wall re-parks every managed window to the current wall area anyway.
    /// </summary>
    public static bool CanRestore(WallState state, long windowId)
    {
        ArgumentNullException.ThrowIfNull(state);
        return IsManaged(state, windowId)
            && !state.IsExiting
            && !state.IsWallMinimized
            && !state.RestorePending;
    }

    /// <summary>
    /// Sends the widen and reports the result: only when <paramref name="allowed"/> is true and <paramref name="trySend"/> reports it was actually sent
    /// is <paramref name="sent"/> called (mark nudged, schedule restore and recheck); when not eligible, or the send was skipped or failed (for example the window was just minimized),
    /// <paramref name="notSent"/> is called (void it; the repair chance is not used up). Returns whether it was sent.
    /// </summary>
    public static bool Dispatch(bool allowed, Func<bool> trySend, Action sent, Action notSent)
    {
        ArgumentNullException.ThrowIfNull(trySend);
        ArgumentNullException.ThrowIfNull(sent);
        ArgumentNullException.ThrowIfNull(notSent);
        if (allowed && trySend())
        {
            sent();
            return true;
        }

        notSent();
        return false;
    }

    /// <summary>Target position and size: the current wall area (parked and expanded windows both equal the wall area); when widened, width plus <see cref="NudgeTracker.NudgePixels"/>.</summary>
    public static CellRect Target(CellRect wall, bool widened) =>
        widened ? wall with { Width = wall.Width + NudgeTracker.NudgePixels } : wall;

    private static bool IsManaged(WallState state, long windowId) => state.WindowIds.Contains(windowId);
}
