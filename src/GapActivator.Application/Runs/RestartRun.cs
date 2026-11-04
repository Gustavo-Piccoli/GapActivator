using GapActivator.Application.Messaging;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Scheduling;
using GapActivator.Domain.Settings;

namespace GapActivator.Application.Runs;

public sealed record RestartRun(ProcessId Id) : ICommand<Unit>;

/// <summary>
/// With no active run this is a Run now with trigger Restart. With one, the run is stopped and the new run begins only
/// after the old one was recorded as ended (§8): the supervisor sends StartPendingRun for that. A run that is already
/// stopping keeps its first reason, and the restart is still registered.
/// </summary>
internal sealed class RestartRunHandler(
    IProcessRepository processes,
    IRunRepository runs,
    ISettingsRepository settings,
    IOccurrenceCalculator calculator,
    RunStarter starter,
    AfterCommit afterCommit,
    RunSupervisor supervisor,
    TimeProvider time) : ICommandHandler<RestartRun, Unit>
{
    public async Task<Unit> HandleAsync(RestartRun command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        DateTimeOffset now = time.GetUtcNow();

        Process process = await processes.GetByIdAsync(command.Id, ct)
            ?? throw OperatorRunRules.NotFound(command.Id);
        InstallationSettings current = await settings.GetAsync(ct);

        OperatorRunRules.RequireStartable(current, process);

        if (await runs.GetActiveForProcessAsync(process.Id, ct) is { } active)
        {
            OperatorRunRules.StopOnOperatorRequest(active, now, afterCommit, supervisor);
            ProcessId processId = process.Id;
            RunId runId = active.Id;
            afterCommit.Enqueue(_ =>
            {
                supervisor.StartAfterEnd(runId, processId);
                return Task.CompletedTask;
            });
            return Unit.Value;
        }

        process.ConsumeDueOccurrences(now, calculator);
        starter.Begin(process, current, RunTrigger.Restart, null, false, now);
        return Unit.Value;
    }
}
