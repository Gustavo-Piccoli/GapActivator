using GapActivator.Application.Messaging;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;

namespace GapActivator.Application.Runs;

public sealed record CancelRun(RunId Id) : ICommand<Unit>;

/// <summary>Cancelling a run that is already stopping succeeds silently: its first stop reason stays.</summary>
internal sealed class CancelRunHandler(
    IRunRepository runs,
    AfterCommit afterCommit,
    RunSupervisor supervisor,
    TimeProvider time) : ICommandHandler<CancelRun, Unit>
{
    public async Task<Unit> HandleAsync(CancelRun command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        DateTimeOffset now = time.GetUtcNow();

        Run run = await runs.GetByIdAsync(command.Id, ct) is { State: not RunState.Completed } active
            ? active
            : throw new CommandRejectedException(RejectionCodes.RunNotActive, "The run is not active.");

        if (run.Kind == ProcessKind.Service)
        {
            throw new CommandRejectedException(
                RejectionCodes.UseDisable, "A service run is stopped by disabling the process.");
        }

        OperatorRunRules.StopOnOperatorRequest(run, now, afterCommit, supervisor);
        return Unit.Value;
    }
}
