using GapActivator.Application.Messaging;
using GapActivator.Domain.Runs;
using Microsoft.Extensions.Logging;

namespace GapActivator.Application.Runs;

public sealed record RecordRunLaunched(RunId RunId, int Pid, DateTimeOffset StartTime) : IInternalCommand<Unit>;

/// <summary>
/// Idempotent: a run that is missing or already past Launching is logged and left alone. A stop recorded before the
/// supervisor tracked the run found nothing to signal, so a run that becomes Stopping here is stopped after the commit.
/// </summary>
internal sealed partial class RecordRunLaunchedHandler(
    IRunRepository runs,
    AfterCommit afterCommit,
    RunSupervisor supervisor,
    TimeProvider time,
    ILogger<RecordRunLaunchedHandler> logger) : ICommandHandler<RecordRunLaunched, Unit>
{
    public async Task<Unit> HandleAsync(RecordRunLaunched command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        DateTimeOffset now = time.GetUtcNow();

        Run? run = await runs.GetByIdAsync(command.RunId, ct);
        if (run is not { State: RunState.Launching })
        {
            LogIgnored(command.RunId);
            return Unit.Value;
        }

        run.MarkRunning(command.Pid, command.StartTime, now);
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
