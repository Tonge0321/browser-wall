using System.Diagnostics;

namespace BrowserWall.Core.Infra;

/// <summary>Result of one background job.</summary>
public enum WorkOutcome
{
    /// <summary>Finished within the deadline.</summary>
    Completed,

    /// <summary>Not started or not finished by the deadline: skipped, or no longer waited for.</summary>
    TimedOut,

    /// <summary>Abandoned jobs that are still stuck have reached the cap; this job was not posted and does not run.</summary>
    Skipped,
}

/// <summary>
/// Background job queue with deadlines. Does not call Windows APIs.
/// <list type="bullet">
/// <item><see cref="Post"/> returns immediately without waiting. Each job has a single deadline = time posted + timeout, shared by queueing and running.</item>
/// <item>Start condition: running jobs are fewer than the slots (<c>concurrency</c>; abandoned ones do not count), and
/// "abandoned but still stuck + running" is below the stuck cap. So even if every running job gets stuck and expires at once, all of them can be abandoned without exceeding the cap.</item>
/// <item>A running job that reaches its deadline is always abandoned: its result is timed out, its slot frees up immediately, and a new thread takes over if needed.</item>
/// <item>When abandoned-but-stuck jobs reach the cap, <see cref="Post"/> immediately returns <see cref="WorkOutcome.Skipped"/>;
/// it resumes automatically once stuck jobs return and the count drops below the cap. Entering and leaving the cap are each notified once.</item>
/// <item>Timeout reports and cap notices are called on a separate report thread, so they do not delay other jobs' deadlines.</item>
/// <item>All threads are background threads; <see cref="Dispose"/> does not wait for any of them.</item>
/// </list>
/// </summary>
public sealed class BoundedWorker : IDisposable
{
    /// <summary>Cap on abandoned background jobs that are still stuck; defined only here in the whole solution.</summary>
    public const int MaxStuck = 4;

    /// <summary>Cap on timeout reports not yet delivered.</summary>
    public const int MaxPendingReports = 256;

    private readonly object _lock = new();
    private readonly TimeSpan _timeout;
    private readonly int _concurrency;
    private readonly int _cap;
    private readonly Action<string, object?>? _onTimeout;
    private readonly Action<bool>? _onStuckCap;
    private readonly Action<Thread>? _configure;
    private readonly string _name;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly LinkedList<Job> _queue = new();
    private readonly List<Job> _running = new();
    private readonly HashSet<Thread> _abandonedThreads = new();
    private readonly Thread _monitor;
    private readonly object _reportLock = new();
    private readonly Queue<Action> _reports = new();
    private bool _reportStop;
    private int _liveThreads;
    private int _idleThreads;
    private bool _atCap;
    private bool _disposed;

