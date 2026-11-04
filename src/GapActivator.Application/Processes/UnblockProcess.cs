using GapActivator.Application.Messaging;
using GapActivator.Application.Runs;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Scheduling;
using GapActivator.Domain.Settings;

namespace GapActivator.Application.Processes;

public sealed record UnblockProcess(ProcessId Id, bool RunNow) : ICommand<Unit>;

/// <summary>
/// With RunNow the checks that could refuse the run are made before anything changes, so a rejection leaves the process
/// Blocked. An occurrence discarded by the unblock creates no run.
/// </summary>
internal sealed class UnblockProcessHandler(
    IProcessRepository processes,
    IRunRepository runs,
    ISettingsRepository settings,
    IOccurrenceCalculator calculator,
    RunStarter starter,
    TimeProvider time) : ICommandHandler<UnblockProcess, Unit>
{
    public async Task<Unit> HandleAsync(UnblockProcess command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        DateTimeOffset now = time.GetUtcNow();

        Process process = await processes.GetByIdAsync(command.Id, ct)
            ?? throw OperatorRunRules.NotFound(command.Id);
        InstallationSettings current = await settings.GetAsync(ct);

        if (command.RunNow)
        {
            if (current.IsPaused)
            {
                throw new CommandRejectedException(RejectionCodes.InstallationPaused, "The installation is paused.");
            }

            if (await runs.GetActiveForProcessAsync(process.Id, ct) is not null)
            {
                throw new CommandRejectedException(RejectionCodes.RunAlreadyActive, "The process already has an active run.");
            }
        }

        process.Unblock(now, calculator);
        if (command.RunNow)
        {
            starter.Begin(process, current, RunTrigger.Manual, null, false, now);
        }

        return Unit.Value;
    }
}
