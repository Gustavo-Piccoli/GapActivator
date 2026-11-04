using GapActivator.Application.Tests.Runs;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GapActivator.Application.Tests.Cycle;

public class CycleRunnerTests
{
    private const string Exe = RunTestHost.Executable;

    // Q-1
    [Fact]
    public async Task Cycle_Paused_DispatchesNothingButRunsRetention()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        DateTimeOffset now = host.Time.GetUtcNow();
        host.AddScheduledProcess(now - TimeSpan.FromSeconds(10));
        host.Store.Settings.Pause(now);

        TimeSpan interval = await host.Cycle().RunOnceAsync(TestContext.Current.CancellationToken);

        interval.ShouldBe(host.Store.Settings.CycleInterval);
        host.Store.Runs.ShouldBeEmpty();
        host.Store.LogRetentionDays.ShouldBe([host.Store.Settings.LogRetentionDays]);
        host.Store.HistoryPurgeCutoffs.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Cycle_Running_EvaluatesEveryProcessAndReturnsInterval()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        DateTimeOffset now = host.Time.GetUtcNow();
        Process first = host.AddScheduledProcess(now - TimeSpan.FromSeconds(10));
        Process second = host.AddScheduledProcess(now - TimeSpan.FromSeconds(10));
        FakeRunningProcess child1 = host.Launcher.QueueSucceed(Exe);
        FakeRunningProcess child2 = host.Launcher.QueueSucceed(Exe);

        TimeSpan interval = await host.Cycle().RunOnceAsync(TestContext.Current.CancellationToken);

        interval.ShouldBe(TimeSpan.FromSeconds(60));
        Run run1 = host.ActiveRun(first);
        Run run2 = host.ActiveRun(second);
        child1.Exit(1);
        child2.Exit(1);
        await host.EndOf(run1.Id);
        await host.EndOf(run2.Id);
    }

    // BI-039
    [Fact]
    public async Task Cycle_OneProcessThrows_OthersStillEvaluated()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        DateTimeOffset now = host.Time.GetUtcNow();
        Process broken = host.AddScheduledProcess(now - TimeSpan.FromSeconds(10));
        Process healthy = host.AddScheduledProcess(now - TimeSpan.FromSeconds(10));
        host.Store.FailOnLoad(broken.Id);
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);

        await host.Cycle().RunOnceAsync(TestContext.Current.CancellationToken);

        Run run = host.ActiveRun(healthy);
        host.Store.Runs.Values.ShouldNotContain(r => r.ProcessId == broken.Id);
        LogEntry error = host.Log.Entries.Single(e => e.Level == LogLevel.Error && e.Category == "CycleRunner");
        error.Message.ShouldContain(broken.Id.ToString());
        host.CycleFailedEntries().ShouldBe(0);
        child.Exit(1);
        await host.EndOf(run.Id);
    }

    // BI-086
    [Fact]
    public async Task Cycle_RetentionFails_DispatchStillRunsAndRetriedSameDay()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        DateTimeOffset now = host.Time.GetUtcNow();
        Process process = host.AddScheduledProcess(now - TimeSpan.FromSeconds(10));
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        var logRetention = host.Services.GetRequiredService<FakeLogRetention>();
        logRetention.Failure = new IOException("Simulated purge failure.");

        await host.Cycle().RunOnceAsync(TestContext.Current.CancellationToken);

        Run run = host.ActiveRun(process);
        host.Log.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Category == "CycleRunner");
        host.Store.LogRetentionDays.Count.ShouldBe(1);
        host.Store.HistoryPurgeCutoffs.Count.ShouldBe(1, "the history purge runs even when the log purge fails");
        host.CycleFailedEntries().ShouldBe(0);

        logRetention.Failure = null;
        await host.Cycle().RunOnceAsync(TestContext.Current.CancellationToken);

        host.Store.LogRetentionDays.Count.ShouldBe(2);
        host.Store.HistoryPurgeCutoffs.Count.ShouldBe(2);
        child.Exit(1);
        await host.EndOf(run.Id);
    }

    [Fact]
    public async Task Cycle_HistoryRetentionFails_LogRetentionStillRunsAndRetriedSameDay()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        host.Dispatcher().FailNext<Application.Cycle.PurgeHistory>(1, new IOException("Simulated purge failure."));

        await host.Cycle().RunOnceAsync(TestContext.Current.CancellationToken);

        host.Store.LogRetentionDays.Count.ShouldBe(1);
        host.Log.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Category == "CycleRunner");

        await host.Cycle().RunOnceAsync(TestContext.Current.CancellationToken);

        host.Store.LogRetentionDays.Count.ShouldBe(2);
        host.Store.HistoryPurgeCutoffs.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Cycle_RetentionSucceeded_NotRepeatedSameDay()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();

        await host.Cycle().RunOnceAsync(TestContext.Current.CancellationToken);
        host.Time.Advance(TimeSpan.FromHours(3));
        await host.Cycle().RunOnceAsync(TestContext.Current.CancellationToken);

        host.Store.LogRetentionDays.Count.ShouldBe(1);
        host.Store.HistoryPurgeCutoffs.Count.ShouldBe(1);
    }

    // BI-089
    [Fact]
    public async Task Cycle_Retention_PurgesHistoryBeforeNowMinusRetentionDays()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        DateTimeOffset now = host.Time.GetUtcNow();

        await host.Cycle().RunOnceAsync(TestContext.Current.CancellationToken);

        host.Store.HistoryPurgeCutoffs.ShouldBe([now - TimeSpan.FromDays(host.Store.Settings.HistoryRetentionDays)]);
    }

    [Fact]
    public async Task Cycle_RetentionNextLocalDay_RunsAgain()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        var plusThree = TimeZoneInfo.CreateCustomTimeZone("Test+3", TimeSpan.FromHours(3), "Test+3", "Test+3");
        host.Time.SetLocalTimeZone(plusThree);

        // 12:00 UTC is 15:00 local; local midnight is 21:00 UTC, UTC midnight is 12:00 later.
        await host.Cycle().RunOnceAsync(TestContext.Current.CancellationToken);
        host.Store.LogRetentionDays.Count.ShouldBe(1);

        host.Time.Advance(TimeSpan.FromHours(8) + TimeSpan.FromMinutes(59));
        await host.Cycle().RunOnceAsync(TestContext.Current.CancellationToken);
        host.Store.LogRetentionDays.Count.ShouldBe(1, "23:59 local is still the same local day");

        host.Time.Advance(TimeSpan.FromMinutes(1));
        await host.Cycle().RunOnceAsync(TestContext.Current.CancellationToken);
        host.Store.LogRetentionDays.Count.ShouldBe(2, "local midnight starts a new day, 3 h before UTC midnight");
        host.Store.HistoryPurgeCutoffs.Count.ShouldBe(2);
    }
}
