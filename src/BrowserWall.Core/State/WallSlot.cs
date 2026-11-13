namespace BrowserWall.Core.State;

/// <summary>Kind of cell.</summary>
public enum WallSlotKind
{
    /// <summary>A managed window.</summary>
    Window,

    /// <summary>Failed to open: shows "failed to open" and the URL.</summary>
    Failed,
}

/// <summary>
/// One cell on the wall. <see cref="Url"/> is the cell's current URL (the "last seen URL"):
/// cells the wall opened itself and failed cells start with the URL they were opened with; adopted, auto-added
/// and new-page cells start with an empty string (no URL), and are later updated by <see cref="Session.LastSeenUrl"/>.
/// <see cref="SessionEntry"/> is the startup URL's id (issued by <see cref="Session.SessionTracker"/>), used only to tell
/// which startup URLs already have a cell; null for other cells.
/// </summary>
public sealed record WallSlot(WallSlotKind Kind, long? WindowId, string Url, int? SessionEntry = null)
{
    public static WallSlot ForWindow(long windowId, string url, int? sessionEntry = null) =>
        new(WallSlotKind.Window, windowId, url, sessionEntry);

    public static WallSlot ForFailed(string url, int? sessionEntry = null) =>
        new(WallSlotKind.Failed, null, url, sessionEntry);
}
