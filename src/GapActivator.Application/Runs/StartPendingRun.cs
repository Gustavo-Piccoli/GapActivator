using GapActivator.Application.Messaging;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Scheduling;
using GapActivator.Domain.Settings;
using Microsoft.Extensions.Logging;

namespace GapActivator.Application.Runs;

public sealed record StartPendingRun(ProcessId ProcessId) : IInternalCommand<Unit>;

/// <summary>
/// Starts the run that a restart left pending. Anything may have changed while the old run was ending, so the checks
/// are made again; when one applies the restart is logged and skipped, without an error.
/// </summary>
internal sealed partial class StartPendingRunHandler(
    IProcessRepository processes,
    IRunRepository runs,
    ISettingsRepository settings,
    IOccurrenceCalculator calculator,
    RunStarter starter,
    TimeProvider time,
    ILogger<StartPendingRunHandler> logger) : ICommandHandler<StartPendingRun, Unit>
{
    public async Task<Unit> HandleAsync(StartPendingRun command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        DateTimeOffset now = time.GetUtcNow();

        Process? process = await processes.GetByIdAsync(command.ProcessId, ct);
        if (process is null)
        {
            LogSkipped(command.ProcessId, "the process was not found");
            return Unit.Value;
        }

        InstallationSettings current = await settings.GetAsync(ct);
        string? reason = process switch
        {
            _ when current.IsPaused => "the installation is paused",
            { Lifecycle: ProcessLifecycle.Blocked } => "the process is blocked",
            { Kind: ProcessKind.Service, Lifecycle: ProcessLifecycle.Disabled } => "the service is disabled",
            _ => null,
        };

        if (reason is null && await runs.GetActiveForProcessAsync(process.Id, ct) is not null)
        {
            reason = "the process has an active run";
        }

        if (reason is not null)
        {
            LogSkipped(process.Id, reason);
            return Unit.Value;
        }

        process.ConsumeDueOccurrences(now, calculator);
        starter.Begin(process, current, RunTrigger.Restart, null, false, now);
        return Unit.Value;
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "The pending run of process {ProcessId} was skipped: {Reason}.")]
    private partial void LogSkipped(ProcessId processId, string reason);
}
