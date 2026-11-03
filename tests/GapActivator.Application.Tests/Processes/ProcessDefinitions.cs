using GapActivator.Application.Processes;
using GapActivator.Domain.Processes;

namespace GapActivator.Application.Tests.Processes;

internal static class ProcessDefinitions
{
    public const string SampleExecutable = @"C:\Tools\sample.exe";

    public static ProcessDefinition Basic(
        string name = "Nightly export",
        string description = "",
        ProcessKind kind = ProcessKind.RunToCompletion,
        string executable = SampleExecutable,
        IReadOnlyList<EnvironmentVariableInput>? environment = null,
        IReadOnlyList<ScheduleInput>? schedules = null) =>
        new(
            name,
            description,
            kind,
            executable,
            string.Empty,
            null,
            environment ?? [],
            null,
            null,
            schedules ?? []);

    public static ScheduleInput OneTime(DateTimeOffset at, Guid? id = null) =>
        new(id, ScheduleInputKind.OneTime, at, null);

    public static ScheduleInput Recurring(DateTimeOffset anchor, string rule = "FREQ=HOURLY", Guid? id = null) =>
        new(id, ScheduleInputKind.Recurring, anchor, rule);
}
