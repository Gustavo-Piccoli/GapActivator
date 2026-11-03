namespace GapActivator.Application.Messaging;

public interface ICommandDispatcher
{
    Task<TResult> SendAsync<TCommand, TResult>(TCommand command, Caller caller, CancellationToken ct)
        where TCommand : ICommand<TResult>;
}
