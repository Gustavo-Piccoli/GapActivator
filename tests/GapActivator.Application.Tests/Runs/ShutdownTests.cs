using GapActivator.Application.Messaging;
using GapActivator.Application.Runs;
using GapActivator.Application.Tests.Messaging;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Testing;
using Microsoft.Extensions.Logging;

namespace GapActivator.Application.Tests.Runs;

public class ShutdownTests
{
    private const string Exe = RunTestHost.Executable;
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);

    // BI-074
    [Fact]
    public async Task Shutdown_ActiveRuns_EndInterruptedNotFailures()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process job = host.AddProcess();
        job.ApplyRunOutcome(RunOutcome.Failed, 5, host.Time.GetUtcNow());
        Process service = host.AddProcess(ProcessKind.Service);
        FakeRunningProcess jobChild = host.Launcher.QueueSucceed(Exe);
        FakeRunningProcess serviceChild = host.Launcher.QueueSucceed(Exe);
        Run jobRun = await host.StartRunAsync(job);
        Run serviceRun = await host.StartRunAsync(service);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>(2).Within();

        Task stopping = host.Supervisor().StopAllAsync(TestContext.Current.CancellationToken);
        await jobChild.Signalled.Within();
        await serviceChild.Signalled.Within();
        jobChild.Exit(2);
        serviceChild.Exit(2);
        await stopping.Within();

        jobRun.StopReason.ShouldBe(StopReason.Shutdown);
        jobRun.Outcome.ShouldBe(RunOutcome.Interrupted);
        serviceRun.Outcome.ShouldBe(RunOutcome.Interrupted);
        job.ConsecutiveFailures.ShouldBe(1);
        job.RetryPending.ShouldBeTrue();
        service.ConsecutiveFailures.ShouldBe(0);
        host.Supervisor().TrackedRuns.ShouldBeEmpty();
    }

    // BI-074, Review Focus 5
    [Fact]
    public async Task Shutdown_DuringLaunch_EndsInterrupted()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        host.Launcher.QueueBlockUntilCancelled(Exe);
        Run run = await host.StartRunAsync(process);
        await host.Launcher.LaunchBlocked.Within();

        await host.Supervisor().StopAllAsync(TestContext.Current.CancellationToken).Within();

        run.Outcome.ShouldBe(RunOutcome.Interrupted);
        run.Pid.ShouldBeNull();
        process.ConsecutiveFailures.ShouldBe(0);
        host.Dispatcher().Attempts<RecordLaunchAborted>().ShouldBe(1);
        host.Supervisor().TrackedRuns.ShouldBeEmpty();
    }

    [Fact]
    public async Task Shutdown_ChildIgnoresSignal_KilledAfterGrace()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(gracePeriod: Grace);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        Run run = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();

        Task stopping = host.Supervisor().StopAllAsync(TestContext.Current.CancellationToken);
        await child.Signalled.Within();
        await host.AdvanceAfterTimerAsync(1, Grace);
        await stopping.Within();

        host.Time.TimerDelays.ShouldBe([Grace]);
        child.Killed.ShouldBeTrue();
        run.Killed.ShouldBeTrue();
        run.Outcome.ShouldBe(RunOutcome.Interrupted);
    }

    [Fact]
    public async Task Shutdown_RunAlreadyStopping_KeepsItsFirstReason()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(gracePeriod: Grace);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        Run run = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();
        await host.Run<StopRun, Unit>(new StopRun(run.Id, StopReason.UserCancel));
        await child.Signalled.Within();

        Task stopping = host.Supervisor().StopAllAsync(TestContext.Current.CancellationToken);
        await host.Dispatcher().WaitForHandledAsync<StopRun>(2).Within();
        child.Exit(2);
        await stopping.Within();

        child.SignalCancelCount.ShouldBe(1);
        run.StopReason.ShouldBe(StopReason.UserCancel);
        run.Outcome.ShouldBe(RunOutcome.Cancelled);
    }

    [Fact]
    public async Task Shutdown_DeadlineExpiresWhileWaiting_ReturnsAndTheRunStaysActive()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(gracePeriod: Grace);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        Run run = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();
        using var deadline = new CancellationTokenSource();

        Task stopping = host.Supervisor().StopAllAsync(deadline.Token);
        await child.Signalled.Within();
        await deadline.CancelAsync();
        await stopping.Within();

        run.State.ShouldBe(RunState.Stopping);
        host.Supervisor().TrackedRuns.ShouldBe([run.Id]);
        host.Log.Entries.ShouldContain(e =>
            e.Level == LogLevel.Warning && e.Category == nameof(RunSupervisor) && e.Message.Contains("1 run", StringComparison.Ordinal));

        child.Exit(2);
        await host.EndOf(run.Id);
        run.Outcome.ShouldBe(RunOutcome.Interrupted);
    }

    [Fact]
    public async Task Shutdown_DeadlineAlreadyPassed_SendsCancelledAndLoggedWithoutThrowing()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(gracePeriod: Grace);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        Run run = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();

        await host.Supervisor().StopAllAsync(new CancellationToken(canceled: true)).Within();

        run.StopReason.ShouldBeNull();
        child.SignalCancelCount.ShouldBe(0);
        host.Supervisor().TrackedRuns.ShouldBe([run.Id]);
        host.LoggedAt(LogLevel.Warning, nameof(RunSupervisor)).ShouldBeTrue();
        host.ErrorsLogged(nameof(RunSupervisor)).ShouldBe(0);

        child.Exit(0);
        await host.EndOf(run.Id);
    }

    [Fact]
    public async Task Shutdown_OneStopFails_OthersStillStoppedAndTheFailedChildIsNotSignalled()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process first = host.AddProcess(gracePeriod: Grace);
        Process second = host.AddProcess(gracePeriod: Grace);
        FakeRunningProcess firstChild = host.Launcher.QueueSucceed(Exe);
        FakeRunningProcess secondChild = host.Launcher.QueueSucceed(Exe);
        Run firstRun = await host.StartRunAsync(first);
        Run secondRun = await host.StartRunAsync(second);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>(2).Within();
        host.Dispatcher().FailNext<StopRun>(1);
        using var deadline = new CancellationTokenSource();

        Task stopping = host.Supervisor().StopAllAsync(deadline.Token);
        await host.Dispatcher().WaitForHandledAsync<StopRun>().Within();
        // Which send fails, and which ticket got which child, depend on harmless races: match them by the outcome.
        Run failedRun = firstRun.StopReason is null ? firstRun : secondRun;
        Run stoppedRun = ReferenceEquals(failedRun, firstRun) ? secondRun : firstRun;
        FakeRunningProcess failed = firstChild.Pid == failedRun.Pid ? firstChild : secondChild;
        FakeRunningProcess signalled = ReferenceEquals(failed, firstChild) ? secondChild : firstChild;
        await signalled.Signalled.Within();
        signalled.Exit(2);
        await host.EndOf(stoppedRun.Id);
        await host.ErrorLoggedAsync(nameof(RunSupervisor)).Within();
        await deadline.CancelAsync();
        await stopping.Within();

        host.Dispatcher().Attempts<StopRun>().ShouldBe(2);
        failed.SignalCancelCount.ShouldBe(0);
        failedRun.StopReason.ShouldBeNull();
        host.Supervisor().TrackedRuns.ShouldBe([failedRun.Id]);
        host.ErrorsLogged(nameof(RunSupervisor)).ShouldBe(1);
        stoppedRun.Outcome.ShouldBe(RunOutcome.Interrupted);

        failed.Exit(0);
        await host.EndOf(failedRun.Id);
    }

    [Fact]
    public async Task Shutdown_PendingRestart_NotStarted()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess(gracePeriod: Grace);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        Run run = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();
        host.Supervisor().StartAfterEnd(run.Id, process.Id);
        int startsBefore = host.Dispatcher().Attempts<StartPendingRun>();

        Task stopping = host.Supervisor().StopAllAsync(TestContext.Current.CancellationToken);
        await child.Signalled.Within();
        child.Exit(2);
        await stopping.Within();

        run.Outcome.ShouldBe(RunOutcome.Interrupted);
        host.Log.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Category == nameof(RunSupervisor)
            && e.Message.Contains("restart", StringComparison.OrdinalIgnoreCase));
        host.Dispatcher().Attempts<StartPendingRun>().ShouldBe(startsBefore, "StartRunAsync itself sends one");
        host.Store.Runs.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Shutdown_RestartRequestedAfterStopAllBegan_Dropped()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process process = host.AddProcess();
        await host.Supervisor().StopAllAsync(TestContext.Current.CancellationToken).Within();

        host.Supervisor().StartAfterEnd(RunId.New(), process.Id);

        host.Log.Entries.ShouldContain(e => e.Level == LogLevel.Information && e.Category == nameof(RunSupervisor)
            && e.Message.Contains("restart", StringComparison.OrdinalIgnoreCase));
        host.Dispatcher().Attempts<StartPendingRun>().ShouldBe(0);
    }

    [Fact]
    public async Task Shutdown_TicketAfterStopAllBegan_NotLaunchedEndsInterrupted()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process running = host.AddProcess(gracePeriod: Grace);
        Process late = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        Run runningRun = await host.StartRunAsync(running);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();

        Task stopping = host.Supervisor().StopAllAsync(TestContext.Current.CancellationToken);
        await child.Signalled.Within();
        Run lateRun = await host.StartRunAsync(late);
        await host.Dispatcher().WaitForHandledAsync<RecordLaunchAborted>().Within();

        lateRun.StopReason.ShouldBe(StopReason.Shutdown);
        lateRun.Outcome.ShouldBe(RunOutcome.Interrupted);
        late.ConsecutiveFailures.ShouldBe(0);
        host.Launcher.LaunchRequests.Select(r => r.RunId).ShouldBe([runningRun.Id]);
        stopping.IsCompleted.ShouldBeFalse();

        child.Exit(2);
        await stopping.Within();
        host.Supervisor().TrackedRuns.ShouldBeEmpty();
    }

    [Fact]
    public async Task Shutdown_TicketWhileWaiting_StopAllWaitsForItsEnd()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        Process running = host.AddProcess(gracePeriod: Grace);
        Process late = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        Run runningRun = await host.StartRunAsync(running);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();
        CommandHold abort = host.Dispatcher().HoldBefore<RecordLaunchAborted>();
        using var deadline = new CancellationTokenSource();

        Task stopping = host.Supervisor().StopAllAsync(deadline.Token);
        await child.Signalled.Within();
        Run lateRun = await host.StartRunAsync(late);
        await abort.Reached.Within();
        child.Exit(2);
        await host.EndOf(runningRun.Id);

        // Had it waited only for the runs tracked when it began, it would now return without reaching the deadline.
        await deadline.CancelAsync();
        await stopping.Within();
        host.Supervisor().TrackedRuns.ShouldBe([lateRun.Id]);
        host.Log.Entries.ShouldContain(e =>
            e.Level == LogLevel.Warning && e.Category == nameof(RunSupervisor) && e.Message.Contains("1 run", StringComparison.Ordinal));

        abort.Release();
        await host.EndOf(lateRun.Id);
        lateRun.Outcome.ShouldBe(RunOutcome.Interrupted);
    }

    [Fact]
    public async Task EndLifetimeAndDispose_InEitherOrderMoreThanOnce_DoNotThrow()
    {
        await using ApplicationTestHost host = RunTestHost.Create();

        host.Supervisor().EndLifetime();
        host.DisposeSupervisor();
        host.Supervisor().EndLifetime();
        host.DisposeSupervisor();

        host.Supervisor().TrackedRuns.ShouldBeEmpty();
        host.Supervisor().LargestGracePeriod.ShouldBe(TimeSpan.Zero);
        host.ErrorsLogged(nameof(RunSupervisor)).ShouldBe(0);
    }
}
