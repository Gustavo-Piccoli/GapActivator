using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using GapActivator.Application.Processes;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Scheduling;

namespace GapActivator.Application.Queries;

public sealed record GetProcess(ProcessId Id) : IQuery<ProcessDetails>;

internal sealed class GetProcessHandler(
    IProcessRepository processes,
    IRunRepository runs,
    IRunHistoryReader history,
    CallerAccessor callers) : IQueryHandler<GetProcess, ProcessDetails>
{
    public async Task<ProcessDetails> HandleAsync(GetProcess query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        Process process = await processes.GetByIdAsync(query.Id, ct)
            ?? throw new CommandRejectedException(RejectionCodes.NotFound, $"Process {query.Id} was not found.");
        Run? active = await runs.GetActiveForProcessAsync(process.Id, ct);
        IReadOnlyDictionary<ProcessId, Run> lastCompleted = await history.LastCompletedRunsAsync(ct);

        // Spec section 10: a caller that cannot change data never sees arguments or values; secrets are never shown.
        var canChange = callers.Current.CanChange;
        EnvironmentVariableView[] environment =
        [
            .. process.Launch.Environment.Items.Select(v =>
                new EnvironmentVariableView(v.Name, v.IsSecret || !canChange ? null : v.PlainValue, v.IsSecret)),
        ];

        return new ProcessDetails(
            QueryMapping.ToSummary(process, active, lastCompleted.GetValueOrDefault(process.Id)),
            process.Description,
            process.Launch.Executable.Value,
            canChange ? process.Launch.Arguments : null,
            process.Launch.WorkingDirectory,
            environment,
            process.Limits.TimeLimit,
            process.Limits.GracePeriod,
            [.. process.Schedules.Select(ToView)],
            process.CreatedAt,
            process.UpdatedAt,
            !canChange);
    }

    private static ScheduleView ToView(Schedule schedule) => schedule switch
    {
        OneTimeSchedule oneTime => new ScheduleView(
            oneTime.Id.Value, ScheduleInputKind.OneTime, oneTime.At, null, oneTime.NextOccurrenceAt, oneTime.FulfilledAt),
        RecurringSchedule recurring => new ScheduleView(
            recurring.Id.Value, ScheduleInputKind.Recurring, recurring.Anchor, recurring.Rule.Value, recurring.NextOccurrenceAt, null),
        _ => throw new InvalidOperationException($"Unknown schedule type {schedule.GetType().Name}."),
    };
}
