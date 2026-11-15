using BrowserWall.Core.Detect;
using BrowserWall.Core.Text;

namespace BrowserWall.Tests;

public class DragOutNoticeTests
{
    [Fact(DisplayName = "DragOut_AddedNoticeOnlyWhenExpanded")]
    public void DragOut_AddedNoticeOnlyWhenExpanded()
    {
        var expected = new (bool Expanded, int Added, int Full, DragOutNoticeKind Kind)[]
        {
            (false, 1, 0, DragOutNoticeKind.None),
            (true, 1, 0, DragOutNoticeKind.Added),
            (true, 2, 0, DragOutNoticeKind.Added),
            (true, 0, 0, DragOutNoticeKind.None),
            (false, 0, 0, DragOutNoticeKind.None),
            (true, 1, 1, DragOutNoticeKind.WallFull),
            (false, 0, 1, DragOutNoticeKind.WallFull),
            (false, 1, 1, DragOutNoticeKind.WallFull),
            (true, 0, 2, DragOutNoticeKind.WallFull),
        };

        foreach ((bool expanded, int added, int full, DragOutNoticeKind kind) in expected)
        {
            Assert.True(kind == DragOutNotice.Choose(expanded, added, full), $"expanded={expanded} added={added} full={full}");
        }

        Assert.Null(DragOutNotice.TextKey(DragOutNoticeKind.None));
        Assert.Equal(UiTextKey.WallFull, DragOutNotice.TextKey(DragOutNoticeKind.WallFull));
        Assert.Equal(UiTextKey.DragOutAdded, DragOutNotice.TextKey(DragOutNoticeKind.Added));
        Assert.Equal("新視窗已加入牆", UiStrings.Get(UiLanguage.ZhTw, UiTextKey.DragOutAdded));
        Assert.Equal("New window added to the wall", UiStrings.Get(UiLanguage.En, UiTextKey.DragOutAdded));
    }
}
