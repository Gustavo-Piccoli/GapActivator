using GapActivator.Application.Events;
using GapActivator.Application.Ports;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Infrastructure.Tests.Support;
using GapActivator.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GapActivator.Infrastructure.Tests.Persistence;

[Trait("Category", "Integration")]
public sealed class HistoryReaderTests
{
    private static readonly DateTimeOffset Start = ApplicationTestHost.StartTime;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListRuns_FivePagedRuns_NewestCreatedAtFirstWithPagingAndTotal()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();
        ProcessId processId = ProcessId.New();
        Run[] runs = [.. Enumerable.Range(1, 5).Select(i => Completed(processId, Start.AddMinutes(i), Start.AddMinutes(i + 1)))];
        await host.SeedAsync(runs);

        RunPage page = await ListAsync(host, new RunFilter(null, null, null, null, 2, 2));

        page.TotalCount.ShouldBe(5);
        page.Items.Select(r => r.Id).ShouldBe([runs[2].Id, runs[1].Id]);
    }

    [Fact]
    public async Task ListRuns_SameCreatedAt_OrdersByIdDescendingSoPagesDoNotOverlap()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();
        ProcessId processId = ProcessId.New();
        Run[] runs = [.. Enumerable.Range(0, 4).Select(_ => Completed(processId, Start, Start.AddMinutes(1)))];
        await host.SeedAsync(runs);

        RunPage first = await ListAsync(host, new RunFilter(null, null, null, null, 1, 2));
        RunPage second = await ListAsync(host, new RunFilter(null, null, null, null, 2, 2));

        RunId[] expected = [.. runs.Select(r => r.Id).OrderByDescending(id => id.Value)];
        first.Items.Concat(second.Items).Select(r => r.Id).ShouldBe(expected);
    }

    [Fact]
    public async Task ListRuns_ProcessOutcomesAndRange_FromInclusiveToExclusive()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();
        ProcessId a = ProcessId.New();
        ProcessId b = ProcessId.New();
        Run before = Completed(a, Start.AddMinutes(-1), Start);
        Run atFrom = Completed(a, Start, Start.AddMinutes(1));
        Run failedInside = Completed(a, Start.AddMinutes(5), Start.AddMinutes(6), exitCode: 1);
        Run atTo = Completed(a, Start.AddMinutes(10), Start.AddMinutes(11));
        Run otherProcess = Completed(b, Start.AddMinutes(2), Start.AddMinutes(3));
        await host.SeedAsync(before, atFrom, failedInside, atTo, otherProcess);

        RunPage range = await ListAsync(host, new RunFilter(a, null, Start, Start.AddMinutes(10), 1, 50));
        RunPage failed = await ListAsync(host, new RunFilter(a, [RunOutcome.Failed, RunOutcome.TimedOut], null, null, 1, 50));
        RunPage everyone = await ListAsync(host, new RunFilter(null, [RunOutcome.Succeeded], Start, Start.AddMinutes(10), 1, 50));

        range.Items.Select(r => r.Id).ShouldBe([failedInside.Id, atFrom.Id]);
        range.TotalCount.ShouldBe(2);
        failed.Items.Select(r => r.Id).ShouldBe([failedInside.Id]);
        everyone.Items.Select(r => r.Id).ShouldBe([otherProcess.Id, atFrom.Id]);
    }

    [Fact]
    public async Task ListRuns_RunWithoutOutcome_NeverMatchesAnOutcomeFilter()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();
        Run active = Run.Begin(ProcessId.New(), ProcessName.Create("Active"), ProcessKind.RunToCompletion, RunTrigger.Manual, null, false, Start);
        Run done = Completed(ProcessId.New(), Start, Start.AddMinutes(1));
        await host.SeedAsync(active, done);

        RunPage filtered = await ListAsync(host, new RunFilter(null, [.. Enum.GetValues<RunOutcome>()], null, null, 1, 50));
        RunPage unfiltered = await ListAsync(host, new RunFilter(null, null, null, null, 1, 50));

        filtered.Items.Select(r => r.Id).ShouldBe([done.Id]);
        unfiltered.TotalCount.ShouldBe(2);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 201)]
    public async Task ListRuns_PageOrPageSizeOutOfRange_Throws(int page, int pageSize)
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => ListAsync(host, new RunFilter(null, null, null, null, page, pageSize)));
    }

    [Fact]
    public async Task ListEntriesForRun_SameInstant_KeepsInsertionOrder()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();
        RunId runId = RunId.New();
        ProcessId processId = ProcessId.New();
        HistoryEntry started = Entry(HistoryEntryKind.RunStarted, Start, runId, processId);
        HistoryEntry stop = Entry(HistoryEntryKind.RunStopRequested, Start, runId, processId);
        HistoryEntry earlier = Entry(HistoryEntryKind.RunLate, Start.AddSeconds(-1), runId, processId);
        HistoryEntry other = Entry(HistoryEntryKind.RunStarted, Start, RunId.New(), processId);

        // One commit, written in this order: the Ordinal, not the id, keeps the two entries of the same instant apart.
        await host.SeedAsync(started, stop, earlier, other);

        await using AsyncServiceScope scope = host.CreateScope();
        IReadOnlyList<HistoryEntry> entries = await scope.ServiceProvider.GetRequiredService<IRunHistoryReader>()
            .ListEntriesForRunAsync(runId, Ct);

        entries.Select(e => e.Id).ShouldBe([earlier.Id, started.Id, stop.Id]);
    }

    [Fact]
    public async Task LastCompletedRuns_OnePerProcessLatestEnded_EvenOnTies()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();
        ProcessId a = ProcessId.New();
        ProcessId b = ProcessId.New();
        ProcessId onlyActive = ProcessId.New();
        Run aOld = Completed(a, Start, Start.AddMinutes(1));
        Run aNew = Completed(a, Start.AddMinutes(2), Start.AddMinutes(9));
        Run bTie1 = Completed(b, Start, Start.AddMinutes(5));
        Run bTie2 = Completed(b, Start.AddMinutes(1), Start.AddMinutes(5));
        Run active = Run.Begin(onlyActive, ProcessName.Create("Active"), ProcessKind.RunToCompletion, RunTrigger.Manual, null, false, Start);
        Run aActive = Run.Begin(a, ProcessName.Create("Done"), ProcessKind.RunToCompletion, RunTrigger.Manual, null, false, Start.AddMinutes(20));
        await host.SeedAsync(aOld, aNew, bTie1, bTie2, active, aActive);

        await using AsyncServiceScope scope = host.CreateScope();
        IReadOnlyDictionary<ProcessId, Run> last = await scope.ServiceProvider.GetRequiredService<IRunHistoryReader>()
            .LastCompletedRunsAsync(Ct);

        last.Keys.ShouldBe([a, b], ignoreOrder: true);
        last[a].Id.ShouldBe(aNew.Id);
        RunId tieWinner = bTie1.Id.Value.CompareTo(bTie2.Id.Value) > 0 ? bTie1.Id : bTie2.Id;
        last[b].Id.ShouldBe(tieWinner);
    }

    internal static Run Completed(ProcessId processId, DateTimeOffset createdAt, DateTimeOffset endedAt, int exitCode = 0)
    {
        Run run = Run.Begin(processId, ProcessName.Create("Done"), ProcessKind.RunToCompletion, RunTrigger.Manual, null, false, createdAt);
        run.MarkRunning(4242, createdAt, createdAt);
        run.Exit(exitCode, false, endedAt);
        return run;
    }

    internal static HistoryEntry Entry(HistoryEntryKind kind, DateTimeOffset at, RunId? runId, ProcessId? processId) =>
        new(Guid.CreateVersion7(), at, kind, processId, runId, null, null, null);

    private static async Task<RunPage> ListAsync(PersistenceTestHost host, RunFilter filter)
    {
        await using AsyncServiceScope scope = host.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IRunHistoryReader>().ListRunsAsync(filter, Ct);
    }
}
