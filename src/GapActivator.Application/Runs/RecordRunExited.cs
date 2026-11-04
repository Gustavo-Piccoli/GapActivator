using GapActivator.Application.Messaging;
using GapActivator.Domain.Runs;
using Microsoft.Extensions.Logging;

namespace GapActivator.Application.Runs;

public sealed record RecordRunExited(RunId RunId, int ExitCode, bool Killed) : IInternalCommand<Unit>;

/// <summary>
/// Idempotent: a run that is missing or already completed is logged and left alone. A run still Launching cannot exit:
/// the domain rejects it and the run stays active for startup reconciliation.
/// </summary>
internal sealed partial class RecordRunExitedHandler(
    IRunRepository runs,
    RunOutcomeApplier outcomes,
    TimeProvider time,
    ILogger<RecordRunExitedHandler> logger) : ICommandHandler<RecordRunExited, Unit>
{
    public async Task<Unit> HandleAsync(RecordRunExited command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        DateTimeOffset now = time.GetUtcNow();

        Run? run = await runs.GetByIdAsync(command.RunId, ct);
        if (run is null or { State: RunState.Completed })
        {
            LogIgnored(command.RunId);
            return Unit.Value;
        }

        run.Exit(command.ExitCode, command.Killed, now);
        await outcomes.ApplyAsync(run, now, ct);
        return Unit.Value;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The exit of run {RunId} was not recorded: the run is missing or already completed.")]
    private partial void LogIgnored(RunId runId);
}
