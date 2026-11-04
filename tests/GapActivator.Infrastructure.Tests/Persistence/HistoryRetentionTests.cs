using GapActivator.Application.Cycle;
using GapActivator.Application.Events;
using GapActivator.Application.Ports;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Infrastructure.Persistence;
using GapActivator.Infrastructure.Tests.Support;
using GapActivator.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GapActivator.Infrastructure.Tests.Persistence;

[Trait("Category", "Integration")]
public sealed class HistoryRetentionTests
{
    private static readonly DateTimeOffset Start = ApplicationTestHost.StartTime;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PurgeBefore_OldCompletedRunsAndEntries_RemovesThemAndKeepsTheRest()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();
        DateTimeOffset cutoff = Start;
        ProcessId p1 = ProcessId.New();
        ProcessId p2 = ProcessId.New();
        ProcessId p3 = ProcessId.New();
        Run oldRun = HistoryReaderTests.Completed(p1, cutoff.AddDays(-3), cutoff.AddDays(-2));
        Run recentRun = HistoryReaderTests.Completed(p1, cutoff.AddDays(-3), cutoff.AddHours(1));
        Run endedAtCutoff = HistoryReaderTests.Completed(p2, cutoff.AddDays(-3), cutoff);
        Run activeOld = Run.Begin(p3, ProcessName.Create("Active"), ProcessKind.RunToCompletion, RunTrigger.Manual, null, false, cutoff.AddDays(-9));
        HistoryEntry oldRunEntry = HistoryReaderTests.Entry(HistoryEntryKind.RunCompleted, cutoff.AddDays(-2), oldRun.Id, p1);
        HistoryEntry recentRunOldEntry = HistoryReaderTests.Entry(HistoryEntryKind.RunStarted, cutoff.AddDays(-3), recentRun.Id, p1);
        HistoryEntry activeOldEntry = HistoryReaderTests.Entry(HistoryEntryKind.RunStarted, cutoff.AddDays(-9), activeOld.Id, p3);
        HistoryEntry oldRunless = HistoryReaderTests.Entry(HistoryEntryKind.InstallationPaused, cutoff.AddMinutes(-1), null, null);
        HistoryEntry recentRunless = HistoryReaderTests.Entry(HistoryEntryKind.InstallationResumed, cutoff, null, null);
        await host.SeedAsync(oldRun, recentRun, endedAtCutoff, activeOld);
        await host.SeedAsync(oldRunEntry, recentRunOldEntry, activeOldEntry, oldRunless, recentRunless);

        int purged;
        await using (AsyncServiceScope scope = host.CreateScope())
        {
            purged = await scope.ServiceProvider.GetRequiredService<IHistoryRetention>().PurgeBeforeAsync(cutoff, Ct);
        }

        purged.ShouldBe(1);
        await using GapActivatorDbContext db = host.NewScopeDb();
        (await db.Runs.Select(r => r.Id).ToListAsync(Ct))
            .ShouldBe([recentRun.Id, endedAtCutoff.Id, activeOld.Id], ignoreOrder: true);
        (await db.HistoryEntries.Select(e => e.Id).ToListAsync(Ct))
            .ShouldBe([recentRunOldEntry.Id, activeOldEntry.Id, recentRunless.Id], ignoreOrder: true);
    }

    // BI-089
    [Fact]
    public async Task PurgeHistory_ThroughDispatcher_UsesNowMinusRetentionDays()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();
        ProcessId processId = ProcessId.New();
        DateTimeOffset cutoff = Start.AddDays(-30);
        Run expired = HistoryReaderTests.Completed(processId, cutoff.AddDays(-1), cutoff.AddSeconds(-1));
        Run kept = HistoryReaderTests.Completed(processId, cutoff.AddDays(-1), cutoff);
        await host.SeedAsync(expired, kept);

        int purged = await host.Send<PurgeHistory, int>(new PurgeHistory(30), ct: Ct);

        purged.ShouldBe(1);
        await using GapActivatorDbContext db = host.NewScopeDb();
        (await db.Runs.Select(r => r.Id).ToListAsync(Ct)).ShouldBe([kept.Id]);
    }
}
