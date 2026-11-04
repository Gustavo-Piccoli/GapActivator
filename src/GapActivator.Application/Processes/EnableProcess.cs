using GapActivator.Application.Messaging;
using GapActivator.Application.Runs;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Scheduling;
using GapActivator.Domain.Settings;

namespace GapActivator.Application.Processes;

public sealed record EnableProcess(ProcessId Id) : ICommand<Unit>;

/// <summary>
/// Enabling discards the occurrences missed while disabled (Q-11), then blocks the process when its counter already
/// meets the limit. A Service-kind process that is still enabled starts at once.
/// </summary>
internal sealed class EnableProcessHandler(
    IProcessRepository processes,
    IRunRepository runs,
    ISettingsRepository settings,
    IOccurrenceCalculator calculator,
    RunStarter starter,
    TimeProvider time) : ICommandHandler<EnableProcess, Unit>
{
    public async Task<Unit> HandleAsync(EnableProcess command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        DateTimeOffset now = time.GetUtcNow();

        Process process = await processes.GetByIdAsync(command.Id, ct)
            ?? throw OperatorRunRules.NotFound(command.Id);
        InstallationSettings current = await settings.GetAsync(ct);

        process.Enable(now, calculator);
        process.EnforceFailureLimit(current.FailureLimit, now);

        if (process is { Kind: ProcessKind.Service, Lifecycle: ProcessLifecycle.Enabled }
            && !current.IsPaused
            && await runs.GetActiveForProcessAsync(process.Id, ct) is null)
        {
            starter.Begin(process, current, RunTrigger.ServiceKeepAlive, null, false, now);
        }

        return Unit.Value;
    }
}
