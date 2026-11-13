using System.IO;
using BrowserWall.Core.Health;
using BrowserWall.Core.Launch;

namespace BrowserWall.App;

/// <summary>
/// "Always show full URLs": before launching the browser, if no browser is using the same <c>profileDir</c>, sets that one
/// setting in <c>&lt;profileDir&gt;\Default\Preferences</c> following the steps of <see cref="PreferencesWrite"/>:
/// write only when a change is really needed; first rename the existing backup to <c>Preferences.backup.old.json</c>, then
/// save the original as <c>%LOCALAPPDATA%\BrowserWall\Preferences.backup.json</c> (no write on failure), write the new
/// content to <c>%LOCALAPPDATA%\BrowserWall\Preferences.tmp</c> and replace; inside <c>profileDir</c> only Preferences is written.
/// No failure interrupts the program; each writes one diagnostics line (no content or path).
/// </summary>
internal static class PreferencesWriter
{
    /// <summary>Message-only window class Chromium on Windows uses to tell whether a browser is already running for the same user data folder.</summary>
    private const string MessageWindowClass = "Chrome_MessageWindow";

    /// <summary>File name of the preferences backup.</summary>
    public const string BackupFileName = "Preferences.backup.json";

    /// <summary>File name of the previous preferences backup.</summary>
    public const string OldBackupFileName = "Preferences.backup.old.json";

    public static void TryEnsure(string profileDir, DiagnosticsLog diagnostics)
    {
        PreferencesWriteReport report;
        try
        {
            // Browser running: skip the whole flow (no read, no backup, no write).
            if (string.IsNullOrWhiteSpace(profileDir) || IsBrowserRunning(profileDir))
            {
                return;
            }

            string wallDir = ConfigLoader.ConfigDirectory(new RealConfigEnvironment());
            report = PreferencesWrite.Run(new Files(profileDir, wallDir));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException or System.Security.SecurityException)
        {
            // Unexpected I/O error: logged as a read failure (the fallback of prepending https:// to recorded URLs still applies).
            report = new PreferencesWriteReport(PreferencesWriteResult.ReadFailed, false);
        }

        if (PreferencesWrite.IsFailure(report.Result))
        {
            diagnostics.Write(DiagnosticsFormat.PreferencesFailure(DateTime.UtcNow, report.Result));
        }

        // A failed temp file delete after writing also gets a line.
        if (report.TempDeleteFailed)
        {
            diagnostics.Write(DiagnosticsFormat.PreferencesTempDeleteFailed(DateTime.UtcNow));
        }
    }

    /// <summary>The actual file actions; each catches its own I/O errors and reports success through its return value.</summary>
    private sealed class Files : IPreferencesFiles
    {
        private readonly string _defaultDir;
        private readonly string _path;
        private readonly string _wallDir;
        private readonly string _temp;
        private readonly string _backup;
        private readonly string _oldBackup;
        private readonly string _backupTemp;

        public Files(string profileDir, string wallDir)
        {
            _defaultDir = Path.Combine(profileDir, "Default");
            _path = Path.Combine(_defaultDir, "Preferences");
            _wallDir = wallDir;
            _temp = Path.Combine(wallDir, "Preferences.tmp");
            _backup = Path.Combine(wallDir, BackupFileName);
            _oldBackup = Path.Combine(wallDir, OldBackupFileName);
            _backupTemp = Path.Combine(wallDir, "Preferences.backup.tmp");
        }

        public bool TryReadOriginal(out byte[]? original)
        {
            byte[]? read = null;
            bool ok = Try(() =>
            {
                if (File.Exists(_path))
                {
                    read = File.ReadAllBytes(_path);
                }
            });
            original = read;
            return ok;
        }

        /// <summary>Renames the existing backup to the previous one (overwriting the older one); nothing happens when there is no backup.</summary>
        public bool MoveBackupToOld() => Try(() =>
        {
            if (File.Exists(_backup))
            {
                File.Move(_backup, _oldBackup, overwrite: true);
            }
        });

        /// <summary>Writes a complete temp backup first and then renames it: a failure halfway never leaves half a backup.</summary>
        public bool WriteBackup(byte[] original) => Try(() =>
        {
            Directory.CreateDirectory(_wallDir);
            File.WriteAllBytes(_backupTemp, original);
            File.Move(_backupTemp, _backup, overwrite: true);
        });

        public bool WriteTemp(byte[] content) => Try(() =>
        {
            Directory.CreateDirectory(_wallDir);
            File.WriteAllBytes(_temp, content);
        });

        /// <summary>Same disk → replace by rename (original untouched on failure); different disk → copy over (original may be half written on failure).</summary>
        public ReplaceOutcome ReplaceWithTemp()
        {
            bool sameVolume;
            try
            {
                Directory.CreateDirectory(_defaultDir);
                sameVolume = string.Equals(
                    Path.GetPathRoot(Path.GetFullPath(_temp)),
                    Path.GetPathRoot(Path.GetFullPath(_path)),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (IsIo(ex))
            {
                return ReplaceOutcome.FailedOriginalUntouched;
            }

            if (sameVolume)
            {
                return Try(() => File.Move(_temp, _path, overwrite: true))
                    ? ReplaceOutcome.Replaced
                    : ReplaceOutcome.FailedOriginalUntouched;
            }

            return Try(() => File.Copy(_temp, _path, overwrite: true))
                ? ReplaceOutcome.Replaced
                : ReplaceOutcome.FailedOriginalMayBeDamaged;
        }

        /// <summary>Writes back the original content (only Preferences itself); when it did not exist, deletes the half-written file.</summary>
        public bool RestoreOriginal(byte[]? original) => Try(() =>
        {
            if (original is null)
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }
            }
            else
            {
                File.WriteAllBytes(_path, original);
            }
        });

        /// <summary>A temp file that cannot be deleted stays in %LOCALAPPDATA%\BrowserWall\ and is overwritten next time; returns false on failure (the caller logs it).</summary>
        public bool DeleteTemp() => Try(() =>
        {
            if (File.Exists(_temp))
            {
                File.Delete(_temp);
            }
        });

        private static bool Try(Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex) when (IsIo(ex))
            {
                return false;
            }
        }

        private static bool IsIo(Exception ex) =>
            ex is IOException or UnauthorizedAccessException or ArgumentException
                or NotSupportedException or System.Security.SecurityException;
    }

    /// <summary>
    /// Whether a browser is running for that folder: (a) a message-only window of class Chrome_MessageWindow whose name equals
    /// the folder path exists; or (b) <c>lockfile</c> exists and cannot be opened exclusively. Either one (or uncertainty) counts as running.
    /// </summary>
    private static bool IsBrowserRunning(string profileDir)
    {
        string full;
        try
        {
            full = Path.GetFullPath(profileDir);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException
                                       or System.Security.SecurityException)
        {
            return true;
        }

        foreach (string name in new[] { full, full.TrimEnd('\\'), profileDir }.Distinct(StringComparer.Ordinal))
        {
            if (NativeMethods.FindWindowEx(NativeMethods.HWND_MESSAGE, IntPtr.Zero, MessageWindowClass, name) != IntPtr.Zero)
            {
                return true;
            }
        }

        string lockFile = Path.Combine(full, "lockfile");
        if (!File.Exists(lockFile))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(lockFile, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
