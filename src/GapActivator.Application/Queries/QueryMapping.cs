using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;

namespace GapActivator.Application.Queries;

internal static class QueryMapping
{
    public static RunRow ToRow(Run run) => new(
        run.Id,
        run.ProcessId,
        run.ProcessNameSnapshot.Value,
        run.Kind,
        run.Trigger,
        run.State,
        run.Outcome,
        run.IsLate,
        run.ScheduledFor,
        run.CreatedAt,
        run.StartedAt,
        run.EndedAt,
        run.ExitCode,
        run.Killed);

    public static ActiveRunView ToActive(Run run) => new(run.Id, run.State, run.Trigger, run.StartedAt, run.Pid);

    public static ProcessSummary ToSummary(Process process, Run? active, Run? lastCompleted) => new(
        process.Id,
        process.Name.Value,
        process.Kind,
        process.Lifecycle,
        process.ConsecutiveFailures,
        process.RetryPending,
        process.NextScheduledAt,
        active is null ? null : ToActive(active),
        lastCompleted?.Outcome,
        lastCompleted?.EndedAt);
}
