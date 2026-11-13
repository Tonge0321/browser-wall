using BrowserWall.Core.Health;

namespace BrowserWall.Tests;

public class FlatCheckTests
{
    private static List<Rgb> Uniform(byte r, byte g, byte b) =>
        Enumerable.Repeat(new Rgb(r, g, b), FlatCheck.GridSize * FlatCheck.GridSize).ToList();

    [Fact(DisplayName = "Flat_DetectsUniformSamples")]
    public void Flat_DetectsUniformSamples()
    {
        // The 10×10 sample points are all inside the area.
        IReadOnlyList<(int X, int Y)> points = FlatCheck.SamplePoints(1920, 1080);
        Assert.Equal(100, points.Count);
        Assert.All(points, p => Assert.True(p.X >= 0 && p.X < 1920 && p.Y >= 0 && p.Y < 1080));
        Assert.Equal(100, points.Distinct().Count());
        Assert.Empty(FlatCheck.SamplePoints(0, 100));
        Assert.Empty(FlatCheck.SamplePoints(100, -1));

        // All the same color → uniform; the color is the first sample.
        List<Rgb> gray = Uniform(200, 200, 200);
        Assert.True(FlatCheck.IsUniform(gray, out Rgb color));
        Assert.Equal(new Rgb(200, 200, 200), color);

        // One point's red differs by 8 → still uniform; by 9 → not uniform. Same for green and blue.
        List<Rgb> diff8 = Uniform(200, 200, 200);
        diff8[57] = new Rgb(208, 200, 200);
        Assert.True(FlatCheck.IsUniform(diff8, out _));
        diff8[3] = new Rgb(192, 200, 200);
        Assert.False(FlatCheck.IsUniform(diff8, out _)); // 192 to 208 differ by 16

        List<Rgb> diff9 = Uniform(200, 200, 200);
        diff9[99] = new Rgb(209, 200, 200);
        Assert.False(FlatCheck.IsUniform(diff9, out _));
        List<Rgb> green = Uniform(10, 10, 10);
        green[0] = new Rgb(10, 19, 10);
        Assert.False(FlatCheck.IsUniform(green, out _));
        List<Rgb> blue = Uniform(10, 10, 10);
        blue[50] = new Rgb(10, 10, 18);
        Assert.True(FlatCheck.IsUniform(blue, out _));

        // No samples → not uniform.
        Assert.False(FlatCheck.IsUniform(new List<Rgb>(), out _));
    }

    [Fact(DisplayName = "Nudge_TokensRejectStaleAndLate")]
    public void Nudge_TokensRejectStaleAndLate()
    {
        TimeSpan S(double s) => TimeSpan.FromSeconds(s);
        var gray = new Rgb(200, 200, 200);
        var tokens = new NudgeTokens();

        // Cancelled before sending (cannot nudge): only the same nudge is cancelled; after that it is no longer a valid nudge.
        long t1 = tokens.Issue(1, gray);
        Assert.True(tokens.IsCurrent(1, t1));
        Assert.True(tokens.Cancel(1, t1));
        Assert.False(tokens.IsCurrent(1, t1));
        Assert.False(tokens.MarkSent(1, t1));

        // An old nudge replaced by a new one: the old token is always invalid.
        long t2 = tokens.Issue(1, gray);
        long t3 = tokens.Issue(1, gray);
        Assert.False(tokens.MarkSent(1, t2));
        Assert.True(tokens.MarkSent(1, t3));
        Assert.Equal(t3, tokens.SentToken(1));
        Assert.False(tokens.Cancel(1, t3)); // Once sent, it cannot be treated as not sent

        // Recheck within the deadline: accepted once; the same one cannot be accepted again.
        Assert.True(tokens.BeginRecheck(1, t3, S(4)));
        Assert.True(tokens.TryFinishRecheck(1, t3, S(3.9)));
        Assert.False(tokens.TryFinishRecheck(1, t3, S(3.95)));
        Assert.False(tokens.TryExpireRecheck(1, t3));

        // Late recheck (the timeout report has not arrived yet): the result is rejected; the later timeout report is used once (only one "not checked" is logged).
        long t4 = tokens.Issue(2, gray);
        tokens.MarkSent(2, t4);
        tokens.BeginRecheck(2, t4, S(4));
        Assert.False(tokens.TryFinishRecheck(2, t4, S(4)));
        Assert.True(tokens.TryExpireRecheck(2, t4));
        Assert.False(tokens.TryExpireRecheck(2, t4));

        // The uniform episode ended, window removed: cancelled.
        long t5 = tokens.Issue(3, gray);
        tokens.EndEpisode(3);
        Assert.False(tokens.IsCurrent(3, t5));
        long t6 = tokens.Issue(4, gray);
        tokens.Retain(new long[] { 5 });
        Assert.False(tokens.IsCurrent(4, t6));
    }

