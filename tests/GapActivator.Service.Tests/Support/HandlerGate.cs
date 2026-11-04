using GapActivator.Application.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace GapActivator.Service.Tests.Support;

/// <summary>
/// Holds the real handler of one command until <see cref="Release"/>: it wraps the registered handler descriptor, so
/// the handler still runs with its own dependencies once released. Cancelling the command's token ends the wait.
/// </summary>
internal sealed class HandlerGate<TCommand, TResult> where TCommand : ICommand<TResult>
{
    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private HandlerGate()
    {
    }

    /// <summary>Completes when the first command arrives at the gate.</summary>
    public Task Reached => _reached.Task;

    /// <summary>Replaces the command's handler registration with a gated wrapper around the original.</summary>
    public static HandlerGate<TCommand, TResult> Install(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        ServiceDescriptor original = services.Single(d => d.ServiceType == typeof(ICommandHandler<TCommand, TResult>));
        Type implementation = original.ImplementationType
            ?? throw new InvalidOperationException($"The handler of {typeof(TCommand).Name} has no implementation type.");

        var gate = new HandlerGate<TCommand, TResult>();
        services.Remove(original);
        services.Add(ServiceDescriptor.Describe(
            typeof(ICommandHandler<TCommand, TResult>),
            sp => new GatedHandler(gate, (ICommandHandler<TCommand, TResult>)ActivatorUtilities.CreateInstance(sp, implementation)),
            original.Lifetime));
        return gate;
    }

    /// <summary>Lets every waiting and every later command go on.</summary>
    public void Release() => _released.TrySetResult();

    private sealed class GatedHandler(HandlerGate<TCommand, TResult> gate, ICommandHandler<TCommand, TResult> inner)
        : ICommandHandler<TCommand, TResult>, IAsyncDisposable
    {
        public async Task<TResult> HandleAsync(TCommand command, CancellationToken ct)
        {
            gate._reached.TrySetResult();
            await gate._released.Task.WaitAsync(ct);
            return await inner.HandleAsync(command, ct);
        }

        public async ValueTask DisposeAsync()
        {
            switch (inner)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
    }
}
