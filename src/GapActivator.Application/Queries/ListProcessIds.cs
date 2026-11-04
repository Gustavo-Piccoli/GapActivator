using GapActivator.Application.Messaging;
using GapActivator.Domain.Processes;

namespace GapActivator.Application.Queries;

public sealed record ListProcessIds : IQuery<IReadOnlyList<ProcessId>>;

internal sealed class ListProcessIdsHandler(IProcessRepository processes) : IQueryHandler<ListProcessIds, IReadOnlyList<ProcessId>>
{
    public async Task<IReadOnlyList<ProcessId>> HandleAsync(ListProcessIds query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlyList<Process> all = await processes.ListAsync(ct);
        return [.. all.Select(p => p.Id)];
    }
}
