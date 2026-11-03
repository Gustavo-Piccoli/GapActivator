using GapActivator.Application.Events;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;

namespace GapActivator.Application.Ports;

/// <summary>Newest CreatedAt first; Page is at least 1; PageSize is 1 to 200.</summary>
public sealed record RunFilter(
    ProcessId? ProcessId,
    IReadOnlyList<RunOutcome>? Outcomes,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Page,
    int PageSize);

public sealed record RunPage(IReadOnlyList<Run> Items, int TotalCount);

public interface IRunHistoryReader
{
    Task<RunPage> ListRunsAsync(RunFilter filter, CancellationToken ct);

    /// <summary>Entries of one run, oldest first.</summary>
    Task<IReadOnlyList<HistoryEntry>> ListEntriesForRunAsync(RunId runId, CancellationToken ct);

    Task<IReadOnlyDictionary<ProcessId, Run>> LastCompletedRunsAsync(CancellationToken ct);
}
