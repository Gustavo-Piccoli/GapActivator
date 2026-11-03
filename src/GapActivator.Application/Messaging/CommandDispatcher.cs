using GapActivator.Application.Events;
using GapActivator.Application.Ports;
using GapActivator.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GapActivator.Application.Messaging;

/// <summary>
/// Runs a command: authorize, wait on the <see cref="WriteGate"/>, handle, write the events as history entries and
/// commit in one unit of work, release the gate, run the <see cref="AfterCommit"/> actions, then publish the events.
/// The actions and the publishing run after the gate is released but before the scope is disposed, so the handler's
/// scoped services stay alive for them. If the handler or the commit throws, nothing is committed, the actions are
/// dropped, nothing is published, the gate is released and the exception propagates. A failing action is logged and
/// the remaining actions and the publishing still run.
/// An <see cref="IUngatedCommand"/> takes no gate, dequeues no aggregate events, writes no history and does not
/// commit; its actions and <see cref="EventBuffer"/> events still run and are published after the handler returns,
/// which keeps one code path.
/// </summary>
public sealed partial class CommandDispatcher(
    IServiceScopeFactory scopes,
    WriteGate gate,
    ILogger<CommandDispatcher> logger) : ICommandDispatcher
{
    public async Task<TResult> SendAsync<TCommand, TResult>(TCommand command, Caller caller, CancellationToken ct)
        where TCommand : ICommand<TResult>
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);

        Authorize(command, caller);

        bool ungated = command is IUngatedCommand;
        bool gateHeld = false;
        if (!ungated)
        {
            await gate.WaitAsync(ct);
            gateHeld = true;
        }

        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            IServiceProvider services = scope.ServiceProvider;
            services.GetRequiredService<CallerAccessor>().Current = caller;
            var handler = services.GetRequiredService<ICommandHandler<TCommand, TResult>>();

            TResult result = await handler.HandleAsync(command, ct);

            var events = new List<IDomainEvent>();
            if (!ungated)
            {
                var unitOfWork = services.GetRequiredService<IUnitOfWork>();
                events.AddRange(unitOfWork.DequeueDomainEvents());
                events.AddRange(services.GetRequiredService<EventBuffer>().Drain());

                var history = services.GetRequiredService<IHistoryWriter>();
                foreach (IDomainEvent e in events)
                {
                    HistoryEntry? entry = HistoryEntryFactory.From(e, Guid.CreateVersion7);
                    if (entry is not null)
                    {
                        history.Add(entry);
                    }
                }

                await unitOfWork.CommitAsync(ct);
            }
            else
            {
                events.AddRange(services.GetRequiredService<EventBuffer>().Drain());
            }

            if (gateHeld)
            {
                gate.Release();
                gateHeld = false;
            }

            foreach (Func<CancellationToken, Task> action in services.GetRequiredService<AfterCommit>().Drain())
            {
                try
                {
                    await action(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogActionFailed(ex, typeof(TCommand).Name);
                }
            }

            await services.GetRequiredService<EventPublisher>().PublishAsync(events, ct);
            return result;
        }
        finally
        {
            if (gateHeld)
            {
                gate.Release();
            }
        }
    }

    private static void Authorize<TResult>(ICommand<TResult> command, Caller caller)
    {
        bool allowed = command is IInternalCommand<TResult> ? caller.IsSystem : caller.CanChange;
        if (!allowed)
        {
            throw new CommandRejectedException(
                RejectionCodes.Unauthorized, $"{caller.Name} is not allowed to send {command.GetType().Name}.");
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "An after-commit action of {Command} failed.")]
    private partial void LogActionFailed(Exception exception, string command);
}
