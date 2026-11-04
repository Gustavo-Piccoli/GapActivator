using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;

namespace GapActivator.Application.Queries;

public sealed record ListProcesses : IQuery<IReadOnlyList<ProcessSummary>>;

internal sealed class ListProcessesHandler(
    IProcessRepository processes,
    IRunRepository runs,
    IRunHistoryReader history) : IQueryHandler<ListProcesses, IReadOnlyList<ProcessSummary>>
{
    public async Task<IReadOnlyList<ProcessSummary>> HandleAsync(ListProcesses query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        // One read of active runs and one of last outcomes for the whole list, not one per process.
        IReadOnlyList<Process> all = await processes.ListAsync(ct);
        IReadOnlyList<Run> activeRuns = await runs.ListActiveAsync(ct);
        IReadOnlyDictionary<ProcessId, Run> lastCompleted = await history.LastCompletedRunsAsync(ct);

        var activeByProcess = new Dictionary<ProcessId, Run>();
        foreach (Run run in activeRuns)
        {
            activeByProcess.TryAdd(run.ProcessId, run);
        }

        return [.. all
            .OrderBy(p => p.Name.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Id.Value)
            .Select(p => QueryMapping.ToSummary(
                p,
                activeByProcess.GetValueOrDefault(p.Id),
                lastCompleted.GetValueOrDefault(p.Id)))];
    }
}
