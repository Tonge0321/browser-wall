using System.Text;
using System.Text.Json.Nodes;
using BrowserWall.Core.Launch;

namespace BrowserWall.Tests;

/// <summary>
/// Write only when a change is really needed; before writing, rotate the backups (old backup renamed to the previous one,
/// original saved as the new backup); no write when the backup fails; temp file, replace, original kept or written back on
/// failure; a failed temp file delete is reported.
/// </summary>
public class PreferencesWriteTests
{
    private static readonly byte[] Unset =
        Encoding.UTF8.GetBytes("{\"profile\":{\"name\":\"A\"},\"omnibox\":{\"other\":1}}");

    private static readonly byte[] AlreadySet =
        Encoding.UTF8.GetBytes("{\"omnibox\":{\"prevent_url_elisions\":true},\"x\":2}");

    private static readonly string[] WriteCalls =
        { "Read", "MoveBackupToOld", "WriteBackup", "WriteTemp", "Replace", "DeleteTemp" };

    [Fact(DisplayName = "Prefs_RotatesBackupBeforeWrite")]
    public void RotatesBackupBeforeWrite()
    {
        var files = new FakeFiles(Unset);

        PreferencesWriteReport report = PreferencesWrite.Run(files);
        Assert.Equal(PreferencesWriteResult.Written, report.Result);
        Assert.False(report.TempDeleteFailed);

        // The old backup is renamed to the previous one first, then the new backup is written (same bytes as the original), both before the temp file and the replace.
        Assert.Equal(WriteCalls, files.Calls);
        Assert.Equal(Unset, files.BackupContent);

        // Written content: the setting is true, everything else kept.
        JsonNode written = JsonNode.Parse(files.TempContent!)!;
        Assert.True(written["omnibox"]!["prevent_url_elisions"]!.GetValue<bool>());
        Assert.Equal(1, written["omnibox"]!["other"]!.GetValue<int>());
        Assert.Equal("A", written["profile"]!["name"]!.GetValue<string>());

        // Renaming the old backup fails: no new backup, no temp file.
        var moveFails = new FakeFiles(Unset) { MoveOk = false };
        Assert.Equal(PreferencesWriteResult.BackupFailed, PreferencesWrite.Run(moveFails).Result);
        Assert.Equal(new[] { "Read", "MoveBackupToOld" }, moveFails.Calls);

        // Writing the new backup fails: no temp file.
        var writeFails = new FakeFiles(Unset) { BackupOk = false };
        Assert.Equal(PreferencesWriteResult.BackupFailed, PreferencesWrite.Run(writeFails).Result);
        Assert.Equal(new[] { "Read", "MoveBackupToOld", "WriteBackup" }, writeFails.Calls);

        // Deleting the temp file fails: same result, reported separately.
        var deleteFails = new FakeFiles(Unset) { DeleteOk = false };
        PreferencesWriteReport deleteReport = PreferencesWrite.Run(deleteFails);
        Assert.Equal(PreferencesWriteResult.Written, deleteReport.Result);
        Assert.True(deleteReport.TempDeleteFailed);
    }

    [Fact(DisplayName = "Prefs_NoWriteWhenBackupFails")]
    public void NoWriteWhenBackupFails()
    {
        var files = new FakeFiles(Unset) { BackupOk = false };

        Assert.Equal(PreferencesWriteResult.BackupFailed, PreferencesWrite.Run(files).Result);
        Assert.Equal(new[] { "Read", "MoveBackupToOld", "WriteBackup" }, files.Calls);
        Assert.DoesNotContain("WriteTemp", files.Calls);
        Assert.DoesNotContain("Replace", files.Calls);
        Assert.DoesNotContain("Restore", files.Calls);
        Assert.True(PreferencesWrite.IsFailure(PreferencesWriteResult.BackupFailed));
    }

    [Fact(DisplayName = "Prefs_MissingOriginal_NoBackupWritesViaTemp")]
    public void MissingOriginal()
    {
        var files = new FakeFiles(null);

        Assert.Equal(PreferencesWriteResult.Written, PreferencesWrite.Run(files).Result);
        Assert.Equal(new[] { "Read", "WriteTemp", "Replace", "DeleteTemp" }, files.Calls);
        Assert.True(JsonNode.Parse(files.TempContent!)!["omnibox"]!["prevent_url_elisions"]!.GetValue<bool>());
    }

    [Fact(DisplayName = "Prefs_InvalidJson_NoBackupNoWrite")]
    public void InvalidJson()
    {
        var files = new FakeFiles(Encoding.UTF8.GetBytes("{not json"));

        Assert.Equal(PreferencesWriteResult.NotNeeded, PreferencesWrite.Run(files).Result);
        Assert.Equal(new[] { "Read" }, files.Calls);
        Assert.False(PreferencesWrite.IsFailure(PreferencesWriteResult.NotNeeded));
    }

    [Fact(DisplayName = "Prefs_SkipWhenAlreadySet")]
    public void SkipWhenAlreadySet()
    {
        var files = new FakeFiles(AlreadySet);

        PreferencesWriteReport report = PreferencesWrite.Run(files);
        Assert.Equal(PreferencesWriteResult.NotNeeded, report.Result);
        Assert.False(report.TempDeleteFailed);

        // Only the original is read: no rename, no backup, no temp file, no replace (backups untouched when the setting is already true).
        Assert.Equal(new[] { "Read" }, files.Calls);
        Assert.Null(files.BackupContent);

        // Invalid JSON, top level not an object: backups untouched as well.
        foreach (string text in new[] { "{not json", "[1,2]", "{\"omnibox\":3}" })
        {
            var other = new FakeFiles(Encoding.UTF8.GetBytes(text));
            Assert.Equal(PreferencesWriteResult.NotNeeded, PreferencesWrite.Run(other).Result);
            Assert.Equal(new[] { "Read" }, other.Calls);
        }
    }

