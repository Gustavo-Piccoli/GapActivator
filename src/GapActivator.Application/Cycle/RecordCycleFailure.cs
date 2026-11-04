using GapActivator.Application.Events;
using GapActivator.Application.Messaging;

namespace GapActivator.Application.Cycle;

public sealed record RecordCycleFailure(string Error) : IInternalCommand<Unit>;

/// <summary>Records <see cref="CycleFailed"/> through the buffer, so the dispatcher writes the history entry and publishes it.</summary>
internal sealed class RecordCycleFailureHandler(EventBuffer events, TimeProvider time) : ICommandHandler<RecordCycleFailure, Unit>
{
    public Task<Unit> HandleAsync(RecordCycleFailure command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        events.Record(new CycleFailed(command.Error, time.GetUtcNow()));
        return Task.FromResult(Unit.Value);
    }
}
