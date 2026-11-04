using GapActivator.Application.Events;
using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;

namespace GapActivator.Application.Settings;

public sealed record RestartGapActivator : ICommand<Unit>;

/// <summary>Records the restart for the history (BI-094); the service restarts only after the commit.</summary>
internal sealed class RestartGapActivatorHandler(
    EventBuffer events,
    AfterCommit afterCommit,
    IServiceControl serviceControl,
    TimeProvider time) : ICommandHandler<RestartGapActivator, Unit>
{
    public Task<Unit> HandleAsync(RestartGapActivator command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        events.Record(new GapActivatorRestarted(time.GetUtcNow()));
        afterCommit.Enqueue(serviceControl.RestartAsync);
        return Task.FromResult(Unit.Value);
    }
}
