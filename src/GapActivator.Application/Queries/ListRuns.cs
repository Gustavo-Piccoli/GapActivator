using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using GapActivator.Domain.Common;

namespace GapActivator.Application.Queries;

public sealed record ListRuns(RunFilter Filter) : IQuery<RunRowPage>;

internal sealed class ListRunsHandler(IRunHistoryReader history) : IQueryHandler<ListRuns, RunRowPage>
{
    public async Task<RunRowPage> HandleAsync(ListRuns query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        RunFilter filter = query.Filter ?? throw new DomainException("A run filter is required.");

        if (filter.Page < 1)
        {
            throw new DomainException("Page must be at least 1.");
        }

        if (filter.PageSize is < 1 or > 200)
        {
            throw new DomainException("Page size must be between 1 and 200.");
        }

        RunPage page = await history.ListRunsAsync(filter, ct);
        return new RunRowPage([.. page.Items.Select(QueryMapping.ToRow)], page.TotalCount);
    }
}
