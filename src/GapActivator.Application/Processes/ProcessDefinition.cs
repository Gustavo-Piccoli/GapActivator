using GapActivator.Domain.Processes;

namespace GapActivator.Application.Processes;

public enum ScheduleInputKind
{
    OneTime,
    Recurring,
}

/// <summary>One schedule of a <see cref="ProcessDefinition"/>. <paramref name="At"/> is the instant (OneTime) or the anchor (Recurring).</summary>
public sealed record ScheduleInput(Guid? Id, ScheduleInputKind Kind, DateTimeOffset At, string? Rule);

/// <summary>A secret with a null <paramref name="Value"/> keeps the stored secret of that name.</summary>
public sealed record EnvironmentVariableInput(string Name, string? Value, bool IsSecret);

public sealed record ProcessDefinition(
    string Name,
    string Description,
    ProcessKind Kind,
    string Executable,
    string Arguments,
    string? WorkingDirectory,
    IReadOnlyList<EnvironmentVariableInput> Environment,
    TimeSpan? TimeLimit,
    TimeSpan? GracePeriod,
    IReadOnlyList<ScheduleInput> Schedules);

/// <summary>The saved process and the names of the other processes that launch the same executable (BI-010).</summary>
public sealed record ProcessSaved(ProcessId Id, IReadOnlyList<string> ProcessesSharingExecutable);
