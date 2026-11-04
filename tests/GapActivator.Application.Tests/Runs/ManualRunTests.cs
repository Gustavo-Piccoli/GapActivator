using GapActivator.Application.Messaging;
using GapActivator.Application.Runs;
using GapActivator.Application.Tests.Messaging;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Scheduling;
using GapActivator.Domain.Settings;
using GapActivator.Testing;

namespace GapActivator.Application.Tests.Runs;

public class ManualRunTests
{
    private const string Exe = RunTestHost.Executable;

    // BI-061, Q-4
    [Fact]
    public async Task RunNow_Idle_StartsManualRunAndConsumesDueOccurrences()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        DateTimeOffset now = host.Time.GetUtcNow();
        Process process = AddProcessWithDueOneTime(host, now);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        RunId runId = await host.Run<RunNow, RunId>(new RunNow(process.Id));

        Run run = host.Store.Runs[runId];
        run.Trigger.ShouldBe(RunTrigger.Manual);
        run.ProcessId.ShouldBe(process.Id);
        run.CreatedAt.ShouldBe(now);
        process.Schedules.OfType<OneTimeSchedule>().Single().FulfilledAt.ShouldBe(now);
        host.Supervisor().IsTracking(process.Id).ShouldBeTrue();

        child.Exit(1);
        await host.EndOf(runId);
    }

    // Q-1
    [Fact]
    public async Task RunNow_WhilePaused_IsRejected()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        host.Store.Settings.Pause(host.Time.GetUtcNow());

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Run<RunNow, RunId>(new RunNow(process.Id)));

        error.Code.ShouldBe(RejectionCodes.InstallationPaused);
        host.Store.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunNow_PausedAndBlocked_PausedWins()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = AddBlockedProcess(host);
        host.Store.Settings.Pause(host.Time.GetUtcNow());

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Run<RunNow, RunId>(new RunNow(process.Id)));

        error.Code.ShouldBe(RejectionCodes.InstallationPaused);
    }

    [Fact]
    public async Task RunNow_Blocked_IsRejected()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = AddBlockedProcess(host);

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Run<RunNow, RunId>(new RunNow(process.Id)));

        error.Code.ShouldBe(RejectionCodes.ProcessBlocked);
        host.Store.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunNow_BlockedWithActiveRun_BlockedWins()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = AddBlockedProcess(host);
        host.AddRunningRun(process);

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Run<RunNow, RunId>(new RunNow(process.Id)));

        error.Code.ShouldBe(RejectionCodes.ProcessBlocked);
    }

    [Fact]
    public async Task RunNow_UnknownProcess_IsRejectedNotFound()
    {
        await using ApplicationTestHost host = RunTestHost.Create();

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Run<RunNow, RunId>(new RunNow(ProcessId.New())));

        error.Code.ShouldBe(RejectionCodes.NotFound);
    }

    // Q-15
    [Fact]
    public async Task RunNow_DisabledRunToCompletion_Runs()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        process.Disable(host.Time.GetUtcNow());
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        RunId runId = await host.Run<RunNow, RunId>(new RunNow(process.Id));

        host.Store.Runs[runId].Trigger.ShouldBe(RunTrigger.Manual);
        process.Lifecycle.ShouldBe(ProcessLifecycle.Disabled);

        child.Exit(0);
        await host.EndOf(runId);
    }

    // Q-15
    [Fact]
    public async Task RunNow_DisabledService_IsRejected()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(ProcessKind.Service);
        process.Disable(host.Time.GetUtcNow());

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Run<RunNow, RunId>(new RunNow(process.Id)));

        error.Code.ShouldBe(RejectionCodes.ProcessDisabled);
        host.Store.Runs.ShouldBeEmpty();
    }

    // BI-062
    [Fact]
    public async Task RunNow_WithActiveRun_IsRejectedRunAlreadyActive()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        Run active = await host.StartRunAsync(process);

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Run<RunNow, RunId>(new RunNow(process.Id)));

        error.Code.ShouldBe(RejectionCodes.RunAlreadyActive);
        host.Store.Runs.Count.ShouldBe(1);

        child.Exit(0);
        await host.EndOf(active.Id);
    }

    // Q-3, Review Focus 4
    [Fact]
    public async Task RunNow_CounterAboveLoweredLimit_StillRuns()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        DateTimeOffset now = host.Time.GetUtcNow();
        Process process = host.AddProcess();
        for (int i = 0; i < 3; i++)
        {
            process.ApplyRunOutcome(RunOutcome.Failed, 10, now);
        }

        InstallationSettings settings = host.Store.Settings;
        settings.Change(
            settings.CycleInterval,
            2,
            settings.DefaultTimeLimit,
            settings.DefaultGracePeriod,
            settings.LogRetentionDays,
            settings.HistoryRetentionDays,
            settings.LogLevel,
            settings.CheckForUpdates);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        RunId runId = await host.Run<RunNow, RunId>(new RunNow(process.Id));

        process.Lifecycle.ShouldBe(ProcessLifecycle.Enabled);
        process.ConsecutiveFailures.ShouldBe(3);
        host.Store.Runs[runId].Trigger.ShouldBe(RunTrigger.Manual);

        child.Exit(0);
        await host.EndOf(runId);
    }

    // BI-062
    [Fact]
    public async Task RestartRun_Active_CancelsThenStartsAfterEnd_NeverOverlapping()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess oldChild = host.Launcher.QueueSucceed(Exe);
        FakeRunningProcess newChild = host.Launcher.QueueSucceed(Exe);
        Run oldRun = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();

        await host.Run<RestartRun, Unit>(new RestartRun(process.Id));
        await oldChild.Signalled.Within();

        oldRun.StopReason.ShouldBe(StopReason.UserCancel);
        host.Launcher.LaunchRequests.Count.ShouldBe(1);
        host.Store.Runs.Count.ShouldBe(1);

        oldChild.Exit(1);
        await host.EndOf(oldRun.Id);
        await host.Dispatcher().WaitForHandledAsync<StartPendingRun>(2).Within();
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>(2).Within();

        oldRun.Outcome.ShouldBe(RunOutcome.Cancelled);
        Run newRun = host.ActiveRun(process);
        newRun.Id.ShouldNotBe(oldRun.Id);
        newRun.Trigger.ShouldBe(RunTrigger.Restart);
        host.Launcher.LaunchRequests.Count.ShouldBe(2);
        process.ConsecutiveFailures.ShouldBe(0);
        process.RetryPending.ShouldBeFalse();

        newChild.Exit(0);
        await host.EndOf(newRun.Id);
    }

    [Fact]
    public async Task RestartRun_ServiceKind_OldRunStoppedNewRunStarted()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(ProcessKind.Service);
        FakeRunningProcess oldChild = host.Launcher.QueueSucceed(Exe);
        FakeRunningProcess newChild = host.Launcher.QueueSucceed(Exe);
        Run oldRun = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();

        await host.Run<RestartRun, Unit>(new RestartRun(process.Id));
        await oldChild.Signalled.Within();
        oldChild.Exit(0);
        await host.EndOf(oldRun.Id);
        await host.Dispatcher().WaitForHandledAsync<StartPendingRun>(2).Within();
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>(2).Within();

        oldRun.Outcome.ShouldBe(RunOutcome.Stopped);
        Run newRun = host.ActiveRun(process);
        newRun.Trigger.ShouldBe(RunTrigger.Restart);
        newRun.Kind.ShouldBe(ProcessKind.Service);

        newChild.Exit(0);
        await host.EndOf(newRun.Id);
    }

    [Fact]
    public async Task RestartRun_Idle_StartsRestartRunAndConsumesDueOccurrences()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        DateTimeOffset now = host.Time.GetUtcNow();
        Process process = AddProcessWithDueOneTime(host, now);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        await host.Run<RestartRun, Unit>(new RestartRun(process.Id));

        Run run = host.ActiveRun(process);
        run.Trigger.ShouldBe(RunTrigger.Restart);
        run.CreatedAt.ShouldBe(now);
        process.Schedules.OfType<OneTimeSchedule>().Single().FulfilledAt.ShouldBe(now);

        child.Exit(0);
        await host.EndOf(run.Id);
    }

    [Fact]
    public async Task RestartRun_AlreadyStopping_KeepsFirstReasonAndStillRestarts()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess oldChild = host.Launcher.QueueSucceed(Exe);
        FakeRunningProcess newChild = host.Launcher.QueueSucceed(Exe);
        Run oldRun = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();
        oldRun.RequestStop(StopReason.Shutdown, host.Time.GetUtcNow());

        await host.Run<RestartRun, Unit>(new RestartRun(process.Id));
        await oldChild.Signalled.Within();
        oldChild.Exit(0);
        await host.EndOf(oldRun.Id);
        await host.Dispatcher().WaitForHandledAsync<StartPendingRun>(2).Within();
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>(2).Within();

        oldRun.StopReason.ShouldBe(StopReason.Shutdown);
        Run newRun = host.ActiveRun(process);
        newRun.Trigger.ShouldBe(RunTrigger.Restart);

        newChild.Exit(0);
        await host.EndOf(newRun.Id);
    }

    [Fact]
    public async Task RestartRun_PausedAndBlocked_PausedWins()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = AddBlockedProcess(host);
        host.Store.Settings.Pause(host.Time.GetUtcNow());

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Run<RestartRun, Unit>(new RestartRun(process.Id)));

        error.Code.ShouldBe(RejectionCodes.InstallationPaused);
    }

    [Fact]
    public async Task RestartRun_Blocked_IsRejected()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = AddBlockedProcess(host);

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Run<RestartRun, Unit>(new RestartRun(process.Id)));

        error.Code.ShouldBe(RejectionCodes.ProcessBlocked);
    }

    [Fact]
    public async Task RestartRun_DisabledService_IsRejected()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(ProcessKind.Service);
        process.Disable(host.Time.GetUtcNow());

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Run<RestartRun, Unit>(new RestartRun(process.Id)));

        error.Code.ShouldBe(RejectionCodes.ProcessDisabled);
    }

    [Fact]
    public async Task RestartRun_UnknownProcess_IsRejectedNotFound()
    {
        await using ApplicationTestHost host = RunTestHost.Create();

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Run<RestartRun, Unit>(new RestartRun(ProcessId.New())));

        error.Code.ShouldBe(RejectionCodes.NotFound);
    }

    // BI-058
    [Fact]
    public async Task CancelRun_NothingActive_IsRejected()
    {
        await using ApplicationTestHost host = RunTestHost.Create();

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Run<CancelRun, Unit>(new CancelRun(RunId.New())));

        error.Code.ShouldBe(RejectionCodes.RunNotActive);
    }

    // BI-058
    [Fact]
    public async Task CancelRun_CompletedRun_IsRejected()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        DateTimeOffset now = host.Time.GetUtcNow();
        var run = Run.Begin(process.Id, process.Name, process.Kind, RunTrigger.Manual, null, false, now);
        run.FailToStart("boom", now);
        host.Store.Runs[run.Id] = run;

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Run<CancelRun, Unit>(new CancelRun(run.Id)));

        error.Code.ShouldBe(RejectionCodes.RunNotActive);
    }

    // BI-056
    [Fact]
    public async Task CancelRun_Running_EndsCancelledCounterUnchanged()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        process.ApplyRunOutcome(RunOutcome.Failed, 5, host.Time.GetUtcNow());
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        Run run = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();

        await host.Run<CancelRun, Unit>(new CancelRun(run.Id));
        await child.Signalled.Within();
        child.Exit(1);
        await host.EndOf(run.Id);

        run.StopReason.ShouldBe(StopReason.UserCancel);
        run.Outcome.ShouldBe(RunOutcome.Cancelled);
        process.ConsecutiveFailures.ShouldBe(1);
        process.RetryPending.ShouldBeFalse();
    }

    [Fact]
    public async Task CancelRun_ServiceKind_IsRejectedUseDisable()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(ProcessKind.Service);
        Run run = host.AddRunningRun(process);

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Run<CancelRun, Unit>(new CancelRun(run.Id)));

        error.Code.ShouldBe(RejectionCodes.UseDisable);
        run.StopReason.ShouldBeNull();
        run.State.ShouldBe(RunState.Running);
    }

    [Fact]
    public async Task CancelRun_AlreadyStopping_SucceedsAndKeepsFirstReason()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        Run run = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();
        run.RequestStop(StopReason.Shutdown, host.Time.GetUtcNow());

        await host.Run<CancelRun, Unit>(new CancelRun(run.Id));
        child.Exit(0);
        await host.EndOf(run.Id);

        run.StopReason.ShouldBe(StopReason.Shutdown);
    }

    private static Process AddBlockedProcess(ApplicationTestHost host)
    {
        Process process = host.AddProcess();
        process.ApplyRunOutcome(RunOutcome.Failed, 1, host.Time.GetUtcNow());
        process.Lifecycle.ShouldBe(ProcessLifecycle.Blocked);
        return process;
    }

    private static Process AddProcessWithDueOneTime(ApplicationTestHost host, DateTimeOffset now)
    {
        Process process = new ProcessBuilder()
            .WithExecutable(Exe)
            .WithSchedule(OneTimeSchedule.Create(now - TimeSpan.FromHours(1)))
            .Build();
        host.Store.Processes[process.Id] = process;
        return process;
    }
}
