using BrowserWall.Core.Text;

namespace BrowserWall.Core.Detect;

/// <summary>Which notice to show above the wall area after one round of dragged-out window detection.</summary>
public enum DragOutNoticeKind
{
    /// <summary>No notice.</summary>
    None,

    /// <summary>"Wall is full; the new window was not added".</summary>
    WallFull,

    /// <summary>"New window added to the wall" (expanded mode only).</summary>
    Added,
}

/// <summary>Whether to show a notice after dragged-out windows were handled, and which one.</summary>
public static class DragOutNotice
{
    /// <summary>
    /// After one detection round: <paramref name="added"/> windows were auto-added, <paramref name="full"/> were not added because the wall was full.
    /// Any full window → "wall is full" (in any mode). Otherwise, in expanded mode with at least one added → "added". Otherwise nothing.
    /// In grid mode the new cell is visible, so no "added" notice is shown.
    /// </summary>
    public static DragOutNoticeKind Choose(bool expanded, int added, int full)
    {
        if (full > 0)
        {
            return DragOutNoticeKind.WallFull;
        }

        return expanded && added > 0 ? DragOutNoticeKind.Added : DragOutNoticeKind.None;
    }

    /// <summary>Text key of the notice; null for <see cref="DragOutNoticeKind.None"/>.</summary>
    public static UiTextKey? TextKey(DragOutNoticeKind kind) => kind switch
    {
        DragOutNoticeKind.WallFull => UiTextKey.WallFull,
        DragOutNoticeKind.Added => UiTextKey.DragOutAdded,
        _ => null,
    };
}
