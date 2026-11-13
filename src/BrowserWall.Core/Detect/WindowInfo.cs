namespace BrowserWall.Core.Detect;

/// <summary>
/// Data of one top-level window (enumerated and provided by the App). <see cref="ExecutablePath"/> is null when unreadable;
/// <see cref="ProcessId"/> is the owning process id, null when unreadable (used to identify the wall's browser process).
/// </summary>
public sealed record WindowInfo(
    long Id,
    string? ExecutablePath,
    string ClassName,
    string Title,
    bool Visible,
    uint? ProcessId = null);
