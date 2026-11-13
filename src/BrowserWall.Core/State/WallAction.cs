namespace BrowserWall.Core.State;

/// <summary>Kinds of action the App performs after a state change.</summary>
public enum WallActionKind
{
    /// <summary>Expand a managed window.</summary>
    Expand,

    /// <summary>Park a managed window (not minimized).</summary>
    Park,

    /// <summary>Restore a minimized managed window to the normal state without taking focus.</summary>
    Restore,

    /// <summary>Show the "back to grid" button.</summary>
    ShowShrinkButton,

    /// <summary>Hide the "back to grid" button.</summary>
    HideShrinkButton,

    /// <summary>Cells were added or removed: lay out again.</summary>
    Relayout,

    /// <summary>Show "no windows" and the config file location.</summary>
    ShowEmpty,

    /// <summary>Minimize a managed window together with the wall.</summary>
    Minimize,

    /// <summary>Remove a managed window's taskbar button when it is registered.</summary>
    HideTaskbarButton,

    /// <summary>
    /// Pop out of the wall: restore to the center of the wall at 80% of the wall's width and height, bring to the front
    /// with focus, and add the taskbar button back.
    /// </summary>
    Release,

    /// <summary>Closing the wall: send a "close window" request to a managed window, without waiting or forcing it to end.</summary>
    CloseWindow,

    /// <summary>A managed window still alive after the wall closes: move it to a visible spot within the wall area and add the taskbar button back.</summary>
    ReleaseOnExit,

    /// <summary>
    /// Parking when the wall becomes the active window: deferred (about 300 ms) and done without waiting for the other window to respond;
    /// does not take the foreground and does not call anything that would bring that window to the foreground.
    /// </summary>
    ParkDeferred,
}

/// <summary>
/// One action for the App. <see cref="WindowId"/> is set only for Expand, Park, Restore, Minimize, HideTaskbarButton, Release,
/// CloseWindow, ReleaseOnExit and ParkDeferred.
/// </summary>
public readonly record struct WallAction(WallActionKind Kind, long? WindowId = null)
{
    public static WallAction Restore(long windowId) => new(WallActionKind.Restore, windowId);

    public static WallAction Expand(long windowId) => new(WallActionKind.Expand, windowId);

    public static WallAction Park(long windowId) => new(WallActionKind.Park, windowId);

    public static WallAction Minimize(long windowId) => new(WallActionKind.Minimize, windowId);

    public static WallAction HideTaskbarButton(long windowId) => new(WallActionKind.HideTaskbarButton, windowId);

    public static WallAction Release(long windowId) => new(WallActionKind.Release, windowId);

    public static WallAction CloseWindow(long windowId) => new(WallActionKind.CloseWindow, windowId);

    public static WallAction ReleaseOnExit(long windowId) => new(WallActionKind.ReleaseOnExit, windowId);

    public static WallAction ParkDeferred(long windowId) => new(WallActionKind.ParkDeferred, windowId);

    public static WallAction ShowShrinkButton { get; } = new(WallActionKind.ShowShrinkButton);

    public static WallAction HideShrinkButton { get; } = new(WallActionKind.HideShrinkButton);

    public static WallAction Relayout { get; } = new(WallActionKind.Relayout);

    public static WallAction ShowEmpty { get; } = new(WallActionKind.ShowEmpty);
}
