using GapActivator.Application.Messaging;
using GapActivator.Application.Tests.Messaging;
using GapActivator.Application.Tests.Runs;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Scheduling;
using GapActivator.Testing;
using Microsoft.Extensions.Logging;

namespace GapActivator.Application.Tests.Cycle;

public class EvaluateProcessTests
{
    private const string Exe = RunTestHost.Executable;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task Evaluate_DueSchedule_StartsScheduledRun()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        DateTimeOffset now = host.Time.GetUtcNow();
        DateTimeOffset dueAt = now - TimeSpan.FromSeconds(30);
        Process process = host.AddScheduledProcess(dueAt);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        await host.EvaluateAsync(process);

        Run run = host.ActiveRun(process);
        run.Trigger.ShouldBe(RunTrigger.Schedule);
        run.ScheduledFor.ShouldBe(dueAt);
        run.IsLate.ShouldBeFalse();
        host.Supervisor().IsTracking(process.Id).ShouldBeTrue();

        child.Exit(1);
        await host.EndOf(run.Id);
    }

    [Fact]
    public async Task Evaluate_LateOccurrence_RunIsLate()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        DateTimeOffset now = host.Time.GetUtcNow();
        Process process = host.AddScheduledProcess(now - (2 * Interval) - TimeSpan.FromSeconds(1));
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        await host.EvaluateAsync(process);

        Run run = host.ActiveRun(process);
        run.IsLate.ShouldBeTrue();
        child.Exit(1);
        await host.EndOf(run.Id);
    }

    [Fact]
    public async Task Evaluate_ExactlyTwoIntervalsAfterSchedule_RunIsNotLate()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        DateTimeOffset now = host.Time.GetUtcNow();
        Process process = host.AddScheduledProcess(now - (2 * Interval));
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        await host.EvaluateAsync(process);

        Run run = host.ActiveRun(process);
        run.IsLate.ShouldBeFalse();
        child.Exit(1);
        await host.EndOf(run.Id);
    }

    // BI-027
    [Fact]
    public async Task Evaluate_DueWhileActive_RecordsSkipped()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        DateTimeOffset now = host.Time.GetUtcNow();
        DateTimeOffset dueAt = now - TimeSpan.FromSeconds(10);
        Process process = host.AddScheduledProcess(dueAt);
        Run active = host.AddRunningRun(process);

        await host.EvaluateAsync(process);

        host.Store.Runs.Count.ShouldBe(2);
        Run skipped = host.Store.Runs.Values.Single(r => r.Id != active.Id);
        skipped.Outcome.ShouldBe(RunOutcome.Skipped);
        skipped.ScheduledFor.ShouldBe(dueAt);
        skipped.State.ShouldBe(RunState.Completed);
        host.Supervisor().IsTracking(process.Id).ShouldBeFalse();
        process.Schedules.OfType<OneTimeSchedule>().Single().FulfilledAt.ShouldBe(now);
    }

    [Fact]
    public async Task Evaluate_RetryPending_StartsRetryRun()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process process = host.AddProcess();
        process.ApplyRunOutcome(RunOutcome.Failed, 5, host.Time.GetUtcNow());
        process.RetryPending.ShouldBeTrue();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        await host.EvaluateAsync(process);

        Run run = host.ActiveRun(process);
        run.Trigger.ShouldBe(RunTrigger.Retry);
        run.ScheduledFor.ShouldBeNull();
        child.Exit(1);
        await host.EndOf(run.Id);
    }

    [Fact]
    public async Task Evaluate_RetryWhileActive_DoesNothing()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process process = host.AddProcess();
        process.ApplyRunOutcome(RunOutcome.Failed, 5, host.Time.GetUtcNow());
        Run active = host.AddRunningRun(process);

        await host.EvaluateAsync(process);

        host.Store.Runs.Keys.ShouldBe([active.Id]);
        host.Supervisor().IsTracking(process.Id).ShouldBeFalse();
        process.RetryPending.ShouldBeTrue();
    }

    [Fact]
    public async Task Evaluate_NotDue_DoesNothing()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process process = host.AddScheduledProcess(host.Time.GetUtcNow() + TimeSpan.FromHours(1));

        await host.EvaluateAsync(process);

        host.Store.Runs.ShouldBeEmpty();
    }

    // BI-008
    [Fact]
    public async Task Evaluate_Blocked_DoesNothing()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process process = host.AddScheduledProcess(host.Time.GetUtcNow() - TimeSpan.FromSeconds(10));
        process.ApplyRunOutcome(RunOutcome.Failed, 1, host.Time.GetUtcNow());
        process.Lifecycle.ShouldBe(ProcessLifecycle.Blocked);

        await host.EvaluateAsync(process);

        host.Store.Runs.ShouldBeEmpty();
        process.Lifecycle.ShouldBe(ProcessLifecycle.Blocked);
    }

    [Fact]
    public async Task Evaluate_Disabled_DoesNothing()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process process = host.AddScheduledProcess(host.Time.GetUtcNow() - TimeSpan.FromSeconds(10));
        process.Disable(host.Time.GetUtcNow());

        await host.EvaluateAsync(process);

        host.Store.Runs.ShouldBeEmpty();
    }

    // BI-044, Review Focus 4
    [Fact]
    public async Task Evaluate_CounterAboveLoweredLimit_BlocksWithoutRunning()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        DateTimeOffset now = host.Time.GetUtcNow();
        Process process = host.AddScheduledProcess(now - TimeSpan.FromSeconds(10));
        for (int i = 0; i < 3; i++)
        {
            process.ApplyRunOutcome(RunOutcome.Failed, 10, now);
        }

        host.Store.Settings.Change(
            Interval,
            2,
            host.Store.Settings.DefaultTimeLimit,
            host.Store.Settings.DefaultGracePeriod,
            host.Store.Settings.LogRetentionDays,
            host.Store.Settings.HistoryRetentionDays,
            host.Store.Settings.LogLevel,
            host.Store.Settings.CheckForUpdates);

        await host.EvaluateAsync(process);

        process.Lifecycle.ShouldBe(ProcessLifecycle.Blocked);
        host.Store.Runs.ShouldBeEmpty();
        host.Supervisor().IsTracking(process.Id).ShouldBeFalse();
    }

    // BI-064, BI-044
    [Fact]
    public async Task Evaluate_ServiceRunningWithCounterAboveLoweredLimit_ResetsAndIsNotBlocked()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        DateTimeOffset now = host.Time.GetUtcNow();
        Process process = host.AddProcess(ProcessKind.Service);
        for (int i = 0; i < 3; i++)
        {
            process.ApplyRunOutcome(RunOutcome.Crashed, 10, now);
        }

        Run running = host.AddRunningRun(process);
        host.Store.Settings.Change(
            Interval,
            2,
            host.Store.Settings.DefaultTimeLimit,
            host.Store.Settings.DefaultGracePeriod,
            host.Store.Settings.LogRetentionDays,
            host.Store.Settings.HistoryRetentionDays,
            host.Store.Settings.LogLevel,
            host.Store.Settings.CheckForUpdates);

        await host.EvaluateAsync(process);

        process.Lifecycle.ShouldBe(ProcessLifecycle.Enabled);
        process.ConsecutiveFailures.ShouldBe(0);
        host.Store.Runs.Keys.ShouldBe([running.Id]);
        running.State.ShouldBe(RunState.Running);
    }

    // BI-067
    [Fact]
    public async Task Evaluate_ServiceNotRunning_StartsKeepAlive()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process process = host.AddProcess(ProcessKind.Service);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        await host.EvaluateAsync(process);

        Run run = host.ActiveRun(process);
        run.Trigger.ShouldBe(RunTrigger.ServiceKeepAlive);
        host.Supervisor().IsTracking(process.Id).ShouldBeTrue();
        child.Exit(1);
        await host.EndOf(run.Id);
    }

    // BI-067, BI-068
    [Fact]
    public async Task Evaluate_ServiceRunningAndTracked_DoesNotStartSecondRun()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process process = host.AddProcess(ProcessKind.Service);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        Run first = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<Application.Runs.RecordRunLaunched>().Within();

        await host.EvaluateAsync(process);
        await host.EvaluateAsync(process);

        host.Store.Runs.Keys.ShouldBe([first.Id]);
        child.Exit(1);
        await host.EndOf(first.Id);
    }

    [Fact]
    public async Task Evaluate_ServiceRunning_ResetsCounter()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process process = host.AddProcess(ProcessKind.Service);
        process.ApplyRunOutcome(RunOutcome.Failed, 5, host.Time.GetUtcNow());
        process.ConsecutiveFailures.ShouldBe(1);
        host.AddRunningRun(process);

        await host.EvaluateAsync(process);

        process.ConsecutiveFailures.ShouldBe(0);
        host.Store.Runs.Count.ShouldBe(1);
        host.Supervisor().IsTracking(process.Id).ShouldBeFalse();
    }

    // BI-070
    [Fact]
    public async Task Evaluate_ServiceLaunching_DoesNothing()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process process = host.AddProcess(ProcessKind.Service);
        process.ApplyRunOutcome(RunOutcome.Failed, 5, host.Time.GetUtcNow());
        DateTimeOffset now = host.Time.GetUtcNow();
        Run launching = Run.Begin(process.Id, process.Name, process.Kind, RunTrigger.ServiceKeepAlive, null, false, now);
        launching.ClearDomainEvents();
        host.Store.Runs[launching.Id] = launching;

        await host.EvaluateAsync(process);

        process.ConsecutiveFailures.ShouldBe(1);
        host.Store.Runs.Keys.ShouldBe([launching.Id]);
        host.Supervisor().IsTracking(process.Id).ShouldBeFalse();
    }

    // BI-070
    [Fact]
    public async Task Evaluate_ServiceStopping_DoesNothing()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process process = host.AddProcess(ProcessKind.Service);
        process.ApplyRunOutcome(RunOutcome.Failed, 5, host.Time.GetUtcNow());
        Run stopping = host.AddRunningRun(process);
        stopping.RequestStop(StopReason.UserCancel, host.Time.GetUtcNow());
        stopping.State.ShouldBe(RunState.Stopping);

        await host.EvaluateAsync(process);

        process.ConsecutiveFailures.ShouldBe(1);
        host.Store.Runs.Keys.ShouldBe([stopping.Id]);
    }

    [Fact]
    public async Task Evaluate_DisabledService_DoesNothing()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process process = host.AddProcess(ProcessKind.Service);
        process.Disable(host.Time.GetUtcNow());

        await host.EvaluateAsync(process);

        host.Store.Runs.ShouldBeEmpty();
        host.Supervisor().IsTracking(process.Id).ShouldBeFalse();
    }

    [Fact]
    public async Task Evaluate_MissingProcess_IsSilentNoOp()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();

        await host.Run<Application.Cycle.EvaluateProcess, Unit>(new Application.Cycle.EvaluateProcess(ProcessId.New()));

        host.Store.Runs.ShouldBeEmpty();
        host.Log.Entries.ShouldContain(e => e.Level == LogLevel.Debug && e.Category == "EvaluateProcessHandler");
    }
}
