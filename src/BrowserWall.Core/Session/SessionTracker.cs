using System.Text.Json;
using BrowserWall.Core.Health;
using BrowserWall.Core.State;

namespace BrowserWall.Core.Session;

/// <summary>
/// Update rules for the last page list.
/// List = each cell's current URL in cell order (<see cref="WallSlot.Url"/>, skipping cells without a URL),
/// followed by startup URLs not opened yet (no cell yet); at most <see cref="WallLimits.MaxCells"/> entries.
/// The whole file is rewritten only when the content differs from the last successful write (or what was read at startup).
/// Does not read the clock or touch the file system; writes only go through <see cref="ISessionStore"/>.
/// A cell's URL changes at most once every 2 seconds, guaranteed by <see cref="LastSeenUrl"/> when recording.
/// </summary>
public sealed class SessionTracker
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ISessionStore _store;
    private readonly List<(int Id, string Url)> _pending = new();
    private readonly int[] _startupEntries;
    private List<string> _written;
    private List<string> _current;
    private readonly FailureEpisode _writeEpisode = new();
    private bool _exiting;

    /// <summary>
    /// <paramref name="startupUrls"/> are the URLs to open at startup (the result of <see cref="SessionLoader"/>); they become
    /// the list in order, all not opened yet. Nothing is written on construction, nor later while the content is unchanged
    /// (an invalid list file is not overwritten).
    /// </summary>
    public SessionTracker(ISessionStore store, IReadOnlyList<string> startupUrls)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(startupUrls);
        if (startupUrls.Count > WallLimits.MaxCells)
        {
            throw new ArgumentException($"At most {WallLimits.MaxCells} startup URLs.", nameof(startupUrls));
        }

        _store = store;
        _startupEntries = new int[startupUrls.Count];
        for (int i = 0; i < startupUrls.Count; i++)
        {
            ArgumentNullException.ThrowIfNull(startupUrls[i]);
            _startupEntries[i] = i;
            _pending.Add((i, startupUrls[i]));
        }

        _written = startupUrls.ToList();
        _current = _written;
    }

    /// <summary>The current list (as built by the last <see cref="Sync"/>), in cell order.</summary>
    public IReadOnlyList<string> Urls => _current.ToList();

    /// <summary>Id of startup URL number <paramref name="index"/>; the App passes it to <c>AddWindow</c> / <c>AddFailed</c> when registering a cell.</summary>
    public int StartupEntry(int index) => _startupEntries[index];

    /// <summary>
    /// Builds the list from the current cells (the App calls this after every state change and after recording a new URL):
    /// startup URLs that appear in a cell no longer count as not opened. The whole file is rewritten only when the
    /// content differs from the last successful write (cells added, removed, swapped, or a URL changed).
    /// A failed write is not recorded as written and is retried next time; no exception is thrown. Does nothing while the wall is closing.
    /// </summary>
    public void Sync(IReadOnlyList<WallSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (_exiting)
        {
            return;
        }

        var placed = new HashSet<int>(slots.Where(s => s.SessionEntry is not null).Select(s => s.SessionEntry!.Value));
        _pending.RemoveAll(p => placed.Contains(p.Id));

        _current = slots.Select(s => s.Url)
            .Where(url => url.Length > 0)
            .Concat(_pending.Select(p => p.Url))
            .Take(WallLimits.MaxCells)
            .ToList();

        if (_current.SequenceEqual(_written, StringComparer.Ordinal))
        {
            return;
        }

        bool ok = _store.TryWrite(Serialize(_current));
        if (ok)
        {
            _written = _current;
        }

        // Log write failures: during consecutive failures, notify only on the first failure and the first success after it.
        EpisodeChange change = _writeEpisode.Report(ok);
        if (change != EpisodeChange.None)
        {
            WriteStatusChanged?.Invoke(change);
        }
    }

    /// <summary>
    /// Raised on the first list write failure (<see cref="EpisodeChange.FirstFailure"/>) or the first success after consecutive
    /// failures (<see cref="EpisodeChange.Recovered"/>); the App writes one diagnostics line for it (no URLs).
    /// Not raised when the content is unchanged and nothing is written.
    /// </summary>
    public event Action<EpisodeChange>? WriteStatusChanged;

    /// <summary>The wall starts closing: from now on the list is no longer updated or written.</summary>
    public void BeginExit() => _exiting = true;

    /// <summary>File content of the whole list: UTF-8 without BOM, an indented JSON array; an empty list is <c>[]</c>.</summary>
    public static byte[] Serialize(IReadOnlyList<string> urls)
    {
        ArgumentNullException.ThrowIfNull(urls);
        return JsonSerializer.SerializeToUtf8Bytes(urls, WriteOptions);
    }
}