    [Fact(DisplayName = "Nudge_GateUsesCurrentWallAndState")]
    public void Nudge_GateUsesCurrentWallAndState()
    {
        // Targets always use the current wall: a nudge and restore sent after the wall resized use the new wall, not the old size.
        var oldWall = new BrowserWall.Core.Layout.CellRect(0, 32, 1920, 1000);
        var newWall = new BrowserWall.Core.Layout.CellRect(100, 132, 1200, 700);
        Assert.Equal(new BrowserWall.Core.Layout.CellRect(100, 132, 1201, 700), NudgeGate.Target(newWall, widened: true));
        Assert.Equal(newWall, NudgeGate.Target(newWall, widened: false));
        Assert.NotEqual(NudgeGate.Target(oldWall, true), NudgeGate.Target(newWall, true));

        var state = new BrowserWall.Core.State.WallState();
        state.AddWindow(1, "https://example.com/1");
        state.AddWindow(2, "https://example.com/2");
        Assert.True(NudgeGate.CanNudge(state, 1));
        Assert.True(NudgeGate.CanRestore(state, 1));

        // Not a managed window (just popped out): no nudge, no restore.
        state.ReleaseWindow(2);
        Assert.False(NudgeGate.CanNudge(state, 2));
        Assert.False(NudgeGate.CanRestore(state, 2));

        // Title bar minimize or close button pressed, or the deferred park after the wall became active not yet run: no nudge (restore unaffected).
        state.CaptionPressStarted();
        Assert.False(NudgeGate.CanNudge(state, 1));
        Assert.True(NudgeGate.CanRestore(state, 1));
        state.CaptionPressEnded(wallStillActive: false);
        state.WallActivated();
        Assert.True(state.ActivationParkPending);
        Assert.False(NudgeGate.CanNudge(state, 1));
        state.DeferredParkTargets(state.WindowIds);

        // Wall minimized, restore pending: nothing moves.
        state.WallMinimized();
        Assert.False(NudgeGate.CanNudge(state, 1));
        Assert.False(NudgeGate.CanRestore(state, 1));
        state.WallRestored();
        Assert.True(state.RestorePending);
        Assert.False(NudgeGate.CanRestore(state, 1));
        state.SettleRestore();
        Assert.True(NudgeGate.CanNudge(state, 1));

        // Wall closing: nothing moves.
        state.BeginExit();
        Assert.False(NudgeGate.CanNudge(state, 1));
        Assert.False(NudgeGate.CanRestore(state, 1));
    }

    [Fact(DisplayName = "Nudge_SkippedSendDoesNotConsume")]
    public void Nudge_SkippedSendDoesNotConsume()
    {
        // Eligible, but the actual send was skipped (for example, the window was just minimized): recorded as not sent, the repair chance is not used up.
        var tracker = new NudgeTracker();
        var tokens = new NudgeTokens();
        var gray = new Rgb(200, 200, 200);
        Assert.Equal(NudgeDecision.None, tracker.Observe(1, true));
        Assert.Equal(NudgeDecision.Nudge, tracker.Observe(1, true));
        long token = tokens.Issue(1, gray);

        bool sentCalled = false;
        bool result = NudgeGate.Dispatch(
            allowed: true,
            trySend: () => false,
            sent: () => sentCalled = true,
            notSent: () =>
            {
                if (tokens.Cancel(1, token))
                {
                    tracker.CancelNudge(1);
                }
            });
        Assert.False(result);
        Assert.False(sentCalled);
        Assert.Null(tokens.SentToken(1));
        Assert.Equal(NudgeDecision.Nudge, tracker.Observe(1, true)); // The next uniform result still nudges

        // Not eligible: no send is attempted.
        bool tried = false;
        Assert.False(NudgeGate.Dispatch(false, () => tried = true, () => { }, () => { }));
        Assert.False(tried);

        // Really sent: only then recorded as nudged.
        long token2 = tokens.Issue(1, gray);
        Assert.True(NudgeGate.Dispatch(true, () => true, () => tokens.MarkSent(1, token2), () => { }));
        Assert.Equal(token2, tokens.SentToken(1));
    }

