using GapActivator.Application.Messaging;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Scheduling;
using GapActivator.Domain.Settings;

namespace GapActivator.Application.Runs;

public sealed record RunNow(ProcessId Id) : ICommand<RunId>;

/// <summary>
/// Starts a manual run. It does not enforce the failure limit (Q-3) and it runs a Disabled RunToCompletion process (Q-15).
/// </summary>
internal sealed class RunNowHandler(
    IProcessRepository processes,
    IRunRepository runs,
    ISettingsRepository settings,
    IOccurrenceCalculator calculator,
    RunStarter starter,
    TimeProvider time) : ICommandHandler<RunNow, RunId>
{
    public async Task<RunId> HandleAsync(RunNow command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        DateTimeOffset now = time.GetUtcNow();

        Process process = await processes.GetByIdAsync(command.Id, ct)
            ?? throw OperatorRunRules.NotFound(command.Id);
        InstallationSettings current = await settings.GetAsync(ct);

        OperatorRunRules.RequireStartable(current, process);
        if (await runs.GetActiveForProcessAsync(process.Id, ct) is not null)
        {
            throw new CommandRejectedException(RejectionCodes.RunAlreadyActive, "The process already has an active run.");
        }

        process.ConsumeDueOccurrences(now, calculator);
        return starter.Begin(process, current, RunTrigger.Manual, null, false, now).Id;
    }
}
