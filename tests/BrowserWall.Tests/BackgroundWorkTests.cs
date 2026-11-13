using System.Collections.Concurrent;
using System.Diagnostics;
using BrowserWall.Core.Health;
using BrowserWall.Core.Infra;

namespace BrowserWall.Tests;

public class BackgroundWorkTests
{
    private static readonly TimeSpan T = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan Slack = TimeSpan.FromMilliseconds(150);

    [Fact(DisplayName = "Taskbar_TimeoutDoesNotBlockExit")]
    public async Task Taskbar_TimeoutDoesNotBlockExit()
    {
        var reports = new ConcurrentQueue<(string Key, TimeSpan At)>();
        var clock = Stopwatch.StartNew();
        using var block = new ManualResetEventSlim(false);
        using var stuckReported = new ManualResetEventSlim(false);
        using var worker = new BoundedWorker(T, concurrency: 1, maxAbandoned: 2,
            onTimeout: (key, _) =>
            {
                reports.Enqueue((key, clock.Elapsed));
                if (key == "stuck")
                {
                    stuckReported.Set();
                }
            });

        // Work that is stuck forever: Post returns at once; the timeout is reported at the deadline without waiting for it to return.
        var sw = Stopwatch.StartNew();
        TimeSpan postedAt = clock.Elapsed;
        Task<WorkOutcome> stuck = worker.Post("stuck", () => block.Wait());
        Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(50));
        Assert.Equal(WorkOutcome.TimedOut, await stuck);
        Assert.True(clock.Elapsed - postedAt < T + Slack, $"Timeout reported too late: {clock.Elapsed - postedAt}");

        // The report is sent from another thread with no ordering guarantee against the result: wait for its completion signal, then check the report time limit.
        Assert.True(stuckReported.Wait(TimeSpan.FromSeconds(2)));
        Assert.Contains(reports, r => r.Key == "stuck" && r.At - postedAt < T + Slack);
        Assert.Equal(1, worker.AbandonedCount);

        // Work posted after the stuck one is finished by a new thread.
        bool ran = false;
        Assert.Equal(WorkOutcome.Completed, await worker.Post("after", () => ran = true));
        Assert.True(ran);

        // Dequeued close to the deadline: the previous item takes 180 ms, and the second item posted at the same time has a result by its own deadline (200 ms).
        var clock2 = Stopwatch.StartNew();
        using var worker2 = new BoundedWorker(T, concurrency: 1, maxAbandoned: 2);
        Task<WorkOutcome> first = worker2.Post("first", () => Thread.Sleep(180));
        Task<WorkOutcome> second = worker2.Post("second", () => Thread.Sleep(180));
        WorkOutcome secondOutcome = await second;
        Assert.True(clock2.Elapsed < T + Slack, $"Second item got its result too late: {clock2.Elapsed}");
        Assert.Equal(WorkOutcome.TimedOut, secondOutcome);
        Assert.Equal(WorkOutcome.Completed, await first);

        // Once abandoned threads reach the cap, new work is not dispatched or run (Skipped is returned at once).
        using var block3 = new ManualResetEventSlim(false);
        using var worker3 = new BoundedWorker(T, concurrency: 1, maxAbandoned: 2);
        Assert.Equal(WorkOutcome.TimedOut, await worker3.Post("a", () => block3.Wait()));
        Assert.Equal(WorkOutcome.TimedOut, await worker3.Post("b", () => block3.Wait()));
        Assert.Equal(2, worker3.AbandonedCount);
        bool ranAfterLimit = false;
        var sw3 = Stopwatch.StartNew();
        Assert.Equal(WorkOutcome.Skipped, await worker3.Post("c", () => ranAfterLimit = true));
        Assert.True(sw3.Elapsed < T + Slack);
        Assert.False(ranAfterLimit);
        Assert.Equal(2, worker3.AbandonedCount);

