using GapActivator.Application.Messaging;
using GapActivator.Application.Runs;
using GapActivator.Application.Tests.Messaging;
using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Testing;
using Microsoft.Extensions.Logging;

namespace GapActivator.Application.Tests.Runs;

public class RunSupervisorTests
{
    private const string Exe = RunTestHost.Executable;
    private static readonly TimeSpan Limit = TimeSpan.FromHours(1);
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task Start_LaunchSucceedsAndExitsZero_RunSucceededAndCounterReset()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        process.ApplyRunOutcome(RunOutcome.Failed, 5, host.Time.GetUtcNow());
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe, pid: 777);

        Run run = await host.StartRunAsync(process);
        host.Supervisor().IsTracking(process.Id).ShouldBeTrue();
        host.Supervisor().TrackedRuns.ShouldBe([run.Id]);
        child.Exit(0);
        await host.EndOf(run.Id);

        run.Outcome.ShouldBe(RunOutcome.Succeeded);
        run.Pid.ShouldBe(777);
        run.ExitCode.ShouldBe(0);
        run.Killed.ShouldBeFalse();
        process.ConsecutiveFailures.ShouldBe(0);
        process.RetryPending.ShouldBeFalse();
        host.Supervisor().IsTracking(process.Id).ShouldBeFalse();
        host.Supervisor().TrackedRuns.ShouldBeEmpty();
        child.DisposeCount.ShouldBe(1);
        host.RunCompletedEntries(run.Id).ShouldBe(1);
    }

    [Fact]
    public async Task Start_ExitNonZero_RunFailedRetryPending()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        Run run = await host.StartRunAsync(process);
        child.Exit(3);
        await host.EndOf(run.Id);

        run.Outcome.ShouldBe(RunOutcome.Failed);
        run.ExitCode.ShouldBe(3);
        process.ConsecutiveFailures.ShouldBe(1);
        process.RetryPending.ShouldBeTrue();
    }

    [Fact]
    public async Task Start_LaunchFails_FailedToStartWithError()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        host.Launcher.QueueFail(Exe, "The file was not found.");

        Run run = await host.StartRunAsync(process);
        await host.EndOf(run.Id);

        run.Outcome.ShouldBe(RunOutcome.FailedToStart);
        run.Error.ShouldBe("The file was not found.");
        process.ConsecutiveFailures.ShouldBe(1);
        process.RetryPending.ShouldBeTrue();
        host.Supervisor().IsTracking(process.Id).ShouldBeFalse();
    }

    [Fact]
    public async Task Start_LauncherThrowsUnexpectedException_FailedToStartAndLoggedAtError()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        host.Launcher.QueueThrow(Exe, new InvalidOperationException("A launcher bug."));

        Run run = await host.StartRunAsync(process);
        await host.EndOf(run.Id);

        run.Outcome.ShouldBe(RunOutcome.FailedToStart);
        run.Error.ShouldBe("A launcher bug.");
        host.LoggedAt(LogLevel.Error, nameof(RunSupervisor)).ShouldBeTrue();
        host.Supervisor().TrackedRuns.ShouldBeEmpty();
    }

    [Fact]
    public async Task Start_LaunchCancelledWithoutStop_FailedToStartAndLoggedAtError()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        host.Launcher.QueueThrow(Exe, new OperationCanceledException("Cancelled from nowhere."));

        Run run = await host.StartRunAsync(process);
        await host.EndOf(run.Id);

        run.Outcome.ShouldBe(RunOutcome.FailedToStart);
        run.Error.ShouldBe("Cancelled from nowhere.");
        host.Dispatcher().Attempts<RecordLaunchAborted>().ShouldBe(0);
        host.LoggedAt(LogLevel.Error, nameof(RunSupervisor)).ShouldBeTrue();
    }

    [Fact]
    public async Task Start_TimeLimitExpires_SignalsThenKillsAfterGrace_TimedOut()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(timeLimit: Limit, gracePeriod: Grace);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        Run run = await host.StartRunAsync(process);
        await host.AdvanceAfterTimerAsync(1, Limit);
        await host.Time.WaitForTimersAsync(2).Within();
        await host.Dispatcher().WaitForHandledAsync<StopRun>().Within();

        child.SignalCancelCount.ShouldBe(1);
        child.Killed.ShouldBeFalse();
        run.StopReason.ShouldBe(StopReason.TimeLimit);

        host.Time.Advance(Grace);
        await host.EndOf(run.Id);

        child.Killed.ShouldBeTrue();
        run.Outcome.ShouldBe(RunOutcome.TimedOut);
        run.Killed.ShouldBeTrue();
        run.ExitCode.ShouldBe(-1);
        process.ConsecutiveFailures.ShouldBe(1);
    }

    [Fact]
    public async Task Start_ChildExitsWithinGrace_NotKilled()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(timeLimit: Limit, gracePeriod: Grace);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        Run run = await host.StartRunAsync(process);
        await host.AdvanceAfterTimerAsync(1, Limit);
        await host.Time.WaitForTimersAsync(2).Within();
        child.Exit(7);
        await host.EndOf(run.Id);

        child.Killed.ShouldBeFalse();
        run.Outcome.ShouldBe(RunOutcome.TimedOut);
        run.Killed.ShouldBeFalse();
        run.ExitCode.ShouldBe(7);
    }

    // Review Focus 5
    [Fact]
    public async Task Start_TimeLimitLongerThanTimerMaximum_StillFires()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        TimeSpan sixtyDays = TimeSpan.FromDays(60);
        Process process = host.AddProcess(timeLimit: sixtyDays, gracePeriod: Grace);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        Run run = await host.StartRunAsync(process);
        await host.AdvanceAfterTimerAsync(1, sixtyDays);
        await host.Time.WaitForTimersAsync(2).Within();
        await host.Dispatcher().WaitForHandledAsync<StopRun>().Within();

        child.SignalCancelCount.ShouldBe(1);
        run.StopReason.ShouldBe(StopReason.TimeLimit);

        child.Exit(0);
        await host.EndOf(run.Id);
        run.Outcome.ShouldBe(RunOutcome.TimedOut);
    }

    // Review Focus 5
    [Fact]
    public async Task Start_TimeLimitWaitedInChunks_FiresOnlyAtTheDeadline()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(timeLimit: TimeSpan.FromDays(2.5), gracePeriod: Grace);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        Run run = await host.StartRunAsync(process);
        await host.AdvanceAfterTimerAsync(1, TimeSpan.FromDays(1));
        await host.AdvanceAfterTimerAsync(2, TimeSpan.FromDays(1));
        await host.Time.WaitForTimersAsync(3).Within();

        child.SignalCancelCount.ShouldBe(0);
        host.Dispatcher().Attempts<StopRun>().ShouldBe(0);

        host.Time.Advance(TimeSpan.FromDays(0.5));
        await host.Time.WaitForTimersAsync(4).Within();

        child.SignalCancelCount.ShouldBe(1);

        child.Exit(0);
        await host.EndOf(run.Id);
        run.Outcome.ShouldBe(RunOutcome.TimedOut);
    }

    [Fact]
    public async Task Start_ExitBeforeTimeLimit_NoStopAndClockMovesFreely()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(timeLimit: Limit, gracePeriod: Grace);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        Run run = await host.StartRunAsync(process);
        await host.Time.WaitForTimersAsync(1).Within();
        child.Exit(0);
        await host.EndOf(run.Id);
        host.Time.Advance(Limit);

        run.Outcome.ShouldBe(RunOutcome.Succeeded);
        child.SignalCancelCount.ShouldBe(0);
        host.Dispatcher().Attempts<StopRun>().ShouldBe(0);
    }

    // Review Focus 1
    [Fact]
    public async Task Start_ChildExitsWhileTimeLimitStopIsSent_EndsOnceWithFirstRecordedOutcome()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(timeLimit: Limit, gracePeriod: Grace);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        CommandHold hold = host.Dispatcher().HoldBefore<StopRun>();

        Run run = await host.StartRunAsync(process);
        await host.AdvanceAfterTimerAsync(1, Limit);
        await hold.Reached.Within();
        child.Exit(0);
        hold.Release();
        await host.EndOf(run.Id);

        run.Outcome.ShouldBe(RunOutcome.TimedOut);
        run.Killed.ShouldBeFalse();
        host.RunCompletedEntries(run.Id).ShouldBe(1);
    }

    [Fact]
    public async Task Start_TimeLimitStopFailsTwice_ChildStoppedMeanwhileAndRunTimedOut()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(timeLimit: Limit, gracePeriod: Grace);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        host.Dispatcher().FailNext<StopRun>(2);

        Run run = await host.StartRunAsync(process);
        await host.AdvanceAfterTimerAsync(1, Limit);
        await child.Signalled.Within();
        await host.PumpClockUntilAsync(host.EndOf(run.Id), TimeSpan.FromSeconds(1));

        child.SignalCancelCount.ShouldBe(1);
        child.Killed.ShouldBeTrue();
        run.Killed.ShouldBeTrue();
        run.Outcome.ShouldBe(RunOutcome.TimedOut);
        host.Dispatcher().Attempts<StopRun>().ShouldBe(3);
    }

    [Fact]
    public async Task Start_TimeLimitStopStillFailingWhenChildExits_RecordedBeforeTheExit()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(timeLimit: Limit, gracePeriod: Grace);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        host.Dispatcher().FailNext<StopRun>(1000);

        Run run = await host.StartRunAsync(process);
        await host.AdvanceAfterTimerAsync(1, Limit);
        await child.Signalled.Within();
        child.Exit(0);
        await host.PumpClockUntilAsync(host.Dispatcher().WaitForAttemptsAsync<StopRun>(3), TimeSpan.FromSeconds(1));

        host.Dispatcher().Attempts<RecordRunExited>().ShouldBe(0);
        host.Supervisor().IsTracking(process.Id).ShouldBeTrue();

        host.Dispatcher().StopFailing<StopRun>();
        await host.PumpClockUntilAsync(host.EndOf(run.Id), TimeSpan.FromSeconds(1));

        run.Outcome.ShouldBe(RunOutcome.TimedOut);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task Start_TimeLimitStopRefused_ChildStillStoppedAndOutcomeFollowsTheExit()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(timeLimit: Limit, gracePeriod: Grace);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        host.Dispatcher().FailNext<StopRun>(1, new DomainException("Simulated rule violation."));

        Run run = await host.StartRunAsync(process);
        await host.AdvanceAfterTimerAsync(1, Limit);
        await host.AdvanceAfterTimerAsync(2, Grace);
        await host.EndOf(run.Id);

        child.SignalCancelCount.ShouldBe(1);
        child.Killed.ShouldBeTrue();
        run.Killed.ShouldBeTrue();
        run.Outcome.ShouldBe(RunOutcome.Failed);
        host.Dispatcher().Attempts<StopRun>().ShouldBe(1);
        host.LoggedAt(LogLevel.Error, nameof(RunSupervisor)).ShouldBeTrue();
    }

    [Fact]
    public async Task Stop_WhileLaunching_LaunchAbortedCancelled()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        host.Launcher.QueueBlockUntilCancelled(Exe);

        Run run = await host.StartRunAsync(process);
        await host.Launcher.LaunchBlocked.Within();
        await host.Run<StopRun, Unit>(new StopRun(run.Id, StopReason.UserCancel));
        await host.EndOf(run.Id);

        run.Outcome.ShouldBe(RunOutcome.Cancelled);
        run.Pid.ShouldBeNull();
        process.ConsecutiveFailures.ShouldBe(0);
        host.Dispatcher().Attempts<RecordLaunchAborted>().ShouldBe(1);
        host.Dispatcher().Attempts<RecordRunLaunchFailed>().ShouldBe(0);
        host.Supervisor().IsTracking(process.Id).ShouldBeFalse();
    }

    [Fact]
    public async Task Stop_AfterOsProcessCreatedButBeforeRecorded_SignalsImmediately()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(gracePeriod: Grace);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        CommandHold hold = host.Dispatcher().HoldBefore<RecordRunLaunched>();

        Run run = await host.StartRunAsync(process);
        await hold.Reached.Within();
        await host.Run<StopRun, Unit>(new StopRun(run.Id, StopReason.UserCancel));
        await host.Time.WaitForTimersAsync(1).Within();

        child.SignalCancelCount.ShouldBe(1);

        hold.Release();
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();

        child.SignalCancelCount.ShouldBe(1);
        run.State.ShouldBe(RunState.Stopping);

        child.Exit(0);
        await host.EndOf(run.Id);
        run.Outcome.ShouldBe(RunOutcome.Cancelled);
        run.Pid.ShouldBe(child.Pid);
    }

    [Fact]
    public async Task Stop_RequestedBeforeTheSupervisorTrackedTheRun_ChildIsSignalled()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(gracePeriod: Grace);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        DateTimeOffset now = host.Time.GetUtcNow();
        Run run = Run.Begin(process.Id, process.Name, process.Kind, RunTrigger.Manual, null, false, now);
        run.RequestStop(StopReason.UserCancel, now);
        host.Store.Runs[run.Id] = run;

        host.Supervisor().Start(TicketFor(run.Id, process.Id));
        await host.Time.WaitForTimersAsync(1).Within();

        child.SignalCancelCount.ShouldBe(1);

        child.Exit(0);
        await host.EndOf(run.Id);
        run.Outcome.ShouldBe(RunOutcome.Cancelled);
    }

    [Fact]
    public async Task Stop_UntrackedRun_IsNoOp()
    {
        await using ApplicationTestHost host = RunTestHost.Create();

        host.Supervisor().Stop(RunId.New());

        host.Supervisor().TrackedRuns.ShouldBeEmpty();
    }

    [Fact]
    public async Task Stop_Twice_SignalsOnceAndDoesNotRestartGracePeriod()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(gracePeriod: Grace);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        Run run = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();
        await host.Run<StopRun, Unit>(new StopRun(run.Id, StopReason.UserCancel));
        await host.AdvanceAfterTimerAsync(1, TimeSpan.FromSeconds(10));
        host.Supervisor().Stop(run.Id);
        await host.Run<StopRun, Unit>(new StopRun(run.Id, StopReason.UserCancel));
        host.Time.Advance(TimeSpan.FromSeconds(20));
        await host.EndOf(run.Id);

        child.SignalCancelCount.ShouldBe(1);
        child.Killed.ShouldBeTrue();
        host.Time.TimersCreated.ShouldBe(1);
        run.Outcome.ShouldBe(RunOutcome.Cancelled);
        run.Killed.ShouldBeTrue();
    }

    [Fact]
    public async Task Stop_SignalThrows_LoggedAndChildKilledAfterGrace()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(gracePeriod: Grace);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        child.SignalFailure = new InvalidOperationException("Simulated signal failure.");

        Run run = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();
        await host.Run<StopRun, Unit>(new StopRun(run.Id, StopReason.UserCancel));
        await host.AdvanceAfterTimerAsync(1, Grace);
        await host.EndOf(run.Id);

        child.Killed.ShouldBeTrue();
        run.Outcome.ShouldBe(RunOutcome.Cancelled);
        host.LoggedAt(LogLevel.Error, nameof(RunSupervisor)).ShouldBeTrue();
    }

    [Fact]
    public async Task Stop_KillThrows_LoggedAndRunEndsWhenChildExitsNotKilled()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(gracePeriod: Grace);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        child.KillFailure = new InvalidOperationException("Simulated kill failure.");

        Run run = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();
        await host.Run<StopRun, Unit>(new StopRun(run.Id, StopReason.UserCancel));
        await host.AdvanceAfterTimerAsync(1, Grace);
        await host.Time.WaitForTimersAsync(2).Within();
        host.Supervisor().IsTracking(process.Id).ShouldBeTrue();
        child.Exit(5);
        await host.EndOf(run.Id);

        run.Killed.ShouldBeFalse();
        run.ExitCode.ShouldBe(5);
        host.ErrorsLogged(nameof(RunSupervisor)).ShouldBe(1);
    }

    [Fact]
    public async Task Stop_KillThrows_RetriedEvery30SecondsUntilItWorks()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(gracePeriod: Grace);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        child.KillFailure = new InvalidOperationException("Simulated kill failure.");

        Run run = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();
        await host.Run<StopRun, Unit>(new StopRun(run.Id, StopReason.UserCancel));
        await host.AdvanceAfterTimerAsync(1, Grace);
        await host.AdvanceAfterTimerAsync(2, TimeSpan.FromSeconds(30));
        await host.Time.WaitForTimersAsync(3).Within();

        child.KillAttempts.ShouldBe(2);
        host.Time.TimerDelays.ShouldBe([Grace, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)]);

        child.KillFailure = null;
        host.Time.Advance(TimeSpan.FromSeconds(30));
        await host.EndOf(run.Id);

        child.KillAttempts.ShouldBe(3);
        run.Killed.ShouldBeTrue();
    }

    [Fact]
    public async Task Stop_KillFailsBecauseChildJustExited_NoErrorAndNotKilled()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(gracePeriod: Grace);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        child.KillFailure = new InvalidOperationException("Simulated kill failure.");
        child.ExitCodeOnFailedKill = 9;

        Run run = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();
        await host.Run<StopRun, Unit>(new StopRun(run.Id, StopReason.UserCancel));
        await host.AdvanceAfterTimerAsync(1, Grace);
        await host.EndOf(run.Id);

        run.Killed.ShouldBeFalse();
        run.ExitCode.ShouldBe(9);
        host.ErrorsLogged(nameof(RunSupervisor)).ShouldBe(0);
    }

    // Review Focus 1
    [Fact]
    public async Task StopRun_RecordedBeforeTheExit_EndsOnceWithTheStopOutcome()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        CommandHold hold = host.Dispatcher().HoldBefore<RecordRunExited>();

        Run run = await host.StartRunAsync(process);
        child.Exit(0);
        await hold.Reached.Within();
        await host.Run<StopRun, Unit>(new StopRun(run.Id, StopReason.UserCancel));
        hold.Release();
        await host.EndOf(run.Id);

        run.Outcome.ShouldBe(RunOutcome.Cancelled);
        child.SignalCancelCount.ShouldBe(0);
        host.RunCompletedEntries(run.Id).ShouldBe(1);
        host.Supervisor().TrackedRuns.ShouldBeEmpty();
    }

    [Fact]
    public async Task RecordRunExited_CommitFailsOnce_RetriedAndRecorded()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        host.Dispatcher().FailNext<RecordRunExited>(1);

        Run run = await host.StartRunAsync(process);
        child.Exit(0);
        await host.AdvanceAfterTimerAsync(1, RetryDelay);
        await host.EndOf(run.Id);

        run.Outcome.ShouldBe(RunOutcome.Succeeded);
        host.RunCompletedEntries(run.Id).ShouldBe(1);
        host.Dispatcher().Attempts<RecordRunExited>().ShouldBe(2);
        host.Time.TimersCreated.ShouldBe(1);
    }

    // Review Focus 2
    [Fact]
    public async Task RecordRunExited_CommitKeepsFailing_LogsErrorOnceKeepsTrackingThenRecordsWhenItRecovers()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        host.Dispatcher().FailNext<RecordRunExited>(1000);
        double[] expectedSeconds = [1, 1, 1, 2, 4, 8, 16, 32, 60, 60];

        Run run = await host.StartRunAsync(process);
        Task end = host.Supervisor().WaitForEndAsync(run.Id, TestContext.Current.CancellationToken);
        child.Exit(0);
        for (int timer = 1; timer < expectedSeconds.Length; timer++)
        {
            await host.AdvanceAfterTimerAsync(timer, TimeSpan.FromSeconds(expectedSeconds[timer - 1]));
        }

        await host.Time.WaitForTimersAsync(expectedSeconds.Length).Within();

        host.Time.TimerDelays.ShouldBe([.. expectedSeconds.Select(TimeSpan.FromSeconds)]);
        host.Dispatcher().Attempts<RecordRunExited>().ShouldBe(expectedSeconds.Length);
        host.ErrorsLogged(nameof(RunSupervisor)).ShouldBe(1);
        host.Supervisor().IsTracking(process.Id).ShouldBeTrue();
        end.IsCompleted.ShouldBeFalse();
        run.State.ShouldBe(RunState.Running);

        host.Dispatcher().StopFailing<RecordRunExited>();
        host.Time.Advance(TimeSpan.FromSeconds(60));
        await end.Within();

        run.Outcome.ShouldBe(RunOutcome.Succeeded);
        host.RunCompletedEntries(run.Id).ShouldBe(1);
        host.ErrorsLogged(nameof(RunSupervisor)).ShouldBe(1);
        host.Supervisor().IsTracking(process.Id).ShouldBeFalse();
        child.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task Dispose_WhileARecordIsRetrying_EndsTheWorkerAndLeavesTheRunActive()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        host.Dispatcher().FailNext<RecordRunExited>(1000);

        Run run = await host.StartRunAsync(process);
        Task end = host.Supervisor().WaitForEndAsync(run.Id, TestContext.Current.CancellationToken);
        child.Exit(0);
        await host.AdvanceAfterTimerAsync(1, RetryDelay);
        await host.AdvanceAfterTimerAsync(2, RetryDelay);
        await host.AdvanceAfterTimerAsync(3, RetryDelay);
        await host.Time.WaitForTimersAsync(4).Within();
        int attempts = host.Dispatcher().Attempts<RecordRunExited>();

        host.DisposeSupervisor();
        await end.Within();

        run.State.ShouldBe(RunState.Running);
        host.Supervisor().IsTracking(process.Id).ShouldBeFalse();
        host.Dispatcher().Attempts<RecordRunExited>().ShouldBe(attempts);
        host.Log.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("is stopping", StringComparison.Ordinal));
        child.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task Dispose_ChildStillRunning_EndsTheWorkerWithoutTouchingTheChild()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(timeLimit: Limit, gracePeriod: Grace);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        Run run = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();
        await host.Time.WaitForTimersAsync(1).Within();

        host.DisposeSupervisor();
        await host.EndOf(run.Id);

        run.State.ShouldBe(RunState.Running);
        child.SignalCancelCount.ShouldBe(0);
        child.Killed.ShouldBeFalse();
        child.DisposeCount.ShouldBe(1);
        host.Supervisor().TrackedRuns.ShouldBeEmpty();
        host.Dispatcher().Attempts<RecordRunExited>().ShouldBe(0);
    }

    [Fact]
    public async Task Dispose_LaunchInFlight_EndsWithoutRecordingAnything()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        host.Launcher.QueueBlockUntilCancelled(Exe);

        Run run = await host.StartRunAsync(process);
        await host.Launcher.LaunchBlocked.Within();
        Task end = host.Supervisor().WaitForEndAsync(run.Id, TestContext.Current.CancellationToken);

        host.DisposeSupervisor();
        await end.Within();

        run.State.ShouldBe(RunState.Launching);
        host.Dispatcher().Attempts<RecordRunLaunchFailed>().ShouldBe(0);
        host.Dispatcher().Attempts<RecordLaunchAborted>().ShouldBe(0);
        host.ErrorsLogged(nameof(RunSupervisor)).ShouldBe(0);
    }

    [Fact]
    public async Task Dispose_WhileAKillKeepsFailing_EndsTheWorker()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(gracePeriod: Grace);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        child.KillFailure = new InvalidOperationException("Simulated kill failure.");

        Run run = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();
        await host.Run<StopRun, Unit>(new StopRun(run.Id, StopReason.UserCancel));
        await host.AdvanceAfterTimerAsync(1, Grace);
        await host.Time.WaitForTimersAsync(2).Within();

        host.DisposeSupervisor();
        await host.EndOf(run.Id);

        run.State.ShouldBe(RunState.Stopping);
        host.Supervisor().TrackedRuns.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dispose_Twice_DoesNotThrow()
    {
        await using ApplicationTestHost host = RunTestHost.Create();

        host.DisposeSupervisor();
        host.DisposeSupervisor();

        host.Supervisor().TrackedRuns.ShouldBeEmpty();
    }

    public static TheoryData<Exception> RuleViolations() =>
        [new DomainException("A rule."), new CommandRejectedException(RejectionCodes.NotFound, "A rejection.")];

    [Theory]
    [MemberData(nameof(RuleViolations))]
    public async Task RecordRunExited_RuleViolation_NotRetriedAndLogged(Exception violation)
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        host.Dispatcher().FailNext<RecordRunExited>(1, violation);

        Run run = await host.StartRunAsync(process);
        child.Exit(0);
        await host.EndOf(run.Id);

        host.Dispatcher().Attempts<RecordRunExited>().ShouldBe(1);
        host.Time.TimersCreated.ShouldBe(0);
        host.LoggedAt(LogLevel.Error, nameof(RunSupervisor)).ShouldBeTrue();
        host.Supervisor().IsTracking(process.Id).ShouldBeFalse();
    }

    [Fact]
    public async Task RecordRunLaunched_CommitFailsOnce_RetriedAndRecorded()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        host.Dispatcher().FailNext<RecordRunLaunched>(1);

        Run run = await host.StartRunAsync(process);
        await host.AdvanceAfterTimerAsync(1, RetryDelay);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();

        run.State.ShouldBe(RunState.Running);

        child.Exit(0);
        await host.EndOf(run.Id);
        run.Outcome.ShouldBe(RunOutcome.Succeeded);
    }

    [Fact]
    public async Task RecordRunLaunched_FailsFourTimesWhileChildRuns_PidRecordedOnceTheFailuresStop()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        host.Dispatcher().FailNext<RecordRunLaunched>(4);

        Run run = await host.StartRunAsync(process);
        await host.AdvanceAfterTimerAsync(1, RetryDelay);
        await host.AdvanceAfterTimerAsync(2, RetryDelay);
        await host.AdvanceAfterTimerAsync(3, RetryDelay);
        await host.AdvanceAfterTimerAsync(4, TimeSpan.FromSeconds(2));
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();

        run.Pid.ShouldBe(child.Pid);
        run.State.ShouldBe(RunState.Running);
        child.Exited.IsCompleted.ShouldBeFalse();

        child.Exit(0);
        await host.EndOf(run.Id);
        run.Outcome.ShouldBe(RunOutcome.Succeeded);
    }

    [Fact]
    public async Task RecordRunLaunched_KeepsFailing_TimeLimitStillFiresAndPidRecordedWhenItRecovers()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(timeLimit: Limit, gracePeriod: Grace);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        host.Dispatcher().FailNext<RecordRunLaunched>(1000);

        Run run = await host.StartRunAsync(process);
        await host.AdvanceAfterTimerAsync(2, Limit);
        await child.Signalled.Within();
        await host.Dispatcher().WaitForHandledAsync<StopRun>().Within();

        run.StopReason.ShouldBe(StopReason.TimeLimit);
        run.Pid.ShouldBeNull();
        run.State.ShouldBe(RunState.Launching);

        host.Dispatcher().StopFailing<RecordRunLaunched>();
        await host.PumpClockUntilAsync(
            host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>(), TimeSpan.FromSeconds(1));

        run.Pid.ShouldBe(child.Pid);

        await host.PumpClockUntilAsync(host.EndOf(run.Id), TimeSpan.FromSeconds(1));
        run.Outcome.ShouldBe(RunOutcome.TimedOut);
        child.SignalCancelCount.ShouldBe(1);
    }

    [Fact]
    public async Task RecordRunLaunched_KeepsFailing_ExitObservedAndLaunchRecordedBeforeTheExit()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        host.Dispatcher().FailNext<RecordRunLaunched>(1000);

        Run run = await host.StartRunAsync(process);
        child.Exit(3);
        await host.PumpClockUntilAsync(
            host.Dispatcher().WaitForAttemptsAsync<RecordRunLaunched>(6), TimeSpan.FromSeconds(60));

        host.Dispatcher().Attempts<RecordRunExited>().ShouldBe(0);
        host.Supervisor().IsTracking(process.Id).ShouldBeTrue();
        run.State.ShouldBe(RunState.Launching);

        host.Dispatcher().StopFailing<RecordRunLaunched>();
        await host.PumpClockUntilAsync(host.EndOf(run.Id), TimeSpan.FromSeconds(60));

        run.Pid.ShouldBe(child.Pid);
        run.Outcome.ShouldBe(RunOutcome.Failed);
        run.ExitCode.ShouldBe(3);
        host.Dispatcher().Attempts<RecordRunExited>().ShouldBe(1);
    }

    [Fact]
    public async Task RecordRunLaunched_WaitingForARetryWhenChildExits_RetriedAtOnceWithoutTheClock()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        host.Dispatcher().FailNext<RecordRunLaunched>(1);

        Run run = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForAttemptsAsync<RecordRunLaunched>(1).Within();
        child.Exit(3);
        await host.EndOf(run.Id);

        run.Pid.ShouldBe(child.Pid);
        run.Outcome.ShouldBe(RunOutcome.Failed);
        run.ExitCode.ShouldBe(3);
        host.Dispatcher().Attempts<RecordRunLaunched>().ShouldBe(2);
    }

    [Fact]
    public async Task RecordRunLaunched_Refused_ExitLoggedAndRunStaysActiveForReconciliation()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        host.Dispatcher().FailNext<RecordRunLaunched>(1, new DomainException("Simulated rule violation."));
        child.Exit(0);

        Run run = await host.StartRunAsync(process);
        await host.EndOf(run.Id);

        run.State.ShouldBe(RunState.Launching);
        host.Dispatcher().Attempts<RecordRunLaunched>().ShouldBe(1);
        host.Dispatcher().Attempts<RecordRunExited>().ShouldBe(1);
        host.ErrorsLogged(nameof(RunSupervisor)).ShouldBe(2);
        host.Supervisor().IsTracking(process.Id).ShouldBeFalse();
        child.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task RecordRunLaunchFailed_KeepsFailing_KeepsTrackingThenRecordsWhenItRecovers()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        host.Launcher.QueueFail(Exe);
        host.Dispatcher().FailNext<RecordRunLaunchFailed>(1000);

        Run run = await host.StartRunAsync(process);
        await host.AdvanceAfterTimerAsync(1, RetryDelay);
        await host.AdvanceAfterTimerAsync(2, RetryDelay);
        await host.AdvanceAfterTimerAsync(3, RetryDelay);
        await host.Time.WaitForTimersAsync(4).Within();

        host.Dispatcher().Attempts<RecordRunLaunchFailed>().ShouldBe(4);
        run.State.ShouldBe(RunState.Launching);
        host.ErrorsLogged(nameof(RunSupervisor)).ShouldBe(1);
        host.Supervisor().IsTracking(process.Id).ShouldBeTrue();

        host.Dispatcher().StopFailing<RecordRunLaunchFailed>();
        host.Time.Advance(TimeSpan.FromSeconds(2));
        await host.EndOf(run.Id);

        run.Outcome.ShouldBe(RunOutcome.FailedToStart);
        host.Supervisor().IsTracking(process.Id).ShouldBeFalse();
    }

    [Fact]
    public async Task Start_ExitedFaults_LoggedEntryRemovedAndRunStaysActive()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        Run run = await host.StartRunAsync(process);
        child.FailExited(new InvalidOperationException("A handle bug."));
        await host.EndOf(run.Id);

        run.State.ShouldNotBe(RunState.Completed);
        host.LoggedAt(LogLevel.Error, nameof(RunSupervisor)).ShouldBeTrue();
        host.Supervisor().IsTracking(process.Id).ShouldBeFalse();
        child.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task Start_DisposeThrows_LoggedAndEntryRemoved()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        child.DisposeFailure = new InvalidOperationException("Simulated dispose failure.");

        Run run = await host.StartRunAsync(process);
        child.Exit(0);
        await host.EndOf(run.Id);

        run.Outcome.ShouldBe(RunOutcome.Succeeded);
        host.LoggedAt(LogLevel.Error, nameof(RunSupervisor)).ShouldBeTrue();
        host.Supervisor().IsTracking(process.Id).ShouldBeFalse();
    }

    // BI-041
    [Fact]
    public async Task Start_SecondTicketSameProcess_Ignored()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        Run run = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();
        host.Supervisor().Start(TicketFor(RunId.New(), process.Id));

        host.Supervisor().TrackedRuns.ShouldBe([run.Id]);
        host.Launcher.LaunchRequests.Count.ShouldBe(1);
        host.LoggedAt(LogLevel.Warning, nameof(RunSupervisor)).ShouldBeTrue();

        child.Exit(0);
        await host.EndOf(run.Id);
        host.Launcher.LaunchRequests.Count.ShouldBe(1);
    }

    // BI-041
    [Fact]
    public async Task Start_WhilePreviousRunIsBeingRecordedAsEnded_LaunchesAfterItsEntryIsRemoved()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess first = host.Launcher.QueueSucceed(Exe);
        FakeRunningProcess second = host.Launcher.QueueSucceed(Exe);
        CommandHold hold = host.Dispatcher().HoldAfter<RecordRunExited>();

        Run firstRun = await host.StartRunAsync(process);
        first.Exit(0);
        await hold.Reached.Within();
        Run secondRun = await host.StartRunAsync(process);

        host.Launcher.LaunchRequests.Count.ShouldBe(1);

        hold.Release();
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>(2).Within();

        host.Launcher.LaunchRequests.Count.ShouldBe(2);
        host.Supervisor().TrackedRuns.ShouldBe([secondRun.Id]);
        firstRun.Outcome.ShouldBe(RunOutcome.Succeeded);

        second.Exit(0);
        await host.EndOf(secondRun.Id);
        secondRun.Outcome.ShouldBe(RunOutcome.Succeeded);
    }

    // BI-041
    [Fact]
    public async Task Start_DeferredRun_IsTrackedAndAThirdTicketIsIgnored()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess first = host.Launcher.QueueSucceed(Exe);
        FakeRunningProcess second = host.Launcher.QueueSucceed(Exe);
        CommandHold hold = host.Dispatcher().HoldAfter<RecordRunExited>();

        Run firstRun = await host.StartRunAsync(process);
        first.Exit(0);
        await hold.Reached.Within();
        Run deferred = await host.StartRunAsync(process);
        RunId third = RunId.New();
        host.Supervisor().Start(TicketFor(third, process.Id));

        host.Supervisor().TrackedRuns.OrderBy(id => id.Value).ShouldBe(new[] { firstRun.Id, deferred.Id }.OrderBy(id => id.Value));
        host.Supervisor().IsTracking(process.Id).ShouldBeTrue();
        host.Launcher.LaunchRequests.Count.ShouldBe(1);

        hold.Release();
        await host.EndOf(firstRun.Id);
        host.Supervisor().IsTracking(process.Id).ShouldBeTrue();
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>(2).Within();

        second.Exit(0);
        await host.EndOf(deferred.Id);
        host.Launcher.LaunchRequests.Select(r => r.RunId).ShouldBe([firstRun.Id, deferred.Id]);
        host.Supervisor().IsTracking(process.Id).ShouldBeFalse();
    }

    [Fact]
    public async Task Stop_DeferredRun_AbortedWithoutLaunching()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess first = host.Launcher.QueueSucceed(Exe);
        CommandHold hold = host.Dispatcher().HoldAfter<RecordRunExited>();

        Run firstRun = await host.StartRunAsync(process);
        first.Exit(0);
        await hold.Reached.Within();
        Run deferred = await host.StartRunAsync(process);

        await host.Run<StopRun, Unit>(new StopRun(deferred.Id, StopReason.UserCancel));
        await host.EndOf(deferred.Id);

        deferred.Outcome.ShouldBe(RunOutcome.Cancelled);
        host.Launcher.LaunchRequests.Count.ShouldBe(1);
        host.Dispatcher().Attempts<RecordLaunchAborted>().ShouldBe(1);
        host.Supervisor().TrackedRuns.ShouldBe([firstRun.Id]);

        hold.Release();
        await host.EndOf(firstRun.Id);
        firstRun.Outcome.ShouldBe(RunOutcome.Succeeded);
        host.Supervisor().TrackedRuns.ShouldBeEmpty();
    }

    [Fact]
    public async Task StartAfterEnd_DeferredRun_RestartsAfterItsOwnEnd()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess first = host.Launcher.QueueSucceed(Exe);
        FakeRunningProcess second = host.Launcher.QueueSucceed(Exe);
        FakeRunningProcess third = host.Launcher.QueueSucceed(Exe);
        CommandHold hold = host.Dispatcher().HoldAfter<RecordRunExited>();

        Run firstRun = await host.StartRunAsync(process);
        first.Exit(0);
        await hold.Reached.Within();
        Run deferred = await host.StartRunAsync(process);
        host.Supervisor().StartAfterEnd(deferred.Id, process.Id);

        host.Dispatcher().Attempts<StartPendingRun>().ShouldBe(2);

        hold.Release();
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>(2).Within();

        host.Dispatcher().Attempts<StartPendingRun>().ShouldBe(2);

        second.Exit(0);
        await host.EndOf(deferred.Id);
        await host.Dispatcher().WaitForHandledAsync<StartPendingRun>(3).Within();
        Run restarted = host.ActiveRun(process);

        restarted.Trigger.ShouldBe(RunTrigger.Restart);
        deferred.Outcome.ShouldBe(RunOutcome.Succeeded);

        third.Exit(0);
        await host.EndOf(restarted.Id);
        firstRun.Outcome.ShouldBe(RunOutcome.Succeeded);
    }

    [Fact]
    public async Task WaitForEnd_DeferredRun_WaitsForItsOwnEnd()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess first = host.Launcher.QueueSucceed(Exe);
        FakeRunningProcess second = host.Launcher.QueueSucceed(Exe);
        CommandHold hold = host.Dispatcher().HoldAfter<RecordRunExited>();

        Run firstRun = await host.StartRunAsync(process);
        first.Exit(0);
        await hold.Reached.Within();
        Run deferred = await host.StartRunAsync(process);

        Task end = host.Supervisor().WaitForEndAsync(deferred.Id, TestContext.Current.CancellationToken);
        end.IsCompleted.ShouldBeFalse();

        hold.Release();
        await host.EndOf(firstRun.Id);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>(2).Within();
        end.IsCompleted.ShouldBeFalse();

        second.Exit(0);
        await end.Within();
        deferred.Outcome.ShouldBe(RunOutcome.Succeeded);
    }

    [Fact]
    public async Task Dispose_DeferredRun_NeverLaunches()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess first = host.Launcher.QueueSucceed(Exe);
        CommandHold hold = host.Dispatcher().HoldAfter<RecordRunExited>();

        Run firstRun = await host.StartRunAsync(process);
        first.Exit(0);
        await hold.Reached.Within();
        Run deferred = await host.StartRunAsync(process);

        host.DisposeSupervisor();
        await host.EndOf(deferred.Id);

        deferred.State.ShouldBe(RunState.Launching);
        host.Launcher.LaunchRequests.Count.ShouldBe(1);
        firstRun.Outcome.ShouldBe(RunOutcome.Succeeded);

        hold.Release();
        await host.EndOf(firstRun.Id);
    }

    // BI-062
    [Fact]
    public async Task StartAfterEnd_Tracked_StartsPendingRunOnceAfterTheEnd()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess oldChild = host.Launcher.QueueSucceed(Exe);
        FakeRunningProcess newChild = host.Launcher.QueueSucceed(Exe);

        Run oldRun = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();
        host.Supervisor().StartAfterEnd(oldRun.Id, process.Id);
        host.Supervisor().StartAfterEnd(oldRun.Id, process.Id);

        host.Launcher.LaunchRequests.Count.ShouldBe(1);

        oldChild.Exit(0);
        await host.EndOf(oldRun.Id);
        await host.Dispatcher().WaitForHandledAsync<StartPendingRun>(2).Within();
        Run newRun = host.ActiveRun(process);

        newRun.Id.ShouldNotBe(oldRun.Id);
        newRun.Trigger.ShouldBe(RunTrigger.Restart);
        oldRun.Outcome.ShouldBe(RunOutcome.Succeeded);
        host.Dispatcher().Attempts<StartPendingRun>().ShouldBe(2);

        newChild.Exit(0);
        await host.EndOf(newRun.Id);
        host.Launcher.LaunchRequests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task StartAfterEnd_ExitWonTheRace_StartsPendingRunAtOnce()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess oldChild = host.Launcher.QueueSucceed(Exe);
        FakeRunningProcess newChild = host.Launcher.QueueSucceed(Exe);

        Run oldRun = await host.StartRunAsync(process);
        oldChild.Exit(0);
        await host.EndOf(oldRun.Id);
        host.Supervisor().StartAfterEnd(oldRun.Id, process.Id);
        await host.Dispatcher().WaitForHandledAsync<StartPendingRun>(2).Within();
        Run newRun = host.ActiveRun(process);

        newRun.Trigger.ShouldBe(RunTrigger.Restart);

        newChild.Exit(0);
        await host.EndOf(newRun.Id);
    }

    [Fact]
    public async Task StartAfterEnd_LaunchAborted_StartsPendingRunAfterTheAbortWasRecorded()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        host.Launcher.QueueBlockUntilCancelled(Exe);
        FakeRunningProcess newChild = host.Launcher.QueueSucceed(Exe);

        Run oldRun = await host.StartRunAsync(process);
        await host.Launcher.LaunchBlocked.Within();
        host.Supervisor().StartAfterEnd(oldRun.Id, process.Id);
        await host.Run<StopRun, Unit>(new StopRun(oldRun.Id, StopReason.UserCancel));
        await host.EndOf(oldRun.Id);
        await host.Dispatcher().WaitForHandledAsync<StartPendingRun>(2).Within();
        Run newRun = host.ActiveRun(process);

        oldRun.Outcome.ShouldBe(RunOutcome.Cancelled);
        newRun.Trigger.ShouldBe(RunTrigger.Restart);

        newChild.Exit(0);
        await host.EndOf(newRun.Id);
    }

    [Fact]
    public async Task WaitForEnd_NotTracked_CompletesAtOnce()
    {
        await using ApplicationTestHost host = RunTestHost.Create();

        await host.EndOf(RunId.New());
    }

    [Fact]
    public async Task WaitForEnd_TokenCancelled_ThrowsAndLeavesTheRunTracked()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        Run run = await host.StartRunAsync(process);
        using var cts = new CancellationTokenSource();

        Task wait = host.Supervisor().WaitForEndAsync(run.Id, cts.Token);
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(wait);
        host.Supervisor().IsTracking(process.Id).ShouldBeTrue();

        child.Exit(0);
        await host.EndOf(run.Id);
    }

    [Fact]
    public async Task Adopt_ChildExits_RunRecordedWithoutLaunching()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        Run run = host.AddRunningRun(process);
        var child = new FakeRunningProcess(4242, host.Time.GetUtcNow());

        host.Supervisor().Adopt(run.Id, process.Id, child, null, Grace, stopping: false);
        host.Supervisor().IsTracking(process.Id).ShouldBeTrue();
        child.Exit(0);
        await host.EndOf(run.Id);

        run.Outcome.ShouldBe(RunOutcome.Succeeded);
        host.Launcher.LaunchRequests.ShouldBeEmpty();
        host.Dispatcher().Attempts<RecordRunLaunched>().ShouldBe(0);
        child.SignalCancelCount.ShouldBe(0);
        child.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task Adopt_RemainingTimeLimit_FiresAfterTheRemainderOnly()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(timeLimit: Limit);
        Run run = host.AddRunningRun(process);
        var child = new FakeRunningProcess(4242, host.Time.GetUtcNow());

        host.Supervisor().Adopt(run.Id, process.Id, child, TimeSpan.FromMinutes(5), Grace, stopping: false);
        await host.AdvanceAfterTimerAsync(1, TimeSpan.FromMinutes(5));
        await host.Time.WaitForTimersAsync(2).Within();
        await host.Dispatcher().WaitForHandledAsync<StopRun>().Within();

        child.SignalCancelCount.ShouldBe(1);
        run.StopReason.ShouldBe(StopReason.TimeLimit);

        child.Exit(0);
        await host.EndOf(run.Id);
        run.Outcome.ShouldBe(RunOutcome.TimedOut);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public async Task Adopt_NoTimeLimitRemaining_StopsAtOnce(int remainingSeconds)
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(timeLimit: Limit);
        Run run = host.AddRunningRun(process);
        var child = new FakeRunningProcess(4242, host.Time.GetUtcNow());

        host.Supervisor().Adopt(
            run.Id, process.Id, child, TimeSpan.FromSeconds(remainingSeconds), Grace, stopping: false);
        await host.Time.WaitForTimersAsync(1).Within();
        await host.Dispatcher().WaitForHandledAsync<StopRun>().Within();

        child.SignalCancelCount.ShouldBe(1);
        run.StopReason.ShouldBe(StopReason.TimeLimit);

        child.Exit(0);
        await host.EndOf(run.Id);
        run.Outcome.ShouldBe(RunOutcome.TimedOut);
    }

    [Fact]
    public async Task Adopt_LessThanOneMillisecondRemaining_WaitsOnATimerInsteadOfSpinning()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(timeLimit: Limit);
        Run run = host.AddRunningRun(process);
        var child = new FakeRunningProcess(4242, host.Time.GetUtcNow());

        host.Supervisor().Adopt(run.Id, process.Id, child, TimeSpan.FromMicroseconds(500), Grace, stopping: false);
        await host.AdvanceAfterTimerAsync(1, TimeSpan.FromMilliseconds(1));
        await host.Time.WaitForTimersAsync(2).Within();

        child.SignalCancelCount.ShouldBe(1);

        child.Exit(0);
        await host.EndOf(run.Id);
    }

    [Fact]
    public async Task Adopt_Stopping_SignalsOnceThenKillsAfterGrace()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        Run run = host.AddRunningRun(process);
        run.RequestStop(StopReason.UserCancel, host.Time.GetUtcNow());
        run.ClearDomainEvents();
        var child = new FakeRunningProcess(4242, host.Time.GetUtcNow());

        host.Supervisor().Adopt(run.Id, process.Id, child, null, Grace, stopping: true);
        await host.Time.WaitForTimersAsync(1).Within();
        host.Supervisor().Stop(run.Id);

        child.SignalCancelCount.ShouldBe(1);

        host.Time.Advance(Grace);
        await host.EndOf(run.Id);

        child.SignalCancelCount.ShouldBe(1);
        child.Killed.ShouldBeTrue();
        run.Outcome.ShouldBe(RunOutcome.Cancelled);
        run.Killed.ShouldBeTrue();
    }

    // BI-041
    [Fact]
    public async Task Adopt_ProcessAlreadyTracked_IgnoredAndHandleDisposed()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        Run run = await host.StartRunAsync(process);
        var other = new FakeRunningProcess(4243, host.Time.GetUtcNow());
        var otherRun = RunId.New();

        host.Supervisor().Adopt(otherRun, process.Id, other, null, Grace, stopping: false);

        host.Supervisor().TrackedRuns.ShouldBe([run.Id]);
        other.DisposeCount.ShouldBe(1);

        child.Exit(0);
        await host.EndOf(run.Id);
    }

    private static RunStartTicket TicketFor(RunId runId, ProcessId processId) =>
        new(runId, processId, Exe, string.Empty, @"C:\Tools", EnvironmentVariables.Empty, null, Grace);
}
