using GapActivator.Application.Cycle;
using GapActivator.Application.Events;
using GapActivator.Application.Runs;
using GapActivator.Application.Tests.Messaging;
using GapActivator.Application.Tests.Runs;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Testing;
using Microsoft.Extensions.Logging;

namespace GapActivator.Application.Tests.Cycle;

public class SchedulerLoopTests
{
    private const string Exe = RunTestHost.Executable;
    private static readonly TimeSpan Sixty = TimeSpan.FromSeconds(60);

    // BI-037, Review Focus 4
    [Fact]
    public async Task Loop_IntervalChanged_NextWaitUsesNewInterval()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        using var stop = new CancellationTokenSource();
        Task loop = host.Loop().RunAsync(stop.Token);

        await host.Time.WaitForTimersAsync(1).Within();
        host.Time.TimerDelays[0].ShouldBe(Sixty);
        host.ChangeCycleInterval(TimeSpan.FromSeconds(20));
        host.Time.Advance(Sixty);

        await host.Time.WaitForTimersAsync(2).Within();
        host.Time.TimerDelays[1].ShouldBe(TimeSpan.FromSeconds(20));

        await StopAsync(stop, loop);
    }

    // BI-038
    [Fact]
    public async Task Loop_LongRunActive_NextCycleStillOnTime()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        DateTimeOffset start = host.Time.GetUtcNow();
        Process longRunning = host.AddScheduledProcess(start - TimeSpan.FromSeconds(1));
        Process second = host.AddScheduledProcess(start + TimeSpan.FromSeconds(30));
        FakeRunningProcess child1 = host.Launcher.QueueSucceed(Exe);
        FakeRunningProcess child2 = host.Launcher.QueueSucceed(Exe);
        using var stop = new CancellationTokenSource();

        Task loop = host.Loop().RunAsync(stop.Token);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>(1).Within();
        await host.Time.WaitForTimersAsync(1).Within();
        Run firstRun = host.ActiveRun(longRunning);

        host.Time.Advance(Sixty);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>(2).Within();
        await host.Time.WaitForTimersAsync(2).Within();

        Run secondRun = host.ActiveRun(second);
        secondRun.Trigger.ShouldBe(RunTrigger.Schedule);
        secondRun.CreatedAt.ShouldBe(start + Sixty);
        secondRun.IsLate.ShouldBeFalse();
        firstRun.State.ShouldBe(RunState.Running);
        host.Time.TimerDelays[^1].ShouldBe(Sixty);

        await stop.CancelAsync();
        await child1.Signalled.Within();
        await child2.Signalled.Within();
        child1.Exit(2);
        child2.Exit(2);
        await loop.Within();
        firstRun.Outcome.ShouldBe(RunOutcome.Interrupted);
        secondRun.Outcome.ShouldBe(RunOutcome.Interrupted);
    }

    // BI-040, Review Focus 4
    [Fact]
    public async Task Loop_CycleThrows_RecordsCycleFailedAndContinues()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        host.ChangeCycleInterval(TimeSpan.FromSeconds(120));
        host.Queries().FailNext(1, "Simulated settings failure.");
        using var stop = new CancellationTokenSource();

        Task loop = host.Loop().RunAsync(stop.Token);
        await host.Time.WaitForTimersAsync(1).Within();

        host.Time.TimerDelays[0].ShouldBe(Sixty, "a cycle that failed before reading the settings waits the fallback interval");
        HistoryEntry entry = host.Store.History.Single(h => h.Kind == HistoryEntryKind.CycleFailed);
        entry.Message.ShouldBe("Simulated settings failure.");
        host.Notifications().Events.OfType<CycleFailed>().Single().Error.ShouldBe("Simulated settings failure.");

        host.Time.Advance(Sixty);
        await host.Time.WaitForTimersAsync(2).Within();
        host.Time.TimerDelays[1].ShouldBe(TimeSpan.FromSeconds(120));
        host.CycleFailedEntries().ShouldBe(1);

        await StopAsync(stop, loop);
    }

    // BI-093
    [Fact]
    public async Task Loop_RecordingTheFailureFails_PublishesCycleFailedDirectlyAndContinues()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        host.Queries().FailNext(1, "Simulated settings failure.");
        host.Dispatcher().FailNext<Application.Cycle.RecordCycleFailure>(1);
        using var stop = new CancellationTokenSource();

        Task loop = host.Loop().RunAsync(stop.Token);
        await host.Time.WaitForTimersAsync(1).Within();

        host.CycleFailedEntries().ShouldBe(0, "the history entry could not be written");
        host.Notifications().Events.OfType<CycleFailed>().Single().Error.ShouldBe("Simulated settings failure.");
        host.Log.Entries.ShouldContain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error && e.Category == "SchedulerLoop");

        host.Time.Advance(Sixty);
        await host.Time.WaitForTimersAsync(2).Within();
        loop.IsCompleted.ShouldBeFalse();

        await StopAsync(stop, loop);
    }

    // BI-040
    [Fact]
    public async Task Loop_StoppingToken_EndsWithoutCycleFailed()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        using var stop = new CancellationTokenSource();
        Task loop = host.Loop().RunAsync(stop.Token);
        await host.Time.WaitForTimersAsync(1).Within();

        await StopAsync(stop, loop);

        loop.IsCompletedSuccessfully.ShouldBeTrue();
        host.CycleFailedEntries().ShouldBe(0);
        host.Notifications().Events.OfType<CycleFailed>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Loop_TokenAlreadyCancelled_EndsWithoutCycle()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        await host.Loop().RunAsync(stop.Token).Within();

        host.Store.LogRetentionDays.ShouldBeEmpty();
        host.Time.TimerDelays.ShouldBe([TimeSpan.FromSeconds(15)], "only the shutdown deadline: nothing is tracked, so no grace period");
        host.LoggedAt(LogLevel.Critical, nameof(SchedulerLoop)).ShouldBeFalse();
    }

    [Fact]
    public async Task Loop_ReconcileThrows_Rethrows()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        Run run = await host.StartRunAsync(process);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>().Within();
        host.Dispatcher().FailNext<ReconcileOnStartup>(1, new InvalidOperationException("Simulated reconcile failure."));

        Task loop = host.Loop().RunAsync(TestContext.Current.CancellationToken);
        await child.Signalled.Within();
        child.Exit(2);
        var error = await Should.ThrowAsync<InvalidOperationException>(loop.Within());

        error.Message.ShouldBe("Simulated reconcile failure.");
        host.LoggedAt(LogLevel.Critical, nameof(SchedulerLoop)).ShouldBeTrue();
        host.Dispatcher().Attempts<ReconcileOnStartup>().ShouldBe(1);
        host.Store.LogRetentionDays.ShouldBeEmpty("no cycle ran");
        host.CycleFailedEntries().ShouldBe(0);
        run.Outcome.ShouldBe(RunOutcome.Interrupted, "the shutdown still stopped the tracked runs");
    }

    // BI-073
    [Fact]
    public async Task Loop_ReconcilesOnceBeforeTheFirstCycle()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        using var stop = new CancellationTokenSource();

        Task loop = host.Loop().RunAsync(stop.Token);
        await host.AdvanceAfterTimerAsync(1, Sixty);
        await host.Time.WaitForTimersAsync(2).Within();

        host.Dispatcher().Attempts<ReconcileOnStartup>().ShouldBe(1);
        host.Time.TimerDelays.ShouldBe([Sixty, Sixty], "two cycles ran");
        lock (host.Store.Sync)
        {
            host.Store.History.Count(h => h.Kind == HistoryEntryKind.GapActivatorStarted).ShouldBe(1);
        }

        await StopAsync(stop, loop);
    }

    [Fact]
    public async Task Loop_Stopping_WaitsTheLargestGracePlus15SecondsThenEndsTheLifetime()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process shortGrace = host.AddProcess(gracePeriod: TimeSpan.FromSeconds(30));
        Process longGrace = host.AddProcess(gracePeriod: TimeSpan.FromMinutes(2));
        FakeRunningProcess child1 = host.Launcher.QueueSucceed(Exe);
        FakeRunningProcess child2 = host.Launcher.QueueSucceed(Exe);
        child1.KillFailure = new InvalidOperationException("Simulated kill failure.");
        child2.KillFailure = new InvalidOperationException("Simulated kill failure.");
        using var stop = new CancellationTokenSource();
        Task loop = host.Loop().RunAsync(stop.Token);
        await host.Time.WaitForTimersAsync(1).Within();
        Run run1 = await host.StartRunAsync(shortGrace);
        Run run2 = await host.StartRunAsync(longGrace);
        await host.Dispatcher().WaitForHandledAsync<RecordRunLaunched>(2).Within();

        await stop.CancelAsync();
        await child1.Signalled.Within();
        await child2.Signalled.Within();
        await host.Time.WaitForTimersAsync(4).Within();

        host.Time.TimerDelays.ShouldContain(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(15));
        loop.IsCompleted.ShouldBeFalse();

        host.Time.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(15));
        await loop.Within();
        await host.EndOf(run1.Id);
        await host.EndOf(run2.Id);

        run1.State.ShouldBe(RunState.Stopping);
        run2.State.ShouldBe(RunState.Stopping);
        host.Supervisor().TrackedRuns.ShouldBeEmpty();
        host.Log.Entries.ShouldContain(e =>
            e.Level == LogLevel.Warning && e.Category == nameof(RunSupervisor) && e.Message.Contains("2 run", StringComparison.Ordinal));
    }

    private static async Task StopAsync(CancellationTokenSource stop, Task loop)
    {
        await stop.CancelAsync();
        await loop.Within();
    }
}
