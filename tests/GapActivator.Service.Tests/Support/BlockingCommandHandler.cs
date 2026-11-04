using GapActivator.Application.Messaging;

namespace GapActivator.Service.Tests.Support;

/// <summary>A handler that waits until <see cref="Release"/>; cancelling its token ends the wait.</summary>
internal sealed class BlockingCommandHandler<TCommand> : ICommandHandler<TCommand, Unit>
    where TCommand : ICommand<Unit>
{
    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the first command arrives.</summary>
    public Task Reached => _reached.Task;

    /// <summary>Lets every waiting and every later command go on.</summary>
    public void Release() => _released.TrySetResult();

    public async Task<Unit> HandleAsync(TCommand command, CancellationToken ct)
    {
        _reached.TrySetResult();
        await _released.Task.WaitAsync(ct);
        return Unit.Value;
    }
}
