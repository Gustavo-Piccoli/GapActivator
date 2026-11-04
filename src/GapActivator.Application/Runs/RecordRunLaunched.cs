using GapActivator.Application.Messaging;
using GapActivator.Domain.Runs;
using Microsoft.Extensions.Logging;

namespace GapActivator.Application.Runs;

/// <param name="RunId">The run.</param>
/// <param name="Pid">The child's process id.</param>
/// <param name="StartTime">The child's start time as the operating system reports it (its identity for re-adoption).</param>
/// <param name="LaunchedAt">
/// The supervisor's clock right after the launch returned. It becomes the run's StartedAt, and the supervisor measures
/// the time limit from it, so a launch recorded late (a busy write gate) never shortens the time limit.
/// </param>
public sealed record RecordRunLaunched(RunId RunId, int Pid, DateTimeOffset StartTime, DateTimeOffset LaunchedAt) : IInternalCommand<Unit>;

/// <summary>
/// Idempotent: a run that is missing or already past Launching is logged and left alone. A stop recorded before the
/// supervisor tracked the run found nothing to signal, so a run that becomes Stopping here is stopped after the commit.
/// </summary>
internal sealed partial class RecordRunLaunchedHandler(
    IRunRepository runs,
    AfterCommit afterCommit,
    RunSupervisor supervisor,
    ILogger<RecordRunLaunchedHandler> logger) : ICommandHandler<RecordRunLaunched, Unit>
{
    public async Task<Unit> HandleAsync(RecordRunLaunched command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        Run? run = await runs.GetByIdAsync(command.RunId, ct);
        if (run is not { State: RunState.Launching })
        {
            LogIgnored(command.RunId);
            return Unit.Value;
        }

        run.MarkRunning(command.Pid, command.StartTime, command.LaunchedAt);
        if (run.State == RunState.Stopping)
        {
            afterCommit.Enqueue(_ =>
            {
                supervisor.Stop(command.RunId);
                return Task.CompletedTask;
            });
        }

        return Unit.Value;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The launch of run {RunId} was not recorded: the run is missing or no longer launching.")]
    private partial void LogIgnored(RunId runId);
}
