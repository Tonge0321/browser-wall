namespace BrowserWall.Core.Launch;

/// <summary>Result of replacing Preferences with the temp file.</summary>
public enum ReplaceOutcome
{
    Replaced,

    /// <summary>Replace failed and the original was not touched (for example, a rename on the same disk failed).</summary>
    FailedOriginalUntouched,

    /// <summary>Replace failed and the original may be half written (for example, a cross-disk copy failed midway).</summary>
    FailedOriginalMayBeDamaged,
}

/// <summary>
/// The actual file actions for writing the browser preferences, provided by the App. Each action catches its own
/// read/write errors and reports success through its return value.
/// </summary>
public interface IPreferencesFiles
{
    /// <summary>Reads the original. false = read failed; true with <paramref name="original"/> null = file does not exist.</summary>
    bool TryReadOriginal(out byte[]? original);

    /// <summary>Renames the existing <c>Preferences.backup.json</c> to <c>Preferences.backup.old.json</c> (overwriting the older one); returns true when there is no backup.</summary>
    bool MoveBackupToOld();

    /// <summary>Saves the original as is as the new <c>Preferences.backup.json</c>.</summary>
    bool WriteBackup(byte[] original);

    /// <summary>Writes the new content to the temp file (in %LOCALAPPDATA%\BrowserWall\).</summary>
    bool WriteTemp(byte[] content);

    /// <summary>Replaces Preferences with the temp file.</summary>
    ReplaceOutcome ReplaceWithTemp();

    /// <summary>Writes back the original content; when it did not exist (null), deletes the half-written Preferences.</summary>
    bool RestoreOriginal(byte[]? original);

    /// <summary>Deletes the temp file; a missing temp file counts as success.</summary>
    bool DeleteTemp();
}

/// <summary>Result of writing the browser preferences.</summary>
public enum PreferencesWriteResult
{
    /// <summary>Already set, or the original is not valid JSON, etc.: nothing written, backups untouched (not a failure).</summary>
    NotNeeded,
    Written,
    ReadFailed,

    /// <summary>Backup (renaming the old backup or writing the new one) failed: nothing written.</summary>
    BackupFailed,
    TempFailed,

    /// <summary>Replace failed; the original is untouched or was written back.</summary>
    ReplaceFailedOriginalKept,

    /// <summary>Replace failed and writing back also failed.</summary>
    ReplaceFailedRestoreFailed,
}

/// <summary>Result of one write, plus whether deleting the temp file failed (that also gets a diagnostics line).</summary>
public readonly record struct PreferencesWriteReport(PreferencesWriteResult Result, bool TempDeleteFailed);

/// <summary>
/// Step order for writing the browser preferences: read the original → compute the new content; when no change is needed,
/// do nothing (no write, backups untouched) → when the original exists, rotate the backups (old backup renamed to the
/// previous one, then the original saved as the new backup; any failure → no write) → write the temp file → replace →
/// on failure keep or write back the original → delete the temp file. Only orders the steps and does not touch files;
/// the caller checks first whether the browser is running.
/// </summary>
public static class PreferencesWrite
{
    public static PreferencesWriteReport Run(IPreferencesFiles files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (!files.TryReadOriginal(out byte[]? original))
        {
            return new PreferencesWriteReport(PreferencesWriteResult.ReadFailed, false);
        }

        // Write only when a change is really needed (already true, not valid JSON, etc. → no write, backups untouched).
        byte[]? content = PreferencesPatch.Apply(original);
        if (content is null)
        {
            return new PreferencesWriteReport(PreferencesWriteResult.NotNeeded, false);
        }

        // (1) Change needed and the original exists (it must be valid JSON then) → rotate backups first; on failure, do not write.
        if (original is not null && (!files.MoveBackupToOld() || !files.WriteBackup(original)))
        {
            return new PreferencesWriteReport(PreferencesWriteResult.BackupFailed, false);
        }

        // (2) Write the new content to the temp file first, then replace.
        PreferencesWriteResult result;
        if (!files.WriteTemp(content))
        {
            result = PreferencesWriteResult.TempFailed;
        }
        else
        {
            switch (files.ReplaceWithTemp())
            {
                case ReplaceOutcome.Replaced:
                    result = PreferencesWriteResult.Written;
                    break;
                case ReplaceOutcome.FailedOriginalUntouched:
                    result = PreferencesWriteResult.ReplaceFailedOriginalKept;
                    break;
                default:
                    // (3) The original may be half written: write back the original content.
                    result = files.RestoreOriginal(original)
                        ? PreferencesWriteResult.ReplaceFailedOriginalKept
                        : PreferencesWriteResult.ReplaceFailedRestoreFailed;
                    break;
            }
        }

        bool deleteFailed = !files.DeleteTemp();
        return new PreferencesWriteReport(result, deleteFailed);
    }

    /// <summary>Everything other than <see cref="PreferencesWriteResult.NotNeeded"/> and <see cref="PreferencesWriteResult.Written"/> is a failure.</summary>
    public static bool IsFailure(PreferencesWriteResult result) =>
        result is not (PreferencesWriteResult.NotNeeded or PreferencesWriteResult.Written);
}
