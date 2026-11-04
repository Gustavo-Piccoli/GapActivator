using GapActivator.Application.Events;
using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using GapActivator.Domain.Runs;

namespace GapActivator.Application.Queries;

public sealed record GetRun(RunId Id) : IQuery<RunDetails>;

internal sealed class GetRunHandler(
    IRunRepository runs,
    IRunHistoryReader history,
    IRunOutputReader output) : IQueryHandler<GetRun, RunDetails>
{
    private const int OutputTailBytes = 1_048_576;

    public async Task<RunDetails> HandleAsync(GetRun query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        Run run = await runs.GetByIdAsync(query.Id, ct)
            ?? throw new CommandRejectedException(RejectionCodes.NotFound, $"Run {query.Id} was not found.");
        IReadOnlyList<HistoryEntry> entries = await history.ListEntriesForRunAsync(run.Id, ct);
        RunOutput tail = await output.ReadTailAsync(run.Id, OutputTailBytes, ct);
        return new RunDetails(QueryMapping.ToRow(run), run.Error, entries, tail);
    }
}
