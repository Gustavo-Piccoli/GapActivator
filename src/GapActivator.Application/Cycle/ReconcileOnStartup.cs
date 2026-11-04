using GapActivator.Application.Events;
using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using GapActivator.Application.Runs;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Settings;
using Microsoft.Extensions.Logging;

namespace GapActivator.Application.Cycle;

public sealed record ReconcileOnStartup : IInternalCommand<Unit>;

/// <summary>
/// Runs once per start (BI-073): every active run left by the previous instance is re-adopted when its child is still
/// alive (BI-072) or abandoned when it is gone (BI-071; a Service-kind run ends Interrupted, BI-066). One command and
/// one commit; the adoptions happen after the commit. An exception from the launcher is not taken to mean "gone", since
/// abandoning a child that may be alive could start a duplicate: it fails the command. Handles that were attached but
/// not adopted, because the command failed, are disposed with the handler's scope.
/// </summary>
internal sealed partial class ReconcileOnStartupHandler(
    IRunRepository runs,
    IProcessRepository processes,
    ISettingsRepository settings,
    RunOutcomeApplier outcomes,
    IProcessLauncher launcher,
    RunSupervisor supervisor,
    AfterCommit afterCommit,
    EventBuffer events,
    TimeProvider time,
    ILogger<ReconcileOnStartupHandler> logger) : ICommandHandler<ReconcileOnStartup, Unit>, IAsyncDisposable
{
    private readonly List<(RunId RunId, IRunningProcess Child)> _notAdopted = [];

    public async Task<Unit> HandleAsync(ReconcileOnStartup command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        DateTimeOffset now = time.GetUtcNow();
        int readopted = 0;
        int abandoned = 0;
        InstallationSettings current = await settings.GetAsync(ct);

        foreach (Run run in await runs.ListActiveAsync(ct))
        {
            // A Launching run has no child this path can see. A crash between the OS process creation and the first
            // commit can leave an orphan child behind it; Plan 3 owns that window.
            Process? process = null;
            IRunningProcess? child = null;
            if (run.State != RunState.Launching)
            {
                // A run whose process no longer exists is abandoned; the outcome applier logs that at Warning.
                process = await processes.GetByIdAsync(run.ProcessId, ct);
                child = process is null ? null : TryAttach(run);
                if (child is not null)
                {
                    _notAdopted.Add((run.Id, child));
                }
            }

            if (child is null)
            {
                run.Abandon(now);
                await outcomes.ApplyAsync(run, now, ct);
                abandoned++;
                continue;
            }

            RunLimits limits = RunLimits.For(process!, current);
            bool stopping = run.State == RunState.Stopping;

            // The limit of the current definition applies: one changed while the service was down counts.
            TimeSpan? remaining = limits.TimeLimit - (now - (run.StartedAt ?? run.CreatedAt));
            afterCommit.Enqueue(_ =>
            {
                // The supervisor owns the handle from here on.
                _notAdopted.Remove((run.Id, child));
                supervisor.Adopt(run.Id, run.ProcessId, child, remaining, limits.GracePeriod, stopping);
                return Task.CompletedTask;
            });
            readopted++;
        }

        events.Record(new GapActivatorStarted(readopted, abandoned, now));
        return Unit.Value;
    }

    public async ValueTask DisposeAsync()
    {
        foreach ((RunId runId, IRunningProcess child) in _notAdopted)
        {
            try
            {
                await child.DisposeAsync();
            }
            catch (Exception ex)
            {
                LogDisposeFailed(ex, runId);
            }
        }

        _notAdopted.Clear();
    }

    /// <summary>A run without a recorded process identity cannot be matched to a live process, so it counts as gone.</summary>
    private IRunningProcess? TryAttach(Run run) =>
        run is { Pid: { } pid, ProcessStartTime: { } startTime } ? launcher.TryAttach(run.Id, pid, startTime) : null;

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Releasing the attached process of run {RunId} failed.")]
    private partial void LogDisposeFailed(Exception exception, RunId runId);
}
