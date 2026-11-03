namespace GapActivator.Application.Messaging;

public interface IQueryDispatcher
{
    Task<TResult> QueryAsync<TQuery, TResult>(TQuery query, Caller caller, CancellationToken ct)
        where TQuery : IQuery<TResult>;
}
