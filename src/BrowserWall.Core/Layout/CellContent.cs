namespace BrowserWall.Core.Layout;

/// <summary>Thumbnail area and title bar inside a cell.</summary>
public readonly record struct CellParts(CellRect Thumbnail, CellRect Title);

/// <summary>Text area and the two buttons in the title bar; a button has width 0 when it does not fit.</summary>
public readonly record struct CellTitleButtons(CellRect Text, CellRect Close, CellRect Release);

/// <summary>What a point inside a cell is used for.</summary>
public enum CellHit
{
    /// <summary>Outside the cell.</summary>
    None,

    /// <summary>Inside the cell (title text area and thumbnail area): can be dragged; a tap expands it.</summary>
    Body,

    /// <summary>The title bar ✕; does not start a drag.</summary>
    CloseButton,

    /// <summary>The title bar "Pop out" button; does not start a drag.</summary>
    ReleaseButton,
}

/// <summary>Position calculations for the content of a cell.</summary>
public static class CellContent
{
    /// <summary>The top <paramref name="titleHeight"/> pixels of the cell are the title bar; the rest below is the thumbnail area.</summary>
    public static CellParts Split(CellRect cell, int titleHeight)
    {
        int title = Math.Clamp(titleHeight, 0, Math.Max(0, cell.Height));
        int thumbHeight = Math.Max(0, cell.Height - title);
        return new CellParts(
            new CellRect(cell.X, cell.Y + title, cell.Width, thumbHeight),
            new CellRect(cell.X, cell.Y, cell.Width, title));
    }

    /// <summary>
    /// The right side of the title bar holds "Pop out" and ✕ in that order (✕ rightmost, "Pop out" to its left); the rest is the text area.
    /// Both buttons always keep their place: button width = min(<paramref name="buttonWidth"/>, title bar width ÷ 2); when narrow, the text area shrinks first (down to 0).
    /// The text area always excludes both buttons, whether they are shown or not (so the title does not jump when the buttons appear or hide).
    /// Only when the height is 0, the width is less than 2, or the given width is 0 are there no buttons and the text fills the bar.
    /// Left to right: text area, "Pop out", ✕; no text labels.
    /// </summary>
    public static CellTitleButtons TitleButtons(CellRect title, int buttonWidth)
    {
        int width = Math.Max(0, title.Width);
        int bw = Math.Min(Math.Max(0, buttonWidth), width / 2);
        if (bw == 0 || title.Height <= 0)
        {
            var empty = new CellRect(title.Right, title.Y, 0, title.Height);
            return new CellTitleButtons(title, empty, empty);
        }

        var close = new CellRect(title.Right - bw, title.Y, bw, title.Height);
        var release = new CellRect(close.X - bw, title.Y, bw, title.Height);
        var text = new CellRect(title.X, title.Y, release.X - title.X, title.Height);
        return new CellTitleButtons(text, close, release);
    }

    /// <summary>
    /// What point (x, y) is used for in cell <paramref name="cell"/>: on ✕ or "Pop out" → that button; elsewhere inside the cell → <see cref="CellHit.Body"/>;
    /// outside the cell → <see cref="CellHit.None"/> (the drag area is the whole cell including the title bar, except the buttons).
    /// </summary>
    public static CellHit HitTest(CellRect cell, int titleHeight, int buttonWidth, int x, int y)
    {
        if (!Contains(cell, x, y))
        {
            return CellHit.None;
        }

        CellTitleButtons buttons = TitleButtons(Split(cell, titleHeight).Title, buttonWidth);
        if (Contains(buttons.Close, x, y))
        {
            return CellHit.CloseButton;
        }

        if (Contains(buttons.Release, x, y))
        {
            return CellHit.ReleaseButton;
        }

        return CellHit.Body;
    }

    private static bool Contains(CellRect r, int x, int y) => x >= r.X && x < r.Right && y >= r.Y && y < r.Bottom;

    /// <summary>
    /// Scales <paramref name="sourceWidth"/>×<paramref name="sourceHeight"/> proportionally to fit inside <paramref name="area"/>, centered.
    /// When the source size is invalid or the area has no size, returns a zero-size rectangle at the area's center.
    /// </summary>
    public static CellRect FitCentered(int sourceWidth, int sourceHeight, CellRect area)
    {
        int centerX = area.X + area.Width / 2;
        int centerY = area.Y + area.Height / 2;
        if (sourceWidth <= 0 || sourceHeight <= 0 || area.Width <= 0 || area.Height <= 0)
        {
            return new CellRect(centerX, centerY, 0, 0);
        }

        int width;
        int height;

        // Compare aspect ratios sourceW/sourceH and areaW/areaH (cross-multiplied as long to avoid overflow and floating-point error)
        if ((long)sourceWidth * area.Height >= (long)area.Width * sourceHeight)
        {
            // Source is wider: fill the width
            width = area.Width;
            height = (int)((long)area.Width * sourceHeight / sourceWidth);
        }
        else
        {
            // Source is taller: fill the height
            height = area.Height;
            width = (int)((long)area.Height * sourceWidth / sourceHeight);
        }

        int x = area.X + (area.Width - width) / 2;
        int y = area.Y + (area.Height - height) / 2;
        return new CellRect(x, y, width, height);
    }
}
