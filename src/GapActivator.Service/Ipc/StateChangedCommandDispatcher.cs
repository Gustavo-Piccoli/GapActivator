using GapActivator.Application.Messaging;
using GapActivator.Ipc.Contracts;

namespace GapActivator.Service.Ipc;

/// <summary>
/// Wraps the command dispatcher: once a command that is not ungated has been handled, every client gets a
/// <c>StateChanged</c> hint without ids. A send that fails posts nothing.
/// </summary>
internal sealed class StateChangedCommandDispatcher(ICommandDispatcher inner, IpcConnectionRegistry registry) : ICommandDispatcher
{
    public async Task<TResult> SendAsync<TCommand, TResult>(TCommand command, Caller caller, CancellationToken ct)
        where TCommand : ICommand<TResult>
    {
        TResult result = await inner.SendAsync<TCommand, TResult>(command, caller, ct).ConfigureAwait(false);
        if (command is not IUngatedCommand)
        {
            registry.PostStateChanged(new StateChangedDto(null, null));
        }

        return result;
    }
}
