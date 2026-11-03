using GapActivator.Application.Events;
using GapActivator.Application.Ports;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;

namespace GapActivator.Testing;

/// <summary>History writer, reader and retention over the shared store. Entries are staged until commit.</summary>
public sealed class InMemoryHistory(InMemoryStore store, InMemoryUnitOfWork unitOfWork)
    : IHistoryWriter, IRunHistoryReader, IHistoryRetention
{
    public void Add(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        unitOfWork.Stage(() => store.History.Add(entry));
    }

    /// <summary>Filters by process, outcomes and CreatedAt (From inclusive, To exclusive), newest first.</summary>
    public Task<RunPage> ListRunsAsync(RunFilter filter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfLessThan(filter.Page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(filter.PageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(filter.PageSize, 200);

        Run[] matching = [.. store.Runs.Values
            .Where(r => filter.ProcessId is null || r.ProcessId == filter.ProcessId)
            .Where(r => filter.Outcomes is null || (r.Outcome is { } outcome && filter.Outcomes.Contains(outcome)))
            .Where(r => filter.From is null || r.CreatedAt >= filter.From)
            .Where(r => filter.To is null || r.CreatedAt < filter.To)
            .OrderByDescending(r => r.CreatedAt)];

        Run[] page = [.. matching.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)];
        foreach (Run run in page)
        {
            unitOfWork.Track(run);
        }

        return Task.FromResult(new RunPage(page, matching.Length));
    }

    public Task<IReadOnlyList<HistoryEntry>> ListEntriesForRunAsync(RunId runId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<HistoryEntry>>(
            [.. store.History.Where(e => e.RunId == runId).OrderBy(e => e.OccurredAt)]);

    public Task<IReadOnlyDictionary<ProcessId, Run>> LastCompletedRunsAsync(CancellationToken ct)
    {
        Dictionary<ProcessId, Run> last = store.Runs.Values
            .Where(r => r.State == RunState.Completed)
            .GroupBy(r => r.ProcessId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.EndedAt ?? r.CreatedAt).First());
        foreach (Run run in last.Values)
        {
            unitOfWork.Track(run);
        }

        return Task.FromResult<IReadOnlyDictionary<ProcessId, Run>>(last);
    }

    /// <summary>Removes completed runs ended before the cutoff, their entries and older run-less entries; returns the runs removed.</summary>
    public Task<int> PurgeBeforeAsync(DateTimeOffset cutoff, CancellationToken ct)
    {
        store.HistoryPurgeCutoffs.Add(cutoff);

        RunId[] purgedRuns = [.. store.Runs.Values
            .Where(r => r.State == RunState.Completed && r.EndedAt < cutoff)
            .Select(r => r.Id)];
        foreach (RunId id in purgedRuns)
        {
            store.Runs.Remove(id);
        }

        HashSet<RunId> purged = [.. purgedRuns];
        store.History.RemoveAll(e => e.RunId is { } id ? purged.Contains(id) : e.OccurredAt < cutoff);
        return Task.FromResult(purgedRuns.Length);
    }
}
