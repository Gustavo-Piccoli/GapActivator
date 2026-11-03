using GapActivator.Application.Messaging;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;

namespace GapActivator.Application.Processes;

public sealed record DeleteProcess(ProcessId Id) : ICommand<Unit>;

internal sealed class DeleteProcessHandler(
    IProcessRepository processes,
    IRunRepository runs) : ICommandHandler<DeleteProcess, Unit>
{
    public async Task<Unit> HandleAsync(DeleteProcess command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        Process process = await processes.GetByIdAsync(command.Id, ct)
            ?? throw new CommandRejectedException(RejectionCodes.NotFound, $"Process {command.Id} was not found.");

        if (await runs.GetActiveForProcessAsync(process.Id, ct) is not null)
        {
            throw new CommandRejectedException(RejectionCodes.HasActiveRun, "A process with an active run cannot be deleted.");
        }

        // History stays: runs keep a snapshot of the process name.
        processes.Remove(process);
        return Unit.Value;
    }
}
