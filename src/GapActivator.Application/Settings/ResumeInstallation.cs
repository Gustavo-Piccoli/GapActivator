using GapActivator.Application.Messaging;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Scheduling;
using GapActivator.Domain.Settings;

namespace GapActivator.Application.Settings;

public sealed record ResumeInstallation : ICommand<Unit>;

/// <summary>
/// Resuming discards the occurrences that came due while paused, so no catch-up run starts (Q-14). Pending retries
/// are untouched.
/// </summary>
internal sealed class ResumeInstallationHandler(
    ISettingsRepository settings,
    IProcessRepository processes,
    IOccurrenceCalculator calculator,
    TimeProvider time) : ICommandHandler<ResumeInstallation, Unit>
{
    public async Task<Unit> HandleAsync(ResumeInstallation command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        DateTimeOffset now = time.GetUtcNow();

        (await settings.GetAsync(ct)).Resume(now);
        foreach (Process process in await processes.ListAsync(ct))
        {
            process.ConsumeDueOccurrences(now, calculator);
        }

        return Unit.Value;
    }
}
