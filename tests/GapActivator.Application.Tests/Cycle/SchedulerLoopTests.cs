using GapActivator.Application.Events;
using GapActivator.Application.Runs;
using GapActivator.Application.Tests.Messaging;
using GapActivator.Application.Tests.Runs;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Testing;

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

        await StopAsync(stop, loop);
        child1.Exit(1);
        child2.Exit(1);
        await host.EndOf(firstRun.Id);
        await host.EndOf(secondRun.Id);
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
        host.Time.TimersCreated.ShouldBe(0);
    }

    private static async Task StopAsync(CancellationTokenSource stop, Task loop)
    {
        await stop.CancelAsync();
        await loop.Within();
    }
}
