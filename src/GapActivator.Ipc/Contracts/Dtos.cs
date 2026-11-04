namespace GapActivator.Ipc.Contracts;

// Each DTO mirrors its Application type property for property; an architecture test pins that.

public sealed record ScheduleInputDto(Guid? Id, ScheduleInputKind Kind, DateTimeOffset At, string? Rule);

public sealed record EnvironmentVariableInputDto(string Name, string? Value, bool IsSecret)
{
    /// <summary>Masks a secret value so a logged or displayed message never exposes it.</summary>
    public override string ToString() =>
        $"EnvironmentVariableInputDto {{ Name = {Name}, Value = {(Value is null ? "null" : IsSecret ? "***" : Value)}, IsSecret = {IsSecret} }}";
}

public sealed record ProcessDefinitionDto(
    string Name,
    string Description,
    ProcessKind Kind,
    string Executable,
    string Arguments,
    string? WorkingDirectory,
    IReadOnlyList<EnvironmentVariableInputDto> Environment,
    TimeSpan? TimeLimit,
    TimeSpan? GracePeriod,
    IReadOnlyList<ScheduleInputDto> Schedules);

public sealed record ProcessSavedDto(Guid Id, IReadOnlyList<string> ProcessesSharingExecutable);

public sealed record SecretChangeDto(SecretChangeKind Kind, string? NewValue)
{
    /// <summary>Masks the new value so a logged or displayed message never exposes it.</summary>
    public override string ToString() =>
        $"SecretChangeDto {{ Kind = {Kind}, NewValue = {(NewValue is null ? "null" : "***")} }}";
}

public sealed record EmailInputDto(
    bool Enabled,
    string Host,
    int Port,
    TlsMode Tls,
    string? Username,
    SecretChangeDto Password,
    string Sender,
    IReadOnlyList<string> Recipients,
    IReadOnlyList<AlertKind> Alerts);

public sealed record SettingsInputDto(
    TimeSpan CycleInterval,
    int FailureLimit,
    TimeSpan? DefaultTimeLimit,
    TimeSpan DefaultGracePeriod,
    int LogRetentionDays,
    int HistoryRetentionDays,
    LogLevel LogLevel,
    bool CheckForUpdates,
    EmailInputDto Email);

public sealed record RunFilterDto(
    Guid? ProcessId,
    IReadOnlyList<RunOutcome>? Outcomes,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int Page,
    int PageSize);

public sealed record ActiveRunViewDto(Guid RunId, RunState State, RunTrigger Trigger, DateTimeOffset? StartedAt, int? Pid);

public sealed record ProcessSummaryDto(
    Guid Id,
    string Name,
    ProcessKind Kind,
    ProcessLifecycle Lifecycle,
    int ConsecutiveFailures,
    bool RetryPending,
    DateTimeOffset? NextScheduledAt,
    ActiveRunViewDto? ActiveRun,
    RunOutcome? LastOutcome,
    DateTimeOffset? LastEndedAt);

public sealed record EnvironmentVariableViewDto(string Name, string? Value, bool IsSecret);

public sealed record ScheduleViewDto(
    Guid Id,
    ScheduleInputKind Kind,
    DateTimeOffset At,
    string? Rule,
    DateTimeOffset? NextOccurrenceAt,
    DateTimeOffset? FulfilledAt);

public sealed record ProcessDetailsDto(
    ProcessSummaryDto Summary,
    string Description,
    string Executable,
    string? Arguments,
    string? WorkingDirectory,
    IReadOnlyList<EnvironmentVariableViewDto> Environment,
    TimeSpan? TimeLimit,
    TimeSpan? GracePeriod,
    IReadOnlyList<ScheduleViewDto> Schedules,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool SensitiveHidden);

public sealed record RunRowDto(
    Guid Id,
    Guid ProcessId,
    string ProcessName,
    ProcessKind Kind,
    RunTrigger Trigger,
    RunState State,
    RunOutcome? Outcome,
    bool IsLate,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    int? ExitCode,
    bool Killed);

public sealed record HistoryEntryDto(
    Guid Id,
    DateTimeOffset OccurredAt,
    HistoryEntryKind Kind,
    Guid? ProcessId,
    Guid? RunId,
    RunOutcome? Outcome,
    int? ExitCode,
    string? Message);

public sealed record RunOutputDto(bool Exists, string Text, bool Truncated);

public sealed record RunDetailsDto(RunRowDto Run, string? Error, IReadOnlyList<HistoryEntryDto> Entries, RunOutputDto Output);

public sealed record UpcomingRunDto(Guid ProcessId, string ProcessName, DateTimeOffset At);

public sealed record DashboardDto(
    bool IsPaused,
    IReadOnlyList<RunRowDto> ActiveRuns,
    IReadOnlyList<UpcomingRunDto> Upcoming,
    IReadOnlyList<RunRowDto> RecentFailures);

public sealed record EmailViewDto(
    bool Enabled,
    string Host,
    int Port,
    TlsMode Tls,
    string? Username,
    bool HasPassword,
    string Sender,
    IReadOnlyList<string> Recipients,
    IReadOnlyList<AlertKind> Alerts);

public sealed record SettingsViewDto(
    TimeSpan CycleInterval,
    int FailureLimit,
    TimeSpan? DefaultTimeLimit,
    TimeSpan DefaultGracePeriod,
    int LogRetentionDays,
    int HistoryRetentionDays,
    LogLevel LogLevel,
    bool CheckForUpdates,
    bool IsPaused,
    EmailViewDto Email);

public sealed record RunRowPageDto(IReadOnlyList<RunRowDto> Items, int TotalCount);

public sealed record NotificationDto(
    NotificationSeverity Severity,
    string Title,
    string Message,
    Guid? ProcessId,
    Guid? RunId);
