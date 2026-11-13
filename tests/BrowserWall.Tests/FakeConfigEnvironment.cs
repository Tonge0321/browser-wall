global using Xunit;

using BrowserWall.Core.Config;

namespace BrowserWall.Tests;

/// <summary>Test environment: only registered paths "exist"; the real file system is never touched.</summary>
internal sealed class FakeConfigEnvironment : IConfigEnvironment
{
    public const string FakeLocalAppData = @"C:\Users\Tester\AppData\Local";
    public const string ChromePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

    private readonly HashSet<string> _files;

    public FakeConfigEnvironment(params string[] existingFiles)
    {
        _files = new HashSet<string>(existingFiles, StringComparer.OrdinalIgnoreCase);
    }

    public string LocalAppData => FakeLocalAppData;

    public bool FileExists(string path) => _files.Contains(path);

    /// <summary>Windows display language; defaults to Traditional Chinese so existing message assertions keep working.</summary>
    public string UiCultureName { get; init; } = "zh-TW";
}
