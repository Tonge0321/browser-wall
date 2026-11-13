using System.IO;
using System.Text;
using BrowserWall.Core.Health;

namespace BrowserWall.App;

/// <summary>
/// The diagnostics log file: <c>%LOCALAPPDATA%\BrowserWall\diagnostics.log</c>, UTF-8 without BOM, one entry per line.
/// Over 1 MB it is first renamed to <c>diagnostics.old.log</c> (overwriting the old one) and started again. Write failures are
/// skipped without interrupting the program. Only these two files are written.
/// Content comes only from <see cref="DiagnosticsFormat"/> (no titles or URLs).
/// <see cref="Write"/> only puts a line into a bounded queue and returns; the file is written on a separate background thread,
/// so neither the UI thread nor other work waits for file I/O (a slow disk cannot block the wall). When the queue is full, new lines are dropped.
/// </summary>
internal sealed class DiagnosticsLog
{
    /// <summary>Maximum number of lines not yet written.</summary>
    private const int MaxPending = 1024;

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly object _queueLock = new();
    private readonly Queue<string> _pending = new();
    private readonly string _path;
    private readonly string _oldPath;

    /// <summary>Interval for checking whether writing is done before the wall closes.</summary>
    private static readonly TimeSpan FlushPoll = TimeSpan.FromMilliseconds(20);

    /// <summary>The background thread is writing a line (taken from the queue, not yet written); guarded by <see cref="_queueLock"/>.</summary>
    private bool _writing;

    public DiagnosticsLog(string directory)
    {
        _path = Path.Combine(directory, "diagnostics.log");
        _oldPath = Path.Combine(directory, "diagnostics.old.log");
        new Thread(WriteLoop) { IsBackground = true, Name = "DiagnosticsLog" }.Start();
    }

    public static DiagnosticsLog ForDefaultLocation() =>
        new(ConfigLoader.ConfigDirectory(new RealConfigEnvironment()));

    /// <summary>Returns once queued, without waiting for the file write.</summary>
    public void Write(string line)
    {
        lock (_queueLock)
        {
            if (_pending.Count < MaxPending)
            {
                _pending.Enqueue(line);
                Monitor.Pulse(_queueLock);
            }
        }
    }

    /// <summary>
    /// Before the wall closes: completes with true when the queue is empty and the line being written is done; with false after <paramref name="max"/>.
    /// Waits asynchronously without blocking the UI thread.
    /// </summary>
    public async Task<bool> WhenFlushed(TimeSpan max)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            lock (_queueLock)
            {
                if (_pending.Count == 0 && !_writing)
                {
                    return true;
                }
            }

            // Read the time once per round: stop when the remaining time is not above 0, so the value passed to Task.Delay is always positive.
            TimeSpan left = max - clock.Elapsed;
            if (left <= TimeSpan.Zero)
            {
                return false;
            }

            await Task.Delay(left < FlushPoll ? left : FlushPoll);
        }
    }

    private void WriteLoop()
    {
        while (true)
        {
            string line;
            lock (_queueLock)
            {
                _writing = false;
                while (_pending.Count == 0)
                {
                    Monitor.Wait(_queueLock);
                }

                line = _pending.Dequeue();
                _writing = true;
            }

            Append(line);
        }
    }

    private void Append(string line)
    {
        byte[] bytes = Utf8NoBom.GetBytes(line + Environment.NewLine);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            long size = File.Exists(_path) ? new FileInfo(_path).Length : 0;
            if (DiagnosticsFormat.ShouldRotate(size, bytes.Length))
            {
                File.Move(_path, _oldPath, overwrite: true);
            }

            using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(bytes, 0, bytes.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A failed write does not interrupt.
        }
    }
}
