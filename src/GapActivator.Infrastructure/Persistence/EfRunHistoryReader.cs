using GapActivator.Application.Events;
using GapActivator.Application.Ports;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using Microsoft.EntityFrameworkCore;

namespace GapActivator.Infrastructure.Persistence;

/// <summary>
/// Reads the run history without tracking: queries get a scope of their own, so nothing here is ever saved.
/// </summary>
internal sealed class EfRunHistoryReader(GapActivatorDbContext db) : IRunHistoryReader
{
    public async Task<RunPage> ListRunsAsync(RunFilter filter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfLessThan(filter.Page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(filter.PageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(filter.PageSize, 200);

        IQueryable<Run> query = db.Runs.AsNoTracking();
        if (filter.ProcessId is { } processId)
        {
            query = query.Where(r => r.ProcessId == processId);
        }

        if (filter.Outcomes is { } outcomes)
        {
            RunOutcome[] wanted = [.. outcomes];
            query = query.Where(r => r.Outcome != null && wanted.Contains(r.Outcome.Value));
        }

        if (filter.From is { } from)
        {
            query = query.Where(r => r.CreatedAt >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(r => r.CreatedAt < to);
        }

        int total = await query.CountAsync(ct).ConfigureAwait(false);
        List<Run> items = await query
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return new RunPage(items, total);
    }

    /// <summary>Oldest first; entries of one instant keep the order they were written in (the <c>Ordinal</c>).</summary>
    public async Task<IReadOnlyList<HistoryEntry>> ListEntriesForRunAsync(RunId runId, CancellationToken ct) =>
        await db.HistoryEntries.AsNoTracking()
            .Where(e => e.RunId == runId)
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => EF.Property<int>(e, HistoryEntryConfiguration.OrdinalProperty))
            .ToListAsync(ct)
            .ConfigureAwait(false);

    /// <summary>
    /// Per process, the completed run that no other completed run of the process outlasts (a later <c>EndedAt</c>), with
    /// the greatest id among those that end together. The query keeps every run tied at the latest end; EF Core cannot
    /// translate a comparison of two GUIDs, so the tie is broken here, which still yields exactly one run per process.
    /// </summary>
    public async Task<IReadOnlyDictionary<ProcessId, Run>> LastCompletedRunsAsync(CancellationToken ct)
    {
        List<Run> latest = await db.Runs.AsNoTracking()
            .Where(r => r.State == RunState.Completed)
            .Where(r => !db.Runs.Any(o =>
                o.State == RunState.Completed && o.ProcessId == r.ProcessId && o.EndedAt > r.EndedAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return latest
            .GroupBy(r => r.ProcessId)
            .ToDictionary(g => g.Key, g => g.MaxBy(r => r.Id.Value)!);
    }
}
