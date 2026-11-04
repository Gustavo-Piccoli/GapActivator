using GapActivator.Application.Messaging;
using GapActivator.Domain.Runs;
using Microsoft.Extensions.Logging;

namespace GapActivator.Application.Runs;

public sealed record StopRun(RunId RunId, StopReason Reason) : IInternalCommand<Unit>;

/// <summary>A no-op when the run is not active: a stop can arrive just after the run ended.</summary>
internal sealed partial class StopRunHandler(
    IRunRepository runs,
    AfterCommit afterCommit,
    RunSupervisor supervisor,
    TimeProvider time,
    ILogger<StopRunHandler> logger) : ICommandHandler<StopRun, Unit>
{
    public async Task<Unit> HandleAsync(StopRun command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        Run? run = await runs.GetByIdAsync(command.RunId, ct);
        if (run is null or { State: RunState.Completed })
        {
            LogNotActive(command.RunId, command.Reason);
            return Unit.Value;
        }

        run.RequestStop(command.Reason, time.GetUtcNow());
        afterCommit.Enqueue(_ =>
        {
            supervisor.Stop(command.RunId);
            return Task.CompletedTask;
        });
        return Unit.Value;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Run {RunId} is not active; the stop ({Reason}) was ignored.")]
    private partial void LogNotActive(RunId runId, StopReason reason);
}
