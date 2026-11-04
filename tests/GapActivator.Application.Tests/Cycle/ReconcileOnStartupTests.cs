using GapActivator.Application.Cycle;
using GapActivator.Application.Events;
using GapActivator.Application.Messaging;
using GapActivator.Application.Runs;
using GapActivator.Application.Tests.Messaging;
using GapActivator.Application.Tests.Runs;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Testing;
using Microsoft.Extensions.Logging;

namespace GapActivator.Application.Tests.Cycle;

public class ReconcileOnStartupTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Limit = TimeSpan.FromHours(1);

    // BI-072
    [Fact]
    public async Task Reconcile_RunningAndAlive_ReadoptedAndSupervised()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process process = host.AddProcess();
        Run run = host.AddRunningRun(process, pid: 4242);
        FakeRunningProcess child = host.AttachAlive(run);

        await ReconcileAsync(host);

        host.Launcher.AttachCalls.ShouldBe([(run.Id, 4242, run.ProcessStartTime!.Value)]);
        host.Supervisor().TrackedRuns.ShouldBe([run.Id]);
        run.State.ShouldBe(RunState.Running);

        child.Exit(0);
        await host.EndOf(run.Id);

        run.Outcome.ShouldBe(RunOutcome.Succeeded);
        process.ConsecutiveFailures.ShouldBe(0);
        child.DisposeCount.ShouldBe(1);
    }

    /// <summary>Such a row cannot be made through the domain; reflection stands in for a damaged database row.</summary>
    [Fact]
    public async Task Reconcile_RunningWithoutStartedAt_RemainderMeasuredFromCreatedAt()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process process = host.AddProcess(timeLimit: Limit, gracePeriod: Grace);
        DateTimeOffset created = host.Time.GetUtcNow();
        Run run = Run.Begin(process.Id, process.Name, process.Kind, RunTrigger.Manual, null, false, created);
        host.Time.Advance(TimeSpan.FromMinutes(10));
        run.MarkRunning(4242, host.Time.GetUtcNow(), host.Time.GetUtcNow());
        typeof(Run).GetProperty(nameof(Run.StartedAt))!.SetValue(run, null);
        run.ClearDomainEvents();
        host.Store.Runs[run.Id] = run;
        FakeRunningProcess child = host.AttachAlive(run);
        host.Time.Advance(TimeSpan.FromMinutes(20));

        await ReconcileAsync(host);
        await host.Time.WaitForTimersAsync(1).Within();

        host.Time.TimerDelays.ShouldBe([TimeSpan.FromMinutes(30)]);

        child.Exit(0);
        await host.EndOf(run.Id);
    }

    // BI-071
    [Fact]
    public async Task Reconcile_RunningButGone_AbandonedCountsFailure()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process process = host.AddProcess();
        Run run = host.AddRunningRun(process);

        await ReconcileAsync(host);

        host.Launcher.AttachCalls.Count.ShouldBe(1);
        run.Outcome.ShouldBe(RunOutcome.Abandoned);
        run.EndedAt.ShouldBe(host.Time.GetUtcNow());
        process.ConsecutiveFailures.ShouldBe(1);
        process.RetryPending.ShouldBeTrue();
        host.Supervisor().TrackedRuns.ShouldBeEmpty();
        host.RunCompletedEntries(run.Id).ShouldBe(1);
    }

    // BI-074
    [Fact]
    public async Task Reconcile_StoppingForShutdownAndGone_InterruptedNoFailure()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process process = host.AddProcess();
        Run run = host.AddRunningRun(process);
        run.RequestStop(StopReason.Shutdown, host.Time.GetUtcNow());
        run.ClearDomainEvents();

        await ReconcileAsync(host);

        host.Launcher.AttachCalls.Count.ShouldBe(1);
        run.Outcome.ShouldBe(RunOutcome.Interrupted);
        run.Killed.ShouldBeFalse();
        run.ExitCode.ShouldBeNull();
        process.ConsecutiveFailures.ShouldBe(0);
        process.RetryPending.ShouldBeFalse();
        host.Supervisor().TrackedRuns.ShouldBeEmpty();
        host.RunCompletedEntries(run.Id).ShouldBe(1);
    }

    // BI-074
    [Fact]
    public async Task Reconcile_LaunchingWithShutdownReason_Interrupted()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process process = host.AddProcess();
        Run run = Run.Begin(process.Id, process.Name, process.Kind, RunTrigger.Manual, null, false, host.Time.GetUtcNow());
        run.RequestStop(StopReason.Shutdown, host.Time.GetUtcNow());
        run.ClearDomainEvents();
        host.Store.Runs[run.Id] = run;

        await ReconcileAsync(host);

        host.Launcher.AttachCalls.ShouldBeEmpty();
        run.Outcome.ShouldBe(RunOutcome.Interrupted);
        process.ConsecutiveFailures.ShouldBe(0);
        process.RetryPending.ShouldBeFalse();
    }

    [Fact]
    public async Task Reconcile_StoppingAndAlive_SignalledAgainThenKilled()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process process = host.AddProcess(gracePeriod: Grace);
        Run run = host.AddRunningRun(process);
        run.RequestStop(StopReason.UserCancel, host.Time.GetUtcNow());
        run.ClearDomainEvents();
        FakeRunningProcess child = host.AttachAlive(run);

        await ReconcileAsync(host);
        await child.Signalled.Within();
        await host.AdvanceAfterTimerAsync(1, Grace);
        await host.EndOf(run.Id);

        host.Time.TimerDelays.ShouldBe([Grace]);
        child.SignalCancelCount.ShouldBe(1);
        child.Killed.ShouldBeTrue();
        run.Outcome.ShouldBe(RunOutcome.Cancelled);
        run.Killed.ShouldBeTrue();
    }

    [Fact]
    public async Task Reconcile_TimeLimitAlreadyExceeded_StopsAtOnceTimedOut()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process process = host.AddProcess(timeLimit: Limit, gracePeriod: Grace);
        Run run = host.AddRunningRun(process);
        FakeRunningProcess child = host.AttachAlive(run);
        host.Time.Advance(Limit + TimeSpan.FromMinutes(5));

        await ReconcileAsync(host);
        await child.Signalled.Within();
        await host.Dispatcher().WaitForHandledAsync<StopRun>().Within();

        run.StopReason.ShouldBe(StopReason.TimeLimit);
        await host.Time.WaitForTimersAsync(1).Within();
        host.Time.TimerDelays.ShouldBe([Grace], "no time-limit timer, only the grace period");

        child.Exit(2);
        await host.EndOf(run.Id);
        run.Outcome.ShouldBe(RunOutcome.TimedOut);
        process.ConsecutiveFailures.ShouldBe(1);
    }

    [Fact]
    public async Task Reconcile_TimeLimitPartlyUsed_WaitsForTheRemainderOfTheCurrentLimit()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process process = host.AddProcess(timeLimit: Limit, gracePeriod: Grace);
        Run run = host.AddRunningRun(process);
        FakeRunningProcess child = host.AttachAlive(run);
        host.Time.Advance(TimeSpan.FromMinutes(20));

        await ReconcileAsync(host);
        await host.Time.WaitForTimersAsync(1).Within();

        host.Time.TimerDelays.ShouldBe([TimeSpan.FromMinutes(40)]);
        child.SignalCancelCount.ShouldBe(0);

        child.Exit(0);
        await host.EndOf(run.Id);
        run.Outcome.ShouldBe(RunOutcome.Succeeded);
    }

    [Fact]
    public async Task Reconcile_RecordsGapActivatorStartedWithCounts()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Run alive = host.AddRunningRun(host.AddProcess(), pid: 1);
        Run gone = host.AddRunningRun(host.AddProcess(), pid: 2);
        Run serviceGone = host.AddRunningRun(host.AddProcess(ProcessKind.Service), pid: 3);
        Process launchingProcess = host.AddProcess();
        Run launching = Run.Begin(
            launchingProcess.Id, launchingProcess.Name, launchingProcess.Kind, RunTrigger.Manual, null, false, host.Time.GetUtcNow());
        host.Store.Runs[launching.Id] = launching;
        var child = new FakeRunningProcess(1, alive.ProcessStartTime!.Value);
        host.Launcher.AttachHandler = (_, pid, _) => pid == 1 ? child : null;

        await ReconcileAsync(host);

        GapActivatorStarted started = host.Notifications().Events.OfType<GapActivatorStarted>().Single();
        started.ReadoptedRuns.ShouldBe(1);
        started.AbandonedRuns.ShouldBe(3);
        host.Store.History.Single(h => h.Kind == HistoryEntryKind.GapActivatorStarted).Message.ShouldBe("1 re-adopted, 3 abandoned");
        gone.Outcome.ShouldBe(RunOutcome.Abandoned);
        serviceGone.Outcome.ShouldBe(RunOutcome.Interrupted);
        launching.Outcome.ShouldBe(RunOutcome.Abandoned);
        host.Supervisor().TrackedRuns.ShouldBe([alive.Id]);

        child.Exit(0);
        await host.EndOf(alive.Id);
    }

    /// <summary>Such a row cannot be made through the domain; reflection stands in for a damaged database row.</summary>
    [Theory]
    [InlineData(nameof(Run.Pid))]
    [InlineData(nameof(Run.ProcessStartTime))]
    public async Task Reconcile_RunningWithoutProcessIdentity_AbandonedWithoutAttaching(string missing)
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process process = host.AddProcess();
        Run run = host.AddRunningRun(process);
        typeof(Run).GetProperty(missing)!.SetValue(run, null);
        host.AttachAlive(run, pid: 4242);

        await ReconcileAsync(host);

        host.Launcher.AttachCalls.ShouldBeEmpty();
        run.Outcome.ShouldBe(RunOutcome.Abandoned);
        host.Supervisor().TrackedRuns.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reconcile_ProcessNoLongerExists_RunAbandonedAndWarningLogged()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process process = host.AddProcess();
        Run run = host.AddRunningRun(process);
        host.Store.Processes.Remove(process.Id);
        host.AttachAlive(run);

        await ReconcileAsync(host);

        host.Launcher.AttachCalls.ShouldBeEmpty();
        run.Outcome.ShouldBe(RunOutcome.Abandoned);
        host.Supervisor().TrackedRuns.ShouldBeEmpty();
        host.Log.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains(process.Id.ToString(), StringComparison.Ordinal));
        host.Notifications().Events.OfType<GapActivatorStarted>().Single().AbandonedRuns.ShouldBe(1);
    }

    [Fact]
    public async Task Reconcile_TryAttachThrows_FailsWithoutAbandoningAndDisposesEarlierHandles()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Run first = host.AddRunningRun(host.AddProcess(), pid: 1);
        Process process = host.AddProcess();
        Run second = host.AddRunningRun(process, pid: 2);
        var child = new FakeRunningProcess(1, first.ProcessStartTime!.Value);
        host.Launcher.AttachHandler = (_, pid, _) => pid == 1 ? child : throw new InvalidOperationException("Simulated attach failure.");

        var error = await Should.ThrowAsync<InvalidOperationException>(ReconcileAsync(host));

        error.Message.ShouldBe("Simulated attach failure.");
        host.Launcher.AttachCalls.Select(c => c.Pid).ShouldBe([1, 2]);
        second.State.ShouldBe(RunState.Running);
        process.ConsecutiveFailures.ShouldBe(0);
        child.DisposeCount.ShouldBe(1);
        host.Supervisor().TrackedRuns.ShouldBeEmpty();
        host.Notifications().Events.OfType<GapActivatorStarted>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Reconcile_CommitFails_NothingAdoptedAndHandlesDisposed()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Run run = host.AddRunningRun(host.AddProcess());
        FakeRunningProcess child = host.AttachAlive(run);
        host.Store.FailNextCommits(1, new InvalidOperationException("Simulated commit failure."));

        await Should.ThrowAsync<InvalidOperationException>(ReconcileAsync(host));

        host.Supervisor().TrackedRuns.ShouldBeEmpty();
        child.DisposeCount.ShouldBe(1);
        child.SignalCancelCount.ShouldBe(0);
    }

    // BI-066
    [Fact]
    public async Task Reconcile_ServiceRunGone_InterruptedNoFailureAndRelaunchedByFirstCycle()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process service = host.AddProcess(ProcessKind.Service, gracePeriod: Grace);
        service.ApplyRunOutcome(RunOutcome.Crashed, 5, host.Time.GetUtcNow());
        Run gone = host.AddRunningRun(service);
        FakeRunningProcess relaunched = host.Launcher.QueueSucceed(RunTestHost.Executable);
        using var stop = new CancellationTokenSource();

        Task loop = host.Loop().RunAsync(stop.Token);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();

        gone.Outcome.ShouldBe(RunOutcome.Interrupted);
        service.ConsecutiveFailures.ShouldBe(1, "the crash before the restart still counts; the interruption does not");
        Run keepAlive = host.ActiveRun(service);
        keepAlive.Trigger.ShouldBe(RunTrigger.ServiceKeepAlive);
        keepAlive.Pid.ShouldBe(relaunched.Pid);

        await stop.CancelAsync();
        await relaunched.Signalled.Within();
        relaunched.Exit(2);
        await loop.Within();
        keepAlive.Outcome.ShouldBe(RunOutcome.Interrupted);
    }

    [Fact]
    public async Task Reconcile_NoActiveRuns_RecordsGapActivatorStartedOnce()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();

        await ReconcileAsync(host);

        GapActivatorStarted started = host.Notifications().Events.OfType<GapActivatorStarted>().Single();
        started.ReadoptedRuns.ShouldBe(0);
        started.AbandonedRuns.ShouldBe(0);
        started.OccurredAt.ShouldBe(host.Time.GetUtcNow());
        host.Store.History.Single(h => h.Kind == HistoryEntryKind.GapActivatorStarted).Message.ShouldBe("0 re-adopted, 0 abandoned");
    }

    [Theory]
    [InlineData(ProcessKind.RunToCompletion, RunOutcome.Abandoned, 1)]
    [InlineData(ProcessKind.Service, RunOutcome.Interrupted, 0)]
    public async Task Reconcile_LaunchingRun_Abandoned(ProcessKind kind, RunOutcome outcome, int failures)
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process process = host.AddProcess(kind);
        DateTimeOffset now = host.Time.GetUtcNow();
        Run run = Run.Begin(process.Id, process.Name, process.Kind, RunTrigger.Manual, null, false, now);
        host.Store.Runs[run.Id] = run;

        await ReconcileAsync(host);

        host.Launcher.AttachCalls.ShouldBeEmpty();
        run.Outcome.ShouldBe(outcome);
        process.ConsecutiveFailures.ShouldBe(failures);
        host.Notifications().Events.OfType<GapActivatorStarted>().Single().AbandonedRuns.ShouldBe(1);
    }

    private static Task<Unit> ReconcileAsync(ApplicationTestHost host) =>
        host.Run<ReconcileOnStartup, Unit>(new ReconcileOnStartup());
}

internal static class ReconcileTestHost
{
    /// <summary>Makes <see cref="Ports.IProcessLauncher.TryAttach"/> find the run's child alive.</summary>
    public static FakeRunningProcess AttachAlive(this ApplicationTestHost host, Run run, int? pid = null)
    {
        var child = new FakeRunningProcess(pid ?? run.Pid!.Value, run.ProcessStartTime ?? run.CreatedAt);
        host.Launcher.AttachResult = child;
        return child;
    }
}
