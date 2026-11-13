namespace BrowserWall.Core.State;

/// <summary>State of the wall.</summary>
public enum WallMode
{
    /// <summary>No cells on the wall: shows "no windows" and the config file location.</summary>
    Empty,

    /// <summary>Grid mode: all managed windows are parked.</summary>
    Grid,

    /// <summary>Exactly one managed window is expanded.</summary>
    Expanded,
}