    /// <param name="timeout">Deadline of each job, counted from when it is posted.</param>
    /// <param name="concurrency">Slots: maximum jobs running at once (only running ones count, abandoned ones do not).</param>
    /// <param name="maxAbandoned">Cap on abandoned jobs that are still stuck (at least 1); also limits "abandoned + running".</param>
    /// <param name="onTimeout">Called when a job times out (the job's key and tag); called on the report thread without holding the lock.</param>
    /// <param name="configure">Setup applied to a new thread before it starts (for example STA).</param>
    /// <param name="onStuckCap">true = reached the stuck cap, false = resumed; called on the report thread without holding the lock.</param>
    public BoundedWorker(
        TimeSpan timeout,
        int concurrency = 1,
        int maxAbandoned = MaxStuck,
        Action<string, object?>? onTimeout = null,
        Action<Thread>? configure = null,
        string name = "BoundedWorker",
        Action<bool>? onStuckCap = null)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        if (concurrency < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(concurrency));
        }

        _timeout = timeout;
        _concurrency = concurrency;
        _cap = Math.Max(1, maxAbandoned);
        _onTimeout = onTimeout;
        _onStuckCap = onStuckCap;
        _configure = configure;
        _name = name;
        _monitor = new Thread(MonitorLoop) { IsBackground = true, Name = name + ".Monitor" };
        _monitor.Start();
        if (_onTimeout is not null || _onStuckCap is not null)
        {
            new Thread(ReportLoop) { IsBackground = true, Name = name + ".Report" }.Start();
        }
    }

    /// <summary>Number of abandoned threads that are still stuck.</summary>
    public int AbandonedCount
    {
        get
        {
            lock (_lock)
            {
                return _abandonedThreads.Count;
            }
        }
    }

    /// <summary>Number of running jobs (started, not expired and not yet returned); for tests.</summary>
    public int RunningCount
    {
        get
        {
            lock (_lock)
            {
                return _running.Count;
            }
        }
    }

    /// <summary>Abandoned-but-stuck jobs have reached the cap: meanwhile <see cref="Post"/> returns <see cref="WorkOutcome.Skipped"/>.</summary>
    public bool IsAtStuckCap
    {
        get
        {
            lock (_lock)
            {
                return _abandonedThreads.Count >= _cap;
            }
        }
    }

    /// <summary>
    /// Posts a job and returns immediately. The returned task completes when the job finishes or expires, whichever comes first.
    /// If a job with the same <paramref name="key"/> is already queued and not started, it is not added again; that job's result is returned.
    /// At the stuck cap the job is not queued and <see cref="WorkOutcome.Skipped"/> is returned; after disposal, timed out is returned.
    /// </summary>
    public Task<WorkOutcome> Post(string key, Action action, object? tag = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(action);
        lock (_lock)
        {
            if (_disposed)
            {
                return Task.FromResult(WorkOutcome.TimedOut);
            }

            if (_abandonedThreads.Count >= _cap)
            {
                return Task.FromResult(WorkOutcome.Skipped);
            }

            foreach (Job queued in _queue)
            {
                if (queued.Key == key)
                {
                    return queued.Completion.Task;
                }
            }

            var job = new Job(key, action, tag, _clock.Elapsed + _timeout);
            _queue.AddLast(job);
            EnsureThreadsLocked();
            Monitor.PulseAll(_lock);
            return job.Completion.Task;
        }
    }

    /// <summary>Completes with true when every posted job has a result, or with false after <paramref name="max"/>. Does not block the caller.</summary>
    public async Task<bool> WhenIdle(TimeSpan max)
    {
        Task[] pending;
        lock (_lock)
        {
            pending = _queue.Concat(_running).Where(j => !j.Finished).Select(j => (Task)j.Completion.Task).ToArray();
        }

        if (pending.Length == 0)
        {
            return true;
        }

        Task all = Task.WhenAll(pending);
        Task first = await Task.WhenAny(all, Task.Delay(max)).ConfigureAwait(false);
        return first == all;
    }

    public void Dispose()
    {
        List<Job> dropped;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            dropped = _queue.ToList();
            _queue.Clear();
            foreach (Job job in dropped)
            {
                job.Finish(WorkOutcome.TimedOut);
            }

            Monitor.PulseAll(_lock);
        }

        lock (_reportLock)
        {
            // Reports already queued are still delivered; then the report thread exits.
            _reportStop = true;
            Monitor.Pulse(_reportLock);
        }
    }

    /// <summary>Start condition (caller holds the lock): a slot is free, and abandoned + running is below the stuck cap.</summary>
    private bool CanStartLocked() =>
        _running.Count < _concurrency && _abandonedThreads.Count + _running.Count < _cap;

    /// <summary>How many more jobs can start now (caller holds the lock).</summary>
    private int StartableLocked() =>
        Math.Max(0, Math.Min(_concurrency - _running.Count, _cap - _abandonedThreads.Count - _running.Count));

    /// <summary>Starts new worker threads when needed (caller holds the lock). Idle threads never exceed the number of jobs that can start, and running + idle never exceeds the slots.</summary>
    private void EnsureThreadsLocked()
    {
        int want = Math.Min(_queue.Count, StartableLocked());
        while (!_disposed && _idleThreads < want && _liveThreads < _concurrency)
        {
            var thread = new Thread(WorkerLoop) { IsBackground = true, Name = _name + ".Worker" };
            _configure?.Invoke(thread);
            _liveThreads++;
            _idleThreads++;
            thread.Start();
        }
    }

    /// <summary>
    /// Entering or leaving the stuck cap (caller holds <c>_lock</c>): queues one notice when the state changes.
    /// The notice is queued while still holding <c>_lock</c> (lock order is always <c>_lock</c> → <c>_reportLock</c>),
    /// so notices are in the same order as the state changes and the last one is always the current state; the callback still runs on the report thread without holding a lock.
    /// </summary>
    private void UpdateCapLocked()
    {
        bool at = _abandonedThreads.Count >= _cap;
        if (at == _atCap)
        {
            return;
        }

        _atCap = at;
        if (_onStuckCap is Action<bool> notify)
        {
            lock (_reportLock)
            {
                _reports.Enqueue(() => notify(at));
                Monitor.Pulse(_reportLock);
            }
        }
    }

    private void WorkerLoop()
    {
        Thread me = Thread.CurrentThread;
        while (true)
        {
            Job job;
            var reports = new List<Job>();
            lock (_lock)
            {
                while (!_disposed && (_queue.Count == 0 || !CanStartLocked()))
                {
                    Monitor.Wait(_lock);
                }

                if (_disposed)
                {
                    _idleThreads--;
                    _liveThreads--;
                    return;
                }

                job = _queue.First!.Value;
                _queue.RemoveFirst();
                if (_clock.Elapsed >= job.Deadline)
                {
                    // Already expired when dequeued: do not run it.
                    job.Finish(WorkOutcome.TimedOut);
                    reports.Add(job);
                    job = null!;
                }
                else
                {
                    _idleThreads--;
                    job.Thread = me;
                    _running.Add(job);
                }
            }

            Report(reports);
            if (job is null)
            {
                continue;
            }

            try
            {
                job.Action();
            }
            catch (Exception)
            {
                // A job's own error does not stop the queue; it still counts as completed.
            }

            lock (_lock)
            {
                _running.Remove(job);
                job.Finish(WorkOutcome.Completed);
                bool abandoned = _abandonedThreads.Remove(me);
                if (abandoned)
                {
                    // This thread was abandoned (not counted in the slots); it exits after returning. Resumes automatically below the cap.
                    UpdateCapLocked();
                    EnsureThreadsLocked();
                }
                else
                {
                    _idleThreads++;
                }

                // Running or abandoned counts changed: threads waiting on the start condition re-check it.
                Monitor.PulseAll(_lock);
                if (abandoned)
                {
                    return;
                }
            }
        }
    }

    private void MonitorLoop()
    {
        while (true)
        {
            var reports = new List<Job>();
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                TimeSpan now = _clock.Elapsed;

                // Expired while queued: remove, do not run.
                for (LinkedListNode<Job>? node = _queue.First; node is not null;)
                {
                    LinkedListNode<Job>? next = node.Next;
                    if (now >= node.Value.Deadline)
                    {
                        _queue.Remove(node);
                        node.Value.Finish(WorkOutcome.TimedOut);
                        reports.Add(node.Value);
                    }

                    node = next;
                }

                // Expired while running: always abandon (the start condition guarantees abandoned jobs never exceed the cap); the slot frees up immediately.
                foreach (Job job in _running.ToList())
                {
                    if (!job.Finished && now >= job.Deadline)
                    {
                        _running.Remove(job);
                        job.Finish(WorkOutcome.TimedOut);
                        reports.Add(job);
                        if (job.Thread is Thread stuck && _abandonedThreads.Add(stuck))
                        {
                            _liveThreads--;
                        }
                    }
                }

                UpdateCapLocked();
                EnsureThreadsLocked();
                if (reports.Count > 0)
                {
                    Monitor.PulseAll(_lock);
                }

                if (reports.Count == 0)
                {
                    TimeSpan? next = _queue.Concat(_running).Where(j => !j.Finished)
                        .Select(j => (TimeSpan?)j.Deadline).Min();
                    if (next is TimeSpan due)
                    {
                        TimeSpan wait = due - _clock.Elapsed;
                        if (wait > TimeSpan.Zero)
                        {
                            Monitor.Wait(_lock, wait);
                        }
                    }
                    else
                    {
                        Monitor.Wait(_lock);
                    }
                }
            }

            Report(reports);
        }
    }

    /// <summary>
    /// Hands timeout reports to the separate report thread; the monitor and worker threads do not wait for it (for example writing the diagnostics file),
    /// so slow file writes never delay other jobs' deadlines. When the queue is full (<see cref="MaxPendingReports"/>), new timeout reports are dropped.
    /// Cap notices do not go through here: <see cref="UpdateCapLocked"/> queues them at the moment the state changes, always, never dropped.
    /// </summary>
    private void Report(List<Job> jobs)
    {
        if (_onTimeout is null || jobs.Count == 0)
        {
            return;
        }

        lock (_reportLock)
        {
            foreach (Job job in jobs)
            {
                if (_reports.Count < MaxPendingReports)
                {
                    string key = job.Key;
                    object? tag = job.Tag;
                    _reports.Enqueue(() => _onTimeout!(key, tag));
                }
            }

            Monitor.Pulse(_reportLock);
        }
    }

    private void ReportLoop()
    {
        while (true)
        {
            Action item;
            lock (_reportLock)
            {
                while (_reports.Count == 0 && !_reportStop)
                {
                    Monitor.Wait(_reportLock);
                }

                if (_reports.Count == 0)
                {
                    return;
                }

                item = _reports.Dequeue();
            }

            try
            {
                item();
            }
            catch (Exception)
            {
                // A failing report does not stop the loop.
            }
        }
    }

    private sealed class Job
    {
        public Job(string key, Action action, object? tag, TimeSpan deadline)
        {
            Key = key;
            Action = action;
            Tag = tag;
            Deadline = deadline;
        }

        public string Key { get; }

        public Action Action { get; }

        public object? Tag { get; }

        public TimeSpan Deadline { get; }

        public Thread? Thread { get; set; }

        public bool Finished { get; private set; }

        public TaskCompletionSource<WorkOutcome> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Finish(WorkOutcome outcome)
        {
            if (!Finished)
            {
                Finished = true;
                Completion.TrySetResult(outcome);
            }
        }
    }
}
