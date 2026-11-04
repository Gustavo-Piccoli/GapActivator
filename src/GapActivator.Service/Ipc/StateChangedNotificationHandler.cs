using GapActivator.Application.Events;
using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Ipc.Contracts;

namespace GapActivator.Service.Ipc;

/// <summary>Turns every published event into a <c>StateChanged</c> hint, with the ids the event carries.</summary>
internal sealed class StateChangedNotificationHandler(IpcConnectionRegistry registry) : INotificationHandler
{
    public Task HandleAsync(IDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        StateChangedDto hint = domainEvent switch
        {
            RunLate e => new(e.ProcessId.Value, e.RunId.Value),
            RunSkipped e => new(e.ProcessId.Value, e.RunId.Value),
            RunStarted e => new(e.ProcessId.Value, e.RunId.Value),
            RunStopRequested e => new(e.ProcessId.Value, e.RunId.Value),
            RunCompleted e => new(e.ProcessId.Value, e.RunId.Value),
            ServiceProcessCrashed e => new(e.ProcessId.Value, e.RunId.Value),
            ProcessBlocked e => new(e.ProcessId.Value, null),
            ProcessUnblocked e => new(e.ProcessId.Value, null),
            _ => new(null, null),
        };

        registry.PostStateChanged(hint);
        return Task.CompletedTask;
    }
}
