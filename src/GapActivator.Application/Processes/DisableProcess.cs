using GapActivator.Application.Messaging;
using GapActivator.Application.Runs;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;

namespace GapActivator.Application.Processes;

public sealed record DisableProcess(ProcessId Id, bool CancelActiveRun) : ICommand<Unit>;

/// <summary>A Service-kind run is always stopped (it ends Stopped); a RunToCompletion run only when asked, otherwise it finishes.</summary>
internal sealed class DisableProcessHandler(
    IProcessRepository processes,
    IRunRepository runs,
    AfterCommit afterCommit,
    RunSupervisor supervisor,
    TimeProvider time) : ICommandHandler<DisableProcess, Unit>
{
    public async Task<Unit> HandleAsync(DisableProcess command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        DateTimeOffset now = time.GetUtcNow();

        Process process = await processes.GetByIdAsync(command.Id, ct)
            ?? throw OperatorRunRules.NotFound(command.Id);

        process.Disable(now);

        if ((process.Kind == ProcessKind.Service || command.CancelActiveRun)
            && await runs.GetActiveForProcessAsync(process.Id, ct) is { } active)
        {
            OperatorRunRules.StopOnOperatorRequest(active, now, afterCommit, supervisor);
        }

        return Unit.Value;
    }
}