    [Fact(DisplayName = "Prefs_ValidJsonButNotObject_NoBackupNoWrite")]
    public void ValidJsonNotObject()
    {
        byte[] array = Encoding.UTF8.GetBytes("[1,2]");
        var files = new FakeFiles(array);

        Assert.Equal(PreferencesWriteResult.NotNeeded, PreferencesWrite.Run(files).Result);
        Assert.Equal(new[] { "Read" }, files.Calls);
        Assert.True(PreferencesPatch.IsValidJson(array));
        Assert.True(PreferencesPatch.IsValidJson(Encoding.UTF8.GetPreamble().Concat(array).ToArray()));
        Assert.False(PreferencesPatch.IsValidJson(Encoding.UTF8.GetBytes("{")));
    }

    [Fact(DisplayName = "Prefs_TempFails_NoReplace")]
    public void TempFails()
    {
        var files = new FakeFiles(Unset) { TempOk = false };

        Assert.Equal(PreferencesWriteResult.TempFailed, PreferencesWrite.Run(files).Result);
        Assert.Equal(new[] { "Read", "MoveBackupToOld", "WriteBackup", "WriteTemp", "DeleteTemp" }, files.Calls);
    }

    [Fact(DisplayName = "Prefs_ReplaceFailsUntouched_NoRestore")]
    public void ReplaceFailsUntouched()
    {
        var files = new FakeFiles(Unset) { Replace = ReplaceOutcome.FailedOriginalUntouched };

        Assert.Equal(PreferencesWriteResult.ReplaceFailedOriginalKept, PreferencesWrite.Run(files).Result);
        Assert.Equal(WriteCalls, files.Calls);
    }

    [Fact(DisplayName = "Prefs_ReplaceMayDamage_RestoresOriginal")]
    public void ReplaceMayDamage()
    {
        var files = new FakeFiles(Unset) { Replace = ReplaceOutcome.FailedOriginalMayBeDamaged };

        Assert.Equal(PreferencesWriteResult.ReplaceFailedOriginalKept, PreferencesWrite.Run(files).Result);
        Assert.Equal(
            new[] { "Read", "MoveBackupToOld", "WriteBackup", "WriteTemp", "Replace", "Restore", "DeleteTemp" },
            files.Calls);
        Assert.Equal(Unset, files.RestoredContent);

        var missing = new FakeFiles(null) { Replace = ReplaceOutcome.FailedOriginalMayBeDamaged };
        Assert.Equal(PreferencesWriteResult.ReplaceFailedOriginalKept, PreferencesWrite.Run(missing).Result);
        Assert.Contains("Restore", missing.Calls);
        Assert.Null(missing.RestoredContent);
    }

    [Fact(DisplayName = "Prefs_RestoreAlsoFails")]
    public void RestoreFails()
    {
        var files = new FakeFiles(Unset) { Replace = ReplaceOutcome.FailedOriginalMayBeDamaged, RestoreOk = false };

        Assert.Equal(PreferencesWriteResult.ReplaceFailedRestoreFailed, PreferencesWrite.Run(files).Result);
        Assert.True(PreferencesWrite.IsFailure(PreferencesWriteResult.ReplaceFailedRestoreFailed));
    }

    [Fact(DisplayName = "Prefs_ReadFails_NothingElse")]
    public void ReadFails()
    {
        var files = new FakeFiles(Unset) { ReadOk = false };

        Assert.Equal(PreferencesWriteResult.ReadFailed, PreferencesWrite.Run(files).Result);
        Assert.Equal(new[] { "Read" }, files.Calls);
    }

    private sealed class FakeFiles : IPreferencesFiles
    {
        private readonly byte[]? _original;

        public FakeFiles(byte[]? original) => _original = original;

        public List<string> Calls { get; } = new();

        public bool ReadOk { get; init; } = true;

        public bool MoveOk { get; init; } = true;

        public bool BackupOk { get; init; } = true;

        public bool TempOk { get; init; } = true;

        public bool RestoreOk { get; init; } = true;

        public bool DeleteOk { get; init; } = true;

        public ReplaceOutcome Replace { get; init; } = ReplaceOutcome.Replaced;

        public byte[]? BackupContent { get; private set; }

        public byte[]? TempContent { get; private set; }

        public byte[]? RestoredContent { get; private set; }

        public bool TryReadOriginal(out byte[]? original)
        {
            Calls.Add("Read");
            original = ReadOk ? _original?.ToArray() : null;
            return ReadOk;
        }

        public bool MoveBackupToOld()
        {
            Calls.Add("MoveBackupToOld");
            return MoveOk;
        }

        public bool WriteBackup(byte[] original)
        {
            Calls.Add("WriteBackup");
            BackupContent = original.ToArray();
            return BackupOk;
        }

        public bool WriteTemp(byte[] content)
        {
            Calls.Add("WriteTemp");
            TempContent = content.ToArray();
            return TempOk;
        }

        public ReplaceOutcome ReplaceWithTemp()
        {
            Calls.Add("Replace");
            return Replace;
        }

        public bool RestoreOriginal(byte[]? original)
        {
            Calls.Add("Restore");
            RestoredContent = original?.ToArray();
            return RestoreOk;
        }

        public bool DeleteTemp()
        {
            Calls.Add("DeleteTemp");
            return DeleteOk;
        }
    }
}
