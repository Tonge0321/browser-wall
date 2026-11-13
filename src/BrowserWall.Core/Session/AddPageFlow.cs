using BrowserWall.Core.Detect;
using BrowserWall.Core.State;

namespace BrowserWall.Core.Session;

/// <summary>
/// The "+ Add page" flow from the click to registering the cell. There is no URL input box:
/// <see cref="NewWindowUrl"/> is opened with the normal launch arguments; the new cell starts without a URL, and
/// <see cref="LastSeenUrl"/> records it later.
/// </summary>
public static class AddPageFlow
{
    /// <summary>URL used to open the new window for "+ Add page".</summary>
    public const string NewWindowUrl = "chrome://newtab/";

    /// <summary>
    /// On click: cannot add now (launching or adding, expanded, wall minimized, wall closing) → <see cref="AddPageBlock.NotAllowed"/>;
    /// cells full → <see cref="AddPageBlock.Full"/>; can add → null. Does not change state or write files. Never pushes out failed cells.
    /// </summary>
    public static AddPageBlock? Begin(WallState state, bool launching)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!AddPicker.CanAdd(state, launching))
        {
            return AddPageBlock.NotAllowed;
        }

        return state.HasFreeSlot ? null : AddPageBlock.Full;
    }

    /// <summary>
    /// Window found (<paramref name="windowId"/> has a value): register it as a cell without a URL, sync the list, then expand
    /// it (<see cref="WallState.ExpandNewWindow"/>); returns Found=true and the actions to run in order.
    /// Not found (null), or not registered because the wall is closing: Found=false and no cell is added (the App shows
    /// "could not add page", except while closing).
    /// </summary>
    public static AddPageResult Finish(WallState state, SessionTracker tracker, long? windowId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(tracker);

        if (windowId is not long id)
        {
            return new AddPageResult(false, Array.Empty<WallAction>());
        }

        IReadOnlyList<WallAction> added = state.AddWindow(id, string.Empty);
        if (added.Count == 0)
        {
            return new AddPageResult(false, Array.Empty<WallAction>());
        }

        tracker.Sync(state.Slots);
        return new AddPageResult(true, added.Concat(state.ExpandNewWindow(id)).ToList());
    }
}

/// <summary>Why "+ Add page" cannot start now (the App shows <c>AddNotAllowed</c> / <c>AddFull</c> from the string table).</summary>
public enum AddPageBlock
{
    NotAllowed,
    Full,
}

/// <summary>Result of "+ Add page": <see cref="Found"/> whether a new window was found and registered; <see cref="Actions"/> the actions to run in order.</summary>
public sealed record AddPageResult(bool Found, IReadOnlyList<WallAction> Actions);
