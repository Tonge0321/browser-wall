using BrowserWall.Core.Infra;

namespace BrowserWall.Tests;

/// <summary>The order of the steps when closing the wall, and the exit waits, match the version before the order list.</summary>
public class ExitSequenceTests
{
    [Fact(DisplayName = "Exit_StepsInVersion9Order")]
    public void Exit_StepsInVersion9Order()
    {
        ExitStep[] expected =
        {
            ExitStep.CaptureWallArea,
            ExitStep.StartExitClock,
            ExitStep.MarkClosing,
            ExitStep.StopCheckTimer,
            ExitStep.StopDragOutTimer,
            ExitStep.StopAddressBar,
            ExitStep.StopFlatCheck,
            ExitStep.CloseNotice,
            ExitStep.CancelDrag,
            ExitStep.CloseDragGhost,
            ExitStep.CancelDeferred,
            ExitStep.StopCaptionPoll,
            ExitStep.UnhookForeground,
            ExitStep.RemoveThumbnails,
            ExitStep.CloseShrinkButton,
            ExitStep.NotifySession,
            ExitStep.NotifyState,
            ExitStep.SendCloseRequests,
            ExitStep.HideWall,
            ExitStep.WaitForWindows,
            ExitStep.ReleaseRemaining,
            ExitStep.WaitTaskbar,
            ExitStep.StopTaskbar,
            ExitStep.WaitDiagnostics,
            ExitStep.MarkExitDone,
            ExitStep.Shutdown,
        };

        Assert.Equal(expected, ExitSequence.Steps);

        // Every step appears exactly once.
        Assert.Equal(Enum.GetValues<ExitStep>().OrderBy(s => s), ExitSequence.Steps.OrderBy(s => s));

        // Wait limits.
        Assert.Equal(TimeSpan.FromSeconds(2), ExitSequence.WindowCloseWait);
        Assert.Equal(TimeSpan.FromMilliseconds(100), ExitSequence.WindowClosePoll);
        Assert.Equal(TimeSpan.FromMilliseconds(500), ExitSequence.TaskbarWait);
        Assert.Equal(TimeSpan.FromSeconds(3), ExitBudget.Total);
        Assert.Equal(TimeSpan.FromMilliseconds(500), ExitBudget.MaxLogFlush);
    }

    [Fact(DisplayName = "Exit_StopsBeforeCloseRequests")]
    public void Exit_StopsBeforeCloseRequests()
    {
        var expectedKinds = new Dictionary<ExitStep, ExitStepKind>
        {
            [ExitStep.CaptureWallArea] = ExitStepKind.Prepare,
            [ExitStep.StartExitClock] = ExitStepKind.Prepare,
            [ExitStep.MarkClosing] = ExitStepKind.Stop,
            [ExitStep.StopCheckTimer] = ExitStepKind.Stop,
            [ExitStep.StopDragOutTimer] = ExitStepKind.Stop,
            [ExitStep.StopAddressBar] = ExitStepKind.Stop,
            [ExitStep.StopFlatCheck] = ExitStepKind.Stop,
            [ExitStep.CloseNotice] = ExitStepKind.Stop,
            [ExitStep.CancelDrag] = ExitStepKind.Stop,
            [ExitStep.CloseDragGhost] = ExitStepKind.Stop,
            [ExitStep.CancelDeferred] = ExitStepKind.Stop,
            [ExitStep.StopCaptionPoll] = ExitStepKind.Stop,
            [ExitStep.UnhookForeground] = ExitStepKind.Stop,
            [ExitStep.RemoveThumbnails] = ExitStepKind.Stop,
            [ExitStep.CloseShrinkButton] = ExitStepKind.Stop,
            [ExitStep.NotifySession] = ExitStepKind.Notify,
            [ExitStep.NotifyState] = ExitStepKind.Notify,
            [ExitStep.SendCloseRequests] = ExitStepKind.Window,
            [ExitStep.HideWall] = ExitStepKind.Window,
            [ExitStep.WaitForWindows] = ExitStepKind.Wait,
            [ExitStep.ReleaseRemaining] = ExitStepKind.Window,
            [ExitStep.WaitTaskbar] = ExitStepKind.Wait,
            [ExitStep.StopTaskbar] = ExitStepKind.Stop,
            [ExitStep.WaitDiagnostics] = ExitStepKind.Wait,
            [ExitStep.MarkExitDone] = ExitStepKind.Finish,
            [ExitStep.Shutdown] = ExitStepKind.Finish,
        };

        foreach (ExitStep step in Enum.GetValues<ExitStep>())
        {
            Assert.Equal(expectedKinds[step], ExitSequence.KindOf(step));
        }

        int At(ExitStep step) => ExitSequence.Steps.ToList().IndexOf(step);
        int send = At(ExitStep.SendCloseRequests);

        // Every stop step except the taskbar's comes before the close requests; the taskbar stops after windows still open are put back.
        foreach (ExitStep step in ExitSequence.Steps.Where(s => ExitSequence.KindOf(s) == ExitStepKind.Stop && s != ExitStep.StopTaskbar))
        {
            Assert.True(At(step) < send, $"{step} should come before the close requests");
            Assert.True(At(step) < At(ExitStep.NotifySession), $"{step} should come before notifying the last page list");
        }

        Assert.True(At(ExitStep.StopTaskbar) > At(ExitStep.ReleaseRemaining));
        Assert.True(At(ExitStep.WaitTaskbar) < At(ExitStep.StopTaskbar));

        // The last page list and the state machine are told before the close requests.
        Assert.True(At(ExitStep.NotifySession) < send);
        Assert.True(At(ExitStep.NotifyState) < send);

        // The wall is hidden after the close requests and before waiting for windows; diagnostics are waited for before shutting down.
        Assert.True(send < At(ExitStep.HideWall));
        Assert.True(At(ExitStep.HideWall) < At(ExitStep.WaitForWindows));
        Assert.True(At(ExitStep.WaitForWindows) < At(ExitStep.ReleaseRemaining));
        Assert.True(At(ExitStep.WaitDiagnostics) < At(ExitStep.Shutdown));
    }
}
