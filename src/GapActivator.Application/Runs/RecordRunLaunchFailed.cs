using GapActivator.Application.Messaging;
using GapActivator.Domain.Runs;
using Microsoft.Extensions.Logging;

namespace GapActivator.Application.Runs;

public sealed record RecordRunLaunchFailed(RunId RunId, string Error) : IInternalCommand<Unit>;

/// <summary>Idempotent: a run that is missing or already completed is logged and left alone.</summary>
internal sealed partial class RecordRunLaunchFailedHandler(
    IRunRepository runs,
    RunOutcomeApplier outcomes,
    TimeProvider time,
    ILogger<RecordRunLaunchFailedHandler> logger) : ICommandHandler<RecordRunLaunchFailed, Unit>
{
    public async Task<Unit> HandleAsync(RecordRunLaunchFailed command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        DateTimeOffset now = time.GetUtcNow();

        Run? run = await runs.GetByIdAsync(command.RunId, ct);
        if (run is null or { State: RunState.Completed })
        {
            LogIgnored(command.RunId);
            return Unit.Value;
        }

        run.FailToStart(command.Error, now);
        await outcomes.ApplyAsync(run, now, ct);
        return Unit.Value;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The failed launch of run {RunId} was not recorded: the run is missing or already completed.")]
    private partial void LogIgnored(RunId runId);
}
