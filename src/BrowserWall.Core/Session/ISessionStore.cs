namespace BrowserWall.Core.Session;

/// <summary>Result of reading the last page list file.</summary>
public enum SessionReadStatus
{
    /// <summary>The file does not exist.</summary>
    Missing,

    /// <summary>Content was read.</summary>
    Read,

    /// <summary>The file exists but reading failed (for example, it is locked).</summary>
    Unreadable,
}

/// <summary>
/// Reads and writes the last page list file (<c>%LOCALAPPDATA%\BrowserWall\session.json</c>).
/// The App provides the real file system; tests provide a fake. Deliberately has no delete method (an invalid list file is
/// never rewritten or deleted by the loader).
/// </summary>
public interface ISessionStore
{
    /// <summary>Reads the whole file. <paramref name="bytes"/> is non-null only for <see cref="SessionReadStatus.Read"/>.</summary>
    SessionReadStatus Read(out byte[]? bytes);

    /// <summary>Rewrites the whole file. Returns false on failure; never throws.</summary>
    bool TryWrite(byte[] bytes);
}
