using System.IO;
using BrowserWall.Core.Config;
using BrowserWall.Core.Session;

namespace BrowserWall.App;

/// <summary>
/// The last page list file: only reads and writes %LOCALAPPDATA%\BrowserWall\session.json and a temp file in the same folder.
/// There is no code that deletes session.json.
/// </summary>
internal sealed class SessionStore : ISessionStore
{
    private readonly string _path;
    private readonly string _tempPath;

    public SessionStore(IConfigEnvironment env)
    {
        _path = SessionLoader.FilePath(env);
        _tempPath = _path + ".tmp";
    }

    /// <summary>Location of the list file (shown in the dialog).</summary>
    public string FilePath => _path;

    public SessionReadStatus Read(out byte[]? bytes)
    {
        bytes = null;
        try
        {
            if (!File.Exists(_path))
            {
                return SessionReadStatus.Missing;
            }

            bytes = File.ReadAllBytes(_path);
            return SessionReadStatus.Read;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            bytes = null;
            return SessionReadStatus.Unreadable;
        }
    }

    /// <summary>Writes a temp file first and then replaces, so a half-written file is never left; returns false on failure without throwing.</summary>
    public bool TryWrite(byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(_tempPath, bytes);
            File.Move(_tempPath, _path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
