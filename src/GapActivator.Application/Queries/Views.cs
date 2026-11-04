using GapActivator.Application.Events;
using GapActivator.Application.Ports;
using GapActivator.Application.Processes;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Settings;

namespace GapActivator.Application.Queries;

// Read models. They expose ids and enums from the domain, never aggregates or value objects.
public sealed record ActiveRunView(RunId RunId, RunState State, RunTrigger Trigger, DateTimeOffset? StartedAt, int? Pid);

public sealed record ProcessSummary(
    ProcessId Id,
    string Name,
    ProcessKind Kind,
    ProcessLifecycle Lifecycle,
    int ConsecutiveFailures,
    bool RetryPending,
    DateTimeOffset? NextScheduledAt,
    ActiveRunView? ActiveRun,
    RunOutcome? LastOutcome,
    DateTimeOffset? LastEndedAt);

/// <summary>A secret's <paramref name="Value"/> is always null; a plain one is null for a caller that cannot change data.</summary>
public sealed record EnvironmentVariableView(string Name, string? Value, bool IsSecret);

public sealed record ScheduleView(
    Guid Id,
    ScheduleInputKind Kind,
    DateTimeOffset At,
    string? Rule,
    DateTimeOffset? NextOccurrenceAt,
    DateTimeOffset? FulfilledAt);

public sealed record ProcessDetails(
    ProcessSummary Summary,
    string Description,
    string Executable,
    string? Arguments,
    string? WorkingDirectory,
    IReadOnlyList<EnvironmentVariableView> Environment,
    TimeSpan? TimeLimit,
    TimeSpan? GracePeriod,
    IReadOnlyList<ScheduleView> Schedules,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool SensitiveHidden);

public sealed record RunRow(
    RunId Id,
    ProcessId ProcessId,
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

public sealed record RunDetails(RunRow Run, string? Error, IReadOnlyList<HistoryEntry> Entries, RunOutput Output);

public sealed record UpcomingRun(ProcessId ProcessId, string ProcessName, DateTimeOffset At);

public sealed record Dashboard(
    bool IsPaused,
    IReadOnlyList<RunRow> ActiveRuns,
    IReadOnlyList<UpcomingRun> Upcoming,
    IReadOnlyList<RunRow> RecentFailures);

public sealed record EmailView(
    bool Enabled,
    string Host,
    int Port,
    TlsMode Tls,
    string? Username,
    bool HasPassword,
    string Sender,
    IReadOnlyList<string> Recipients,
    IReadOnlyList<AlertKind> Alerts);

public sealed record SettingsView(
    TimeSpan CycleInterval,
    int FailureLimit,
    TimeSpan? DefaultTimeLimit,
    TimeSpan DefaultGracePeriod,
    int LogRetentionDays,
    int HistoryRetentionDays,
    LogLevel LogLevel,
    bool CheckForUpdates,
    bool IsPaused,
    EmailView Email);

public sealed record RunRowPage(IReadOnlyList<RunRow> Items, int TotalCount);
