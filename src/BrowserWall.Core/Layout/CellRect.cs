namespace BrowserWall.Core.Layout;

/// <summary>Integer rectangle; coordinates are relative to the wall's top-left corner (pixels).</summary>
public readonly record struct CellRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    /// <summary>Whether the two rectangles overlap in area (merely touching does not count).</summary>
    public bool Overlaps(CellRect other) =>
        X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
}