    [Fact(DisplayName = "Taskbar_LateOldCallIsCorrected")]
    public void Taskbar_LateOldCallIsCorrected()
    {
        var latest = new BrowserWall.Core.Infra.LatestRequest<long, bool>();

        // "Hide" started running (passed the check) and got stuck; meanwhile the user popped the window out and "show" finished first on a new thread.
        latest.Request(7, false);
        Assert.True(latest.TryBegin(7, false, out long hideVersion));
        latest.Request(7, true);
        Assert.True(latest.TryBegin(7, true, out long showVersion));
        Assert.False(latest.AfterRun(7, showVersion, true, out _)); // "Show" is the latest: nothing to replay

        // The late "hide" returns afterwards: it finds a different newer request → resend the latest state ("show").
        Assert.True(latest.AfterRun(7, hideVersion, false, out bool fix));
        Assert.True(fix);

        // An old queued request: not run when the latest state is no longer this one.
        Assert.False(latest.TryBegin(7, false, out _));

        // The newer request equals the one just finished: nothing to replay.
        latest.Request(8, false);
        Assert.True(latest.TryBegin(8, false, out long v1));
        latest.Request(8, false);
        Assert.False(latest.AfterRun(8, v1, false, out _));
    }

    [Fact(DisplayName = "Nudge_OncePerFlatEpisode")]
    public void Nudge_OncePerFlatEpisode()
    {
        var tracker = new NudgeTracker();

        // Uniform once: no nudge; twice: nudge.
        Assert.Equal(NudgeDecision.None, tracker.Observe(1, true));
        Assert.Equal(NudgeDecision.Nudge, tracker.Observe(1, true));

        // Recheck still uniform: not recovered, state unchanged; no more nudges in the same episode.
        Assert.False(tracker.Recheck(1, true));
        Assert.Equal(NudgeDecision.None, tracker.Observe(1, true));
        Assert.Equal(NudgeDecision.None, tracker.Observe(1, true));

        // A periodic check recovers once, then uniform twice again: nudge again.
        Assert.Equal(NudgeDecision.None, tracker.Observe(1, false));
        Assert.Equal(NudgeDecision.None, tracker.Observe(1, true));
        Assert.Equal(NudgeDecision.Nudge, tracker.Observe(1, true));

        // Nudge → recheck recovered → episode ends; uniform once again: no nudge, twice: nudge again.
        Assert.True(tracker.Recheck(1, false));
        Assert.Equal(NudgeDecision.None, tracker.Observe(1, true));
        Assert.Equal(NudgeDecision.Nudge, tracker.Observe(1, true));

        // Each window is counted separately.
        Assert.Equal(NudgeDecision.None, tracker.Observe(2, true));
        Assert.Equal(NudgeDecision.Nudge, tracker.Observe(2, true));

        // Should have nudged but did not send (wall just minimized, window just popped out): undo "nudged in this episode", so the next uniform result still nudges.
        Assert.Equal(NudgeDecision.None, tracker.Observe(3, true));
        Assert.Equal(NudgeDecision.Nudge, tracker.Observe(3, true));
        tracker.CancelNudge(3);
        Assert.Equal(NudgeDecision.Nudge, tracker.Observe(3, true));
        Assert.Equal(NudgeDecision.None, tracker.Observe(3, true));
        tracker.CancelNudge(99); // A window without state: no action

        // State of removed windows is cleared.
        tracker.Retain(new long[] { 2 });
        Assert.DoesNotContain(3L, tracker.TrackedIds);
        Assert.Equal(new long[] { 2 }, tracker.TrackedIds);
        Assert.Equal(NudgeDecision.None, tracker.Observe(1, true));
        Assert.Equal(NudgeDecision.Nudge, tracker.Observe(1, true));
    }
}
