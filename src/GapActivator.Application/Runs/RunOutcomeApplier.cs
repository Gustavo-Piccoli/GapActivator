using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Settings;
using Microsoft.Extensions.Logging;

namespace GapActivator.Application.Runs;

/// <summary>Scoped: applies the outcome of a completed run to its process. A missing process is logged; the run still completes.</summary>
internal sealed partial class RunOutcomeApplier(
    IProcessRepository processes,
    ISettingsRepository settings,
    ILogger<RunOutcomeApplier> logger)
{
    public async Task ApplyAsync(Run run, DateTimeOffset now, CancellationToken ct)
    {
        Process? process = await processes.GetByIdAsync(run.ProcessId, ct);
        if (process is null)
        {
            LogProcessMissing(run.ProcessId, run.Id);
            return;
        }

        InstallationSettings current = await settings.GetAsync(ct);
        process.ApplyRunOutcome(run.Outcome!.Value, current.FailureLimit, now);
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Process {ProcessId} of run {RunId} was not found; its outcome was not applied.")]
    private partial void LogProcessMissing(ProcessId processId, RunId runId);
}
