using Microsoft.Extensions.DependencyInjection;

namespace GapActivator.Application.Messaging;

/// <summary>Runs a query in its own scope. There is no write gate and any caller may query.</summary>
public sealed class QueryDispatcher(IServiceScopeFactory scopes) : IQueryDispatcher
{
    public async Task<TResult> QueryAsync<TQuery, TResult>(TQuery query, Caller caller, CancellationToken ct)
        where TQuery : IQuery<TResult>
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(caller);

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CallerAccessor>().Current = caller;
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<TQuery, TResult>>();
        return await handler.HandleAsync(query, ct);
    }
}
