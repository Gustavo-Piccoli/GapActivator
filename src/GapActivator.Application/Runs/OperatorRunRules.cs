using GapActivator.Application.Messaging;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Settings;

namespace GapActivator.Application.Runs;

/// <summary>What the operator commands share: the checks that must pass before a run is begun, and stopping a run.</summary>
internal static class OperatorRunRules
{
    /// <summary>Checks 1 to 3 of Run now and Restart, in that order: the first one that applies wins.</summary>
    public static void RequireStartable(InstallationSettings settings, Process process)
    {
        if (settings.IsPaused)
        {
            throw new CommandRejectedException(RejectionCodes.InstallationPaused, "The installation is paused.");
        }

        if (process.Lifecycle == ProcessLifecycle.Blocked)
        {
            throw new CommandRejectedException(RejectionCodes.ProcessBlocked, "The process is blocked; unblock it first.");
        }

        if (process is { Kind: ProcessKind.Service, Lifecycle: ProcessLifecycle.Disabled })
        {
            throw new CommandRejectedException(RejectionCodes.ProcessDisabled, "The service is disabled; enable it first.");
        }
    }

    /// <summary>Records the stop on the run now; the supervisor is told once the command has committed.</summary>
    public static void StopOnOperatorRequest(
        Run run, DateTimeOffset now, AfterCommit afterCommit, RunSupervisor supervisor)
    {
        run.RequestStop(StopReason.UserCancel, now);
        RunId runId = run.Id;
        afterCommit.Enqueue(_ =>
        {
            supervisor.Stop(runId);
            return Task.CompletedTask;
        });
    }

    public static CommandRejectedException NotFound(ProcessId id) =>
        new(RejectionCodes.NotFound, $"Process {id} was not found.");
}
