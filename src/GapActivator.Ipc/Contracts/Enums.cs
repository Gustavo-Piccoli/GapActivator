using System.Text.Json.Serialization;

namespace GapActivator.Ipc.Contracts;

/// <summary>Writes an enum as its member name and rejects numbers and unknown names.</summary>
public sealed class StrictEnumConverter<T>() : JsonStringEnumConverter<T>(namingPolicy: null, allowIntegerValues: false)
    where T : struct, Enum;

[JsonConverter(typeof(StrictEnumConverter<ProcessKind>))]
public enum ProcessKind
{
    RunToCompletion,
    Service,
}

[JsonConverter(typeof(StrictEnumConverter<ProcessLifecycle>))]
public enum ProcessLifecycle
{
    Enabled,
    Disabled,
    Blocked,
}

[JsonConverter(typeof(StrictEnumConverter<RunState>))]
public enum RunState
{
    Launching,
    Running,
    Stopping,
    Completed,
}

[JsonConverter(typeof(StrictEnumConverter<RunOutcome>))]
public enum RunOutcome
{
    Succeeded,
    Failed,
    FailedToStart,
    TimedOut,
    Cancelled,
    Interrupted,
    Abandoned,
    Skipped,
    Crashed,
    Stopped,
}

[JsonConverter(typeof(StrictEnumConverter<RunTrigger>))]
public enum RunTrigger
{
    Schedule,
    Retry,
    Manual,
    Restart,
    ServiceKeepAlive,
}

[JsonConverter(typeof(StrictEnumConverter<ScheduleInputKind>))]
public enum ScheduleInputKind
{
    OneTime,
    Recurring,
}

[JsonConverter(typeof(StrictEnumConverter<SecretChangeKind>))]
public enum SecretChangeKind
{
    Keep,
    Clear,
    Set,
}

[JsonConverter(typeof(StrictEnumConverter<TlsMode>))]
public enum TlsMode
{
    None,
    StartTls,
    Implicit,
}

[JsonConverter(typeof(StrictEnumConverter<LogLevel>))]
public enum LogLevel
{
    Debug,
    Information,
    Warning,
    Error,
}

[JsonConverter(typeof(StrictEnumConverter<AlertKind>))]
public enum AlertKind
{
    ProcessBlocked,
    RunFailed,
    RunTimedOut,
    ServiceProcessCrashed,
    CycleFailed,
    ServiceRestarted,
}

[JsonConverter(typeof(StrictEnumConverter<HistoryEntryKind>))]
public enum HistoryEntryKind
{
    RunLate,
    RunSkipped,
    RunStarted,
    RunStopRequested,
    RunCompleted,
    ServiceProcessCrashed,
    ProcessBlocked,
    ProcessUnblocked,
    InstallationPaused,
    InstallationResumed,
    CycleFailed,
    GapActivatorStarted,
    GapActivatorRestarted,
}

[JsonConverter(typeof(StrictEnumConverter<NotificationSeverity>))]
public enum NotificationSeverity
{
    Information,
    Warning,
    Error,
}

/// <summary>Whether the service enforces the Operators group; wire-only, it has no Application counterpart.</summary>
[JsonConverter(typeof(StrictEnumConverter<OperatorsGroupMode>))]
public enum OperatorsGroupMode
{
    Enabled,
    Disabled,
}
