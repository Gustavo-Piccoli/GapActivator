using GapActivator.Application.Messaging;
using GapActivator.Domain.Runs;
using Microsoft.Extensions.Logging;

namespace GapActivator.Application.Runs;

public sealed record RecordLaunchAborted(RunId RunId) : IInternalCommand<Unit>;

/// <summary>Idempotent: a run that is missing or already completed is logged and left alone.</summary>
internal sealed partial class RecordLaunchAbortedHandler(
    IRunRepository runs,
    RunOutcomeApplier outcomes,
    TimeProvider time,
    ILogger<RecordLaunchAbortedHandler> logger) : ICommandHandler<RecordLaunchAborted, Unit>
{
    public async Task<Unit> HandleAsync(RecordLaunchAborted command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        DateTimeOffset now = time.GetUtcNow();

        Run? run = await runs.GetByIdAsync(command.RunId, ct);
        if (run is null or { State: RunState.Completed })
        {
            LogIgnored(command.RunId);
            return Unit.Value;
        }

        run.AbortLaunch(now);
        await outcomes.ApplyAsync(run, now, ct);
        return Unit.Value;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The aborted launch of run {RunId} was not recorded: the run is missing or already completed.")]
    private partial void LogIgnored(RunId runId);
}
