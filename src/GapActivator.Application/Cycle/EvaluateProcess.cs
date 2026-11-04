using GapActivator.Application.Messaging;
using GapActivator.Application.Runs;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Scheduling;
using GapActivator.Domain.Settings;
using Microsoft.Extensions.Logging;

namespace GapActivator.Application.Cycle;

public sealed record EvaluateProcess(ProcessId Id) : IInternalCommand<Unit>;

/// <summary>
/// Decides what one process needs in this cycle: a scheduled or retry run, a skip record, or a keep-alive start.
/// It does not check the pause: the <see cref="CycleRunner"/> does not dispatch while paused.
/// </summary>
internal sealed partial class EvaluateProcessHandler(
    IProcessRepository processes,
    IRunRepository runs,
    ISettingsRepository settings,
    IOccurrenceCalculator calculator,
    RunStarter starter,
    RunSupervisor supervisor,
    TimeProvider time,
    ILogger<EvaluateProcessHandler> logger) : ICommandHandler<EvaluateProcess, Unit>
{
    public async Task<Unit> HandleAsync(EvaluateProcess command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        DateTimeOffset now = time.GetUtcNow();

        Process? process = await processes.GetByIdAsync(command.Id, ct);
        if (process is null)
        {
            LogMissing(command.Id);
            return Unit.Value;
        }

        InstallationSettings current = await settings.GetAsync(ct);

        // BI-008: a Blocked process does nothing. BI-044, Q-3: a lowered limit blocks it here, before anything else.
        process.EnforceFailureLimit(current.FailureLimit, now);
        if (process.Lifecycle != ProcessLifecycle.Enabled)
        {
            return Unit.Value;
        }

        if (process.Kind == ProcessKind.Service)
        {
            await EvaluateServiceAsync(process, current, now, ct);
        }
        else
        {
            await EvaluateRunToCompletionAsync(process, current, now, ct);
        }

        return Unit.Value;
    }

    private async Task EvaluateRunToCompletionAsync(Process process, InstallationSettings current, DateTimeOffset now, CancellationToken ct)
    {
        DueDecision decision = process.EvaluateDue(now, 2 * current.CycleInterval, calculator);
        if (!decision.IsDue)
        {
            return;
        }

        bool active = await runs.GetActiveForProcessAsync(process.Id, ct) is not null;
        if (!active)
        {
            starter.Begin(process, current, decision.Trigger!.Value, decision.ScheduledFor, decision.IsLate, now);
        }
        else if (decision.Trigger == RunTrigger.Schedule)
        {
            // BI-027: an occurrence that comes due while the previous run is active is recorded as skipped.
            runs.Add(Run.Skip(process.Id, process.Name, process.Kind, decision.ScheduledFor!.Value, now));
        }
    }

    private async Task EvaluateServiceAsync(Process process, InstallationSettings current, DateTimeOffset now, CancellationToken ct)
    {
        Run? active = await runs.GetActiveForProcessAsync(process.Id, ct);
        switch (active?.State)
        {
            case RunState.Running:
                process.MarkServiceFoundRunning(now);
                break;
            case RunState.Launching or RunState.Stopping:
                // BI-070: the run is changing state; leave it alone.
                break;
            default:
                // BI-067, BI-068: no active run and no live child: keep the service up.
                if (!supervisor.IsTracking(process.Id))
                {
                    starter.Begin(process, current, RunTrigger.ServiceKeepAlive, null, false, now);
                }

                break;
        }
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Process {ProcessId} was not found when it was evaluated; it was probably deleted during the cycle.")]
    private partial void LogMissing(ProcessId processId);
}
