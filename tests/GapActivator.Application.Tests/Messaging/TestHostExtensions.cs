using GapActivator.Application.Messaging;
using GapActivator.Testing;

namespace GapActivator.Application.Tests.Messaging;

/// <summary>Sends and queries with the xUnit test cancellation token, so a stuck test can be cancelled.</summary>
public static class TestHostExtensions
{
    public static Task<TResult> Run<TCommand, TResult>(
        this ApplicationTestHost host, TCommand command, Caller? caller = null)
        where TCommand : ICommand<TResult> =>
        host.Send<TCommand, TResult>(command, caller, TestContext.Current.CancellationToken);

    public static Task<TResult> Ask<TQuery, TResult>(
        this ApplicationTestHost host, TQuery query, Caller? caller = null)
        where TQuery : IQuery<TResult> =>
        host.Query<TQuery, TResult>(query, caller, TestContext.Current.CancellationToken);
}