        // WhenIdle: with stuck work it completes with false after about max, without waiting for the work to end (closing the wall stays within 3 seconds).
        using var block4 = new ManualResetEventSlim(false);
        using var worker4 = new BoundedWorker(TimeSpan.FromSeconds(5), concurrency: 1);
        _ = worker4.Post("long", () => block4.Wait());
        var sw4 = Stopwatch.StartNew();
        Assert.False(await worker4.WhenIdle(TimeSpan.FromMilliseconds(300)));
        Assert.True(sw4.Elapsed < TimeSpan.FromMilliseconds(300) + Slack);

        // The same key is queued only once while not yet started.
        using var gate = new ManualResetEventSlim(false);
        using var worker5 = new BoundedWorker(TimeSpan.FromSeconds(2), concurrency: 1);
        int count = 0;
        _ = worker5.Post("busy", () => gate.Wait());
        Task<WorkOutcome> d1 = worker5.Post("dup", () => Interlocked.Increment(ref count));
        Task<WorkOutcome> d2 = worker5.Post("dup", () => Interlocked.Increment(ref count));
        Assert.Same(d1, d2);
        gate.Set();
        Assert.Equal(WorkOutcome.Completed, await d1);
        Assert.Equal(1, count);

        block.Set();
        block3.Set();
        block4.Set();
    }

    [Fact(DisplayName = "Worker_AbandonCapAndSlowReport")]
    public async Task Worker_AbandonCapAndSlowReport()
    {
        // 4 items at once, all stuck: all time out at the deadline, but abandoned threads do not exceed the cap of 2.
        using var block = new ManualResetEventSlim(false);
        int reported = 0;
        var clock = Stopwatch.StartNew();

        // A very slow report (simulating a 500 ms file write stall) must not delay other work's deadlines.
        using var worker = new BoundedWorker(T, concurrency: 4, maxAbandoned: 2,
            onTimeout: (_, _) =>
            {
                Thread.Sleep(500);
                Interlocked.Increment(ref reported);
            });
        Task<WorkOutcome>[] stuck = Enumerable.Range(0, 4).Select(i => worker.Post($"s{i}", () => block.Wait())).ToArray();
        WorkOutcome[] outcomes = await Task.WhenAll(stuck);
        Assert.All(outcomes, o => Assert.Equal(WorkOutcome.TimedOut, o));
        Assert.True(clock.Elapsed < T + Slack, $"Slow report delayed the deadline: {clock.Elapsed}");
        Assert.True(worker.AbandonedCount <= 2);

        // After the stuck thread returns, new work runs as usual (new work is Skipped while at the cap, so first wait for the stuck one to return and recover).
        block.Set();
        Assert.True(await WaitUntil(() => !worker.IsAtStuckCap, TimeSpan.FromSeconds(2)));
        Assert.Equal(WorkOutcome.Completed, await worker.Post("after", () => { }));

        // The report is still sent in the end.
        var wait = Stopwatch.StartNew();
        while (Volatile.Read(ref reported) < 4 && wait.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(20);
        }

        Assert.Equal(4, Volatile.Read(ref reported));
    }

    [Fact(DisplayName = "Worker_RunsInOrderAndIdle")]
    public async Task Worker_RunsInOrderAndIdle()
    {
        using var worker = new BoundedWorker(TimeSpan.FromSeconds(2));
        var order = new ConcurrentQueue<int>();
        Task<WorkOutcome>[] tasks = Enumerable.Range(1, 5).Select(i => worker.Post($"k{i}", () => order.Enqueue(i))).ToArray();
        Assert.True(await worker.WhenIdle(TimeSpan.FromSeconds(2)));
        Assert.All(await Task.WhenAll(tasks), o => Assert.Equal(WorkOutcome.Completed, o));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, order.ToArray());
        Assert.True(await worker.WhenIdle(TimeSpan.FromMilliseconds(10)));
    }

    [Fact(DisplayName = "Nudge_ScheduleEachWindowOwnTimes")]
    public void Nudge_ScheduleEachWindowOwnTimes()
    {
        TimeSpan S(double s) => TimeSpan.FromSeconds(s);
        var schedule = new NudgeSchedule();
        schedule.Add(1, S(0));
        schedule.Add(2, S(0.3));
        Assert.Equal(S(0.5), schedule.NextDue);

        Assert.Empty(schedule.TakeDue(S(0.4)));
        Assert.Equal(new[] { new NudgeDue(1, NudgeStep.Restore, S(0.5)) }, schedule.TakeDue(S(0.5)));
        Assert.Equal(new[] { new NudgeDue(2, NudgeStep.Restore, S(0.8)) }, schedule.TakeDue(S(1)));
        Assert.Equal(new[] { new NudgeDue(1, NudgeStep.Recheck, S(2)) }, schedule.TakeDue(S(2)));
        Assert.Equal(new[] { new NudgeDue(2, NudgeStep.Recheck, S(2.3)) }, schedule.TakeDue(S(2.3)));
        Assert.Null(schedule.NextDue);

        // Taken a bit later: only due items are taken; the recheck is not taken early.
        schedule.Add(1, S(10));
        schedule.Add(2, S(10.3));
        Assert.Equal(
            new[] { new NudgeDue(1, NudgeStep.Restore, S(10.5)), new NudgeDue(2, NudgeStep.Restore, S(10.8)) },
            schedule.TakeDue(S(11)));

        // Gone after removal.
        schedule.Remove(1);
        Assert.Equal(new[] { new NudgeDue(2, NudgeStep.Recheck, S(12.3)) }, schedule.TakeDue(S(20)));
    }

    [Fact(DisplayName = "Nudge_RecheckDoesNotDelayOtherWindows")]
    public async Task Nudge_RecheckDoesNotDelayOtherWindows()
    {
        // Simulated work (no windows touched): A is nudged at 0 s, B at 0.3 s; A's recheck is stuck for 600 ms.
        var clock = Stopwatch.StartNew();
        var schedule = new NudgeSchedule();
        using var worker = new BoundedWorker(TimeSpan.FromSeconds(2), concurrency: 16);
        using var aBlock = new ManualResetEventSlim(false);
        var events = new ConcurrentDictionary<string, TimeSpan>();
        bool aDoneWhenBStarted = true;

        void OnRestore(long id) => events[$"restore{id}"] = clock.Elapsed;
        void OnRecheck(long id) => worker.Post($"recheck:{id}", () =>
        {
            events[$"recheck{id}"] = clock.Elapsed;
            if (id == 1)
            {
                aBlock.Wait(600);
                events["recheck1done"] = clock.Elapsed;
            }
            else
            {
                aDoneWhenBStarted = events.ContainsKey("recheck1done");
            }
        });

        schedule.Add(1, TimeSpan.Zero);
        schedule.Add(2, TimeSpan.FromMilliseconds(300));
        var maxPump = TimeSpan.Zero;
        while (clock.Elapsed < TimeSpan.FromSeconds(2.6))
        {
            var sw = Stopwatch.StartNew();
            schedule.Pump(clock.Elapsed, OnRestore, OnRecheck);
            maxPump = sw.Elapsed > maxPump ? sw.Elapsed : maxPump;
            await Task.Delay(5);
        }

        Assert.True(await worker.WhenIdle(TimeSpan.FromSeconds(2)));
        Assert.True(maxPump < TimeSpan.FromMilliseconds(50), $"Dispatch should not wait: {maxPump}");
        AssertNear(TimeSpan.FromMilliseconds(500), events["restore1"]);
        AssertNear(TimeSpan.FromMilliseconds(800), events["restore2"]);
        AssertNear(TimeSpan.FromMilliseconds(2000), events["recheck1"]);
        AssertNear(TimeSpan.FromMilliseconds(2300), events["recheck2"]);
        Assert.False(aDoneWhenBStarted);
    }

    [Fact(DisplayName = "Diagnostics_FormatHasNoTitleOrUrl")]
    public void Diagnostics_FormatHasNoTitleOrUrl()
    {
        var t = new DateTime(2026, 11, 10, 12, 0, 5, DateTimeKind.Utc);
        var gray = new Rgb(200, 200, 200);
        Assert.Equal("2026-11-10T12:00:05Z\tcell 3\tflat\t#C8C8C8", DiagnosticsFormat.Flat(t, 3, gray));
        Assert.Equal("2026-11-10T12:00:05Z\tcell 3\tnudge\t#C8C8C8", DiagnosticsFormat.Nudge(t, 3, gray));
        Assert.Equal("2026-11-10T12:00:05Z\tcell 3\tnudge result\t#C8C8C8\trecovered", DiagnosticsFormat.NudgeResult(t, 3, gray, NudgeOutcome.Recovered));
        Assert.EndsWith("\tnot recovered", DiagnosticsFormat.NudgeResult(t, 3, gray, NudgeOutcome.NotRecovered));
        Assert.EndsWith("\tnot checked", DiagnosticsFormat.NudgeResult(t, null, gray, NudgeOutcome.NotChecked));
        Assert.Equal("2026-11-10T12:00:05Z\t-\tcheck round over 1 s\t1234 ms", DiagnosticsFormat.SlowRound(t, TimeSpan.FromMilliseconds(1234.6)));
        Assert.Equal("2026-11-10T12:00:05Z\tcell 1\tcapture timed out", DiagnosticsFormat.CaptureTimeout(t, 1));
        Assert.Equal("2026-11-10T12:00:05Z\tcell 2\ttaskbar hide timed out, skipped", DiagnosticsFormat.TaskbarTimeout(t, 2, false));
        Assert.Equal("2026-11-10T12:00:05Z\t-\ttaskbar show timed out, skipped", DiagnosticsFormat.TaskbarTimeout(t, null, true));

        // Exactly 1 MB does not rotate; only exceeding it does.
        Assert.False(DiagnosticsFormat.ShouldRotate(1024 * 1024 - 10, 10));
        Assert.True(DiagnosticsFormat.ShouldRotate(1024 * 1024 - 10, 11));
    }

    /// <summary>Abandoned work that is still stuck does not take a slot; other work runs as usual.</summary>
    [Fact(DisplayName = "Worker_AbandonedDoesNotBlockOthers")]
    public async Task Worker_AbandonedDoesNotBlockOthers()
    {
        using var block = new ManualResetEventSlim(false);
        using var worker = new BoundedWorker(T, concurrency: 1);
        try
        {
            Assert.Equal(WorkOutcome.TimedOut, await worker.Post("stuck", () => block.Wait()));
            Assert.Equal(1, worker.AbandonedCount);
            Assert.Equal(0, worker.RunningCount);

            // Only 1 slot and the first item is still stuck: the second item finishes within its deadline as usual.
            bool ran = false;
            var sw = Stopwatch.StartNew();
            Assert.Equal(WorkOutcome.Completed, await worker.Post("next", () => ran = true));
            Assert.True(ran);
            Assert.True(sw.Elapsed < T, $"Second item finished too late: {sw.Elapsed}");
            Assert.Equal(1, worker.AbandonedCount);
        }
        finally
        {
            block.Set();
        }

        // After the stuck one returns, the count is back to 0.
        Assert.True(await WaitUntil(() => worker.AbandonedCount == 0, TimeSpan.FromSeconds(2)));
    }

    /// <summary>With 4 stuck, no new work is dispatched this round; it resumes automatically when the stuck ones end; entering and resuming each notify once.</summary>
    [Fact(DisplayName = "Worker_StuckCapSkipsRound")]
    public async Task Worker_StuckCapSkipsRound()
    {
        ManualResetEventSlim[] gates = Enumerable.Range(0, 4).Select(_ => new ManualResetEventSlim(false)).ToArray();
        var caps = new ConcurrentQueue<bool>();
        using var worker = new BoundedWorker(T, concurrency: 4, onStuckCap: reached => caps.Enqueue(reached));
        try
        {
            Assert.Equal(4, BoundedWorker.MaxStuck);
            Task<WorkOutcome>[] stuck = Enumerable.Range(0, 4)
                .Select(i => worker.Post($"s{i}", () => gates[i].Wait())).ToArray();
            Assert.All(await Task.WhenAll(stuck), o => Assert.Equal(WorkOutcome.TimedOut, o));
            Assert.Equal(4, worker.AbandonedCount);
            Assert.True(worker.IsAtStuckCap);
            Assert.True(await WaitUntil(() => caps.Count >= 1, TimeSpan.FromSeconds(2)));

            // At the cap: new work is not queued or run; Skipped is returned at once.
            bool ran = false;
            var sw = Stopwatch.StartNew();
            Assert.Equal(WorkOutcome.Skipped, await worker.Post("round", () => ran = true));
            Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(50), $"Skipped should return at once: {sw.Elapsed}");
            Assert.Equal(WorkOutcome.Skipped, await worker.Post("round2", () => ran = true));
            Assert.False(ran);
            await Task.Delay(100);
            Assert.Equal(new[] { true }, caps.ToArray());

            // Release 1 of them: below the cap, one resume notification, new work finishes as usual.
            gates[0].Set();
            Assert.True(await WaitUntil(() => !worker.IsAtStuckCap, TimeSpan.FromSeconds(2)));
            Assert.True(await WaitUntil(() => caps.Count >= 2, TimeSpan.FromSeconds(2)));
            Assert.Equal(WorkOutcome.Completed, await worker.Post("after", () => ran = true));
            Assert.True(ran);
            await Task.Delay(100);
            Assert.Equal(new[] { true, false }, caps.ToArray());
        }
        finally
        {
            foreach (ManualResetEventSlim gate in gates)
            {
                gate.Set();
            }
        }
    }

    /// <summary>
    /// At most 4 start at the same time, so every timed-out item is abandoned; at any moment "abandoned + running"
    /// never exceeds 4, and no timed-out work stays in a slot.
    /// </summary>
    [Fact(DisplayName = "Worker_StuckNeverExceedsCap")]
    public async Task Worker_StuckNeverExceedsCap()
    {
        ManualResetEventSlim[] gates = Enumerable.Range(0, 6).Select(_ => new ManualResetEventSlim(false)).ToArray();
        int[] started = new int[6];
        using var worker = new BoundedWorker(T, concurrency: 6);
        bool over = false;
        using var stopSampling = new CancellationTokenSource();
        Task sampler = Task.Run(async () =>
        {
            while (!stopSampling.IsCancellationRequested)
            {
                // Read abandoned first, then running: a transition (running → abandoned) can only undercount, never overcount.
                int abandoned = worker.AbandonedCount;
                int running = worker.RunningCount;
                if (abandoned + running > BoundedWorker.MaxStuck)
                {
                    Volatile.Write(ref over, true);
                }

                await Task.Delay(1);
            }
        });

        try
        {
            Task<WorkOutcome>[] tasks = Enumerable.Range(0, 6).Select(i => worker.Post($"s{i}", () =>
            {
                Interlocked.Exchange(ref started[i], 1);
                gates[i].Wait();
            })).ToArray();

            Assert.True(await WaitUntil(() => worker.RunningCount == 4, TimeSpan.FromMilliseconds(150)));
            Assert.Equal(4, worker.RunningCount);

            // The 4 started items and the 2 queued items all time out at their deadlines.
            Assert.All(await Task.WhenAll(tasks), o => Assert.Equal(WorkOutcome.TimedOut, o));
            Assert.Equal(new[] { 1, 1, 1, 1, 0, 0 }, Enumerable.Range(0, 6).Select(i => Volatile.Read(ref started[i])).ToArray());
            Assert.Equal(4, worker.AbandonedCount);
            Assert.Equal(0, worker.RunningCount);
            Assert.Equal(WorkOutcome.Skipped, await worker.Post("new", () => { }));

            // Release 1 → resume, new work finishes; the 2 items that timed out while queued never ran.
            gates[0].Set();
            Assert.True(await WaitUntil(() => !worker.IsAtStuckCap, TimeSpan.FromSeconds(2)));
            Assert.Equal(WorkOutcome.Completed, await worker.Post("after", () => { }));
            foreach (ManualResetEventSlim gate in gates)
            {
                gate.Set();
            }

            Assert.True(await WaitUntil(() => worker.AbandonedCount == 0, TimeSpan.FromSeconds(2)));
            Assert.Equal(0, started[4] + started[5]);
        }
        finally
        {
            stopSampling.Cancel();
            await sampler;
            foreach (ManualResetEventSlim gate in gates)
            {
                gate.Set();
            }
        }

        Assert.False(Volatile.Read(ref over), "Abandoned + running exceeded the cap");
    }

    /// <summary>With 3 already abandoned, only 1 more can start; when it times out it is abandoned (it does not stay in a slot).</summary>
    [Fact(DisplayName = "Worker_TimedOutAlwaysAbandoned")]
    public async Task Worker_TimedOutAlwaysAbandoned()
    {
        using var block = new ManualResetEventSlim(false);
        using var worker = new BoundedWorker(T, concurrency: 2);
        try
        {
            Task<WorkOutcome> a = worker.Post("a", () => block.Wait());
            Task<WorkOutcome> b = worker.Post("b", () => block.Wait());
            Assert.Equal(WorkOutcome.TimedOut, await a);
            Assert.Equal(WorkOutcome.TimedOut, await b);
            Assert.Equal(WorkOutcome.TimedOut, await worker.Post("c", () => block.Wait()));
            Assert.Equal(3, worker.AbandonedCount);

            bool dStarted = false;
            bool eStarted = false;
            Task<WorkOutcome> d = worker.Post("d", () =>
            {
                dStarted = true;
                block.Wait();
            });
            Task<WorkOutcome> e = worker.Post("e", () =>
            {
                eStarted = true;
                block.Wait();
            });

            Assert.True(await WaitUntil(() => worker.RunningCount == 1, TimeSpan.FromMilliseconds(150)));
            await Task.Delay(30);
            Assert.Equal(1, worker.RunningCount); // 2 slots, but abandoned + running must not exceed 4
            Assert.Equal(WorkOutcome.TimedOut, await d);
            Assert.Equal(WorkOutcome.TimedOut, await e);
            Assert.True(dStarted);
            Assert.False(eStarted);
            Assert.Equal(4, worker.AbandonedCount);
            Assert.Equal(0, worker.RunningCount);
        }
        finally
        {
            block.Set();
        }
    }

    /// <summary>Slots count only running work; abandoned work is not counted in the slots.</summary>
    [Fact(DisplayName = "Worker_AbandonedNotCountedInConcurrency")]
    public async Task Worker_AbandonedNotCountedInConcurrency()
    {
        using var block = new ManualResetEventSlim(false);
        using var worker = new BoundedWorker(T, concurrency: 1);
        try
        {
            Assert.Equal(WorkOutcome.TimedOut, await worker.Post("stuck", () => block.Wait()));
            Assert.Equal(1, worker.AbandonedCount);

            using var gate = new ManualResetEventSlim(false);
            Task<WorkOutcome> next = worker.Post("next", () => gate.Wait());
            Assert.True(await WaitUntil(() => worker.RunningCount == 1, TimeSpan.FromMilliseconds(150)));
            gate.Set();
            Assert.Equal(WorkOutcome.Completed, await next);
        }
        finally
        {
            block.Set();
        }
    }

    /// <summary>
    /// When stuck work returns soon after timing out, the order of "cap reached" and "resumed" notifications still matches
    /// the state changes: they always alternate, start with "cap reached", and the last one is the current state (resumed).
    /// </summary>
    [Fact(DisplayName = "Worker_StuckCapNoticesInOrder")]
    public async Task Worker_StuckCapNoticesInOrder()
    {
        var caps = new ConcurrentQueue<bool>();
        TimeSpan shortTimeout = TimeSpan.FromMilliseconds(20);
        using var worker = new BoundedWorker(shortTimeout, concurrency: 1, maxAbandoned: 1, onStuckCap: caps.Enqueue);
        int cycles = 0;
        var sw = Stopwatch.StartNew();
        while (cycles < 30 && sw.Elapsed < TimeSpan.FromSeconds(10))
        {
            // Each item returns slightly after its deadline: timeout → cap reached, then it returns → resumed.
            WorkOutcome outcome = await worker.Post($"c{cycles}", () => Thread.Sleep(25));
            if (outcome == WorkOutcome.TimedOut)
            {
                cycles++;
            }

            Assert.True(await WaitUntil(() => !worker.IsAtStuckCap, TimeSpan.FromSeconds(2)));
        }

        Assert.True(cycles > 0);
        Assert.True(await WaitUntil(() => caps.Count == 2 * cycles, TimeSpan.FromSeconds(2)), $"{caps.Count} notifications, expected {2 * cycles}");
        bool[] seen = caps.ToArray();
        for (int i = 0; i < seen.Length; i++)
        {
            Assert.Equal(i % 2 == 0, seen[i]);
        }

        Assert.False(seen[^1]);
    }

    [Fact(DisplayName = "Diagnostics_StuckCapAndPreferencesFormat")]
    public void Diagnostics_StuckCapAndPreferencesFormat()
    {
        var t = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal("2026-11-12T10:00:00Z\t-\tbackground address bar stuck cap reached, paused",
            DiagnosticsFormat.StuckCap(t, BackgroundWorkKind.AddressBar, true));
        Assert.Equal("2026-11-12T10:00:00Z\t-\tbackground address bar resumed",
            DiagnosticsFormat.StuckCap(t, BackgroundWorkKind.AddressBar, false));
        Assert.Equal("2026-11-12T10:00:00Z\t-\tbackground taskbar stuck cap reached, paused",
            DiagnosticsFormat.StuckCap(t, BackgroundWorkKind.Taskbar, true));
        Assert.Equal("2026-11-12T10:00:00Z\t-\tbackground flat check resumed",
            DiagnosticsFormat.StuckCap(t, BackgroundWorkKind.FlatCheck, false));

        Assert.Equal("2026-11-12T10:00:00Z\t-\tpreferences backup failed, not written",
            DiagnosticsFormat.PreferencesFailure(t, BrowserWall.Core.Launch.PreferencesWriteResult.BackupFailed));
        foreach (BrowserWall.Core.Launch.PreferencesWriteResult r in Enum.GetValues<BrowserWall.Core.Launch.PreferencesWriteResult>())
        {
            string[] fields = DiagnosticsFormat.PreferencesFailure(t, r).Split('\t');
            Assert.Equal(3, fields.Length);
            Assert.Equal("-", fields[1]);
            Assert.StartsWith("preferences ", fields[2]);
        }
    }

    private static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan max)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.Elapsed > max)
            {
                return false;
            }

            await Task.Delay(5);
        }

        return true;
    }

    private static void AssertNear(TimeSpan expected, TimeSpan actual) =>
        Assert.True(actual >= expected - TimeSpan.FromMilliseconds(5) && actual < expected + Slack,
            $"Expected about {expected.TotalMilliseconds} ms, actual {actual.TotalMilliseconds} ms");
}
