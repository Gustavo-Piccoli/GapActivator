using GapActivator.Application.Messaging;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using AppEvents = GapActivator.Application.Events;
using AppPorts = GapActivator.Application.Ports;
using AppProcesses = GapActivator.Application.Processes;
using AppQueries = GapActivator.Application.Queries;
using AppSettings = GapActivator.Application.Settings;
using DomainRuns = GapActivator.Domain.Runs;
using DomainSettings = GapActivator.Domain.Settings;
using Wire = GapActivator.Ipc.Contracts;

namespace GapActivator.Service.Ipc;

/// <summary>
/// Maps between the wire DTOs and the Application types, one method per pair. Enums map by member name through
/// <see cref="EnumMap"/>; ids are wrapped in <see cref="ProcessId"/> and <see cref="RunId"/> and unwrapped with <c>.Value</c>.
/// </summary>
internal static class IpcMapping
{
    // Wire -> Application.
    public static AppProcesses.ScheduleInput ToApplication(this Wire.ScheduleInputDto dto) =>
        new(dto.Id, EnumMap.To<AppProcesses.ScheduleInputKind>(dto.Kind), dto.At, dto.Rule);

    public static AppProcesses.EnvironmentVariableInput ToApplication(this Wire.EnvironmentVariableInputDto dto) =>
        new(dto.Name, dto.Value, dto.IsSecret);

    public static AppProcesses.ProcessDefinition ToApplication(this Wire.ProcessDefinitionDto dto) =>
        new(
            dto.Name,
            dto.Description,
            EnumMap.To<ProcessKind>(dto.Kind),
            dto.Executable,
            dto.Arguments,
            dto.WorkingDirectory,
            [.. dto.Environment.Select(e => e.ToApplication())],
            dto.TimeLimit,
            dto.GracePeriod,
            [.. dto.Schedules.Select(s => s.ToApplication())]);

    public static AppSettings.SecretChange ToApplication(this Wire.SecretChangeDto dto) =>
        new(EnumMap.To<AppSettings.SecretChangeKind>(dto.Kind), dto.NewValue);

    public static AppSettings.EmailInput ToApplication(this Wire.EmailInputDto dto) =>
        new(
            dto.Enabled,
            dto.Host,
            dto.Port,
            EnumMap.To<DomainSettings.TlsMode>(dto.Tls),
            dto.Username,
            dto.Password.ToApplication(),
            dto.Sender,
            [.. dto.Recipients],
            [.. dto.Alerts.Select(a => EnumMap.To<DomainSettings.AlertKind>(a))]);

    public static AppSettings.SettingsInput ToApplication(this Wire.SettingsInputDto dto) =>
        new(
            dto.CycleInterval,
            dto.FailureLimit,
            dto.DefaultTimeLimit,
            dto.DefaultGracePeriod,
            dto.LogRetentionDays,
            dto.HistoryRetentionDays,
            EnumMap.To<DomainSettings.LogLevel>(dto.LogLevel),
            dto.CheckForUpdates,
            dto.Email.ToApplication());

    public static AppPorts.RunFilter ToApplication(this Wire.RunFilterDto dto) =>
        new(
            dto.ProcessId is { } id ? new ProcessId(id) : null,
            dto.Outcomes is null ? null : [.. dto.Outcomes.Select(o => EnumMap.To<DomainRuns.RunOutcome>(o))],
            dto.From,
            dto.To,
            dto.Page,
            dto.PageSize);

    // Application -> wire.
    public static Wire.ProcessSavedDto ToWire(this AppProcesses.ProcessSaved saved) =>
        new(saved.Id.Value, [.. saved.ProcessesSharingExecutable]);

    public static Wire.RunNowResponse ToWire(this RunId id) => new(id.Value);

    public static Wire.EmptyResponse ToWire(this Unit unit) => new();

    public static Wire.ListProcessesResponse ToListResponse(this IReadOnlyList<AppQueries.ProcessSummary> items) =>
        new([.. items.Select(i => i.ToWire())]);

    public static Wire.PreviewScheduleResponse ToPreviewResponse(this IReadOnlyList<DateTimeOffset> occurrences) =>
        new([.. occurrences]);

    public static Wire.ActiveRunViewDto ToWire(this AppQueries.ActiveRunView view) =>
        new(
            view.RunId.Value,
            EnumMap.To<Wire.RunState>(view.State),
            EnumMap.To<Wire.RunTrigger>(view.Trigger),
            view.StartedAt,
            view.Pid);

    public static Wire.ProcessSummaryDto ToWire(this AppQueries.ProcessSummary summary) =>
        new(
            summary.Id.Value,
            summary.Name,
            EnumMap.To<Wire.ProcessKind>(summary.Kind),
            EnumMap.To<Wire.ProcessLifecycle>(summary.Lifecycle),
            summary.ConsecutiveFailures,
            summary.RetryPending,
            summary.NextScheduledAt,
            summary.ActiveRun?.ToWire(),
            EnumMap.ToNullable<Wire.RunOutcome>(summary.LastOutcome),
            summary.LastEndedAt);

    public static Wire.EnvironmentVariableViewDto ToWire(this AppQueries.EnvironmentVariableView view) =>
        new(view.Name, view.Value, view.IsSecret);

    public static Wire.ScheduleViewDto ToWire(this AppQueries.ScheduleView view) =>
        new(
            view.Id,
            EnumMap.To<Wire.ScheduleInputKind>(view.Kind),
            view.At,
            view.Rule,
            view.NextOccurrenceAt,
            view.FulfilledAt);

    public static Wire.ProcessDetailsDto ToWire(this AppQueries.ProcessDetails details) =>
        new(
            details.Summary.ToWire(),
            details.Description,
            details.Executable,
            details.Arguments,
            details.WorkingDirectory,
            [.. details.Environment.Select(e => e.ToWire())],
            details.TimeLimit,
            details.GracePeriod,
            [.. details.Schedules.Select(s => s.ToWire())],
            details.CreatedAt,
            details.UpdatedAt,
            details.SensitiveHidden);

    public static Wire.RunRowDto ToWire(this AppQueries.RunRow row) =>
        new(
            row.Id.Value,
            row.ProcessId.Value,
            row.ProcessName,
            EnumMap.To<Wire.ProcessKind>(row.Kind),
            EnumMap.To<Wire.RunTrigger>(row.Trigger),
            EnumMap.To<Wire.RunState>(row.State),
            EnumMap.ToNullable<Wire.RunOutcome>(row.Outcome),
            row.IsLate,
            row.ScheduledFor,
            row.CreatedAt,
            row.StartedAt,
            row.EndedAt,
            row.ExitCode,
            row.Killed);

    public static Wire.HistoryEntryDto ToWire(this AppEvents.HistoryEntry entry) =>
        new(
            entry.Id,
            entry.OccurredAt,
            EnumMap.To<Wire.HistoryEntryKind>(entry.Kind),
            entry.ProcessId?.Value,
            entry.RunId?.Value,
            EnumMap.ToNullable<Wire.RunOutcome>(entry.Outcome),
            entry.ExitCode,
            entry.Message);

    public static Wire.RunOutputDto ToWire(this AppPorts.RunOutput output) =>
        new(output.Exists, output.Text, output.Truncated);

    public static Wire.RunDetailsDto ToWire(this AppQueries.RunDetails details) =>
        new(
            details.Run.ToWire(),
            details.Error,
            [.. details.Entries.Select(e => e.ToWire())],
            details.Output.ToWire());

    public static Wire.UpcomingRunDto ToWire(this AppQueries.UpcomingRun run) =>
        new(run.ProcessId.Value, run.ProcessName, run.At);

    public static Wire.DashboardDto ToWire(this AppQueries.Dashboard dashboard) =>
        new(
            dashboard.IsPaused,
            [.. dashboard.ActiveRuns.Select(r => r.ToWire())],
            [.. dashboard.Upcoming.Select(u => u.ToWire())],
            [.. dashboard.RecentFailures.Select(r => r.ToWire())]);

    public static Wire.EmailViewDto ToWire(this AppQueries.EmailView view) =>
        new(
            view.Enabled,
            view.Host,
            view.Port,
            EnumMap.To<Wire.TlsMode>(view.Tls),
            view.Username,
            view.HasPassword,
            view.Sender,
            [.. view.Recipients],
            [.. view.Alerts.Select(a => EnumMap.To<Wire.AlertKind>(a))]);

    public static Wire.SettingsViewDto ToWire(this AppQueries.SettingsView view) =>
        new(
            view.CycleInterval,
            view.FailureLimit,
            view.DefaultTimeLimit,
            view.DefaultGracePeriod,
            view.LogRetentionDays,
            view.HistoryRetentionDays,
            EnumMap.To<Wire.LogLevel>(view.LogLevel),
            view.CheckForUpdates,
            view.IsPaused,
            view.Email.ToWire());

    public static Wire.RunRowPageDto ToWire(this AppQueries.RunRowPage page) =>
        new([.. page.Items.Select(r => r.ToWire())], page.TotalCount);

    public static Wire.NotificationDto ToWire(this AppPorts.AppNotification notification) =>
        new(
            EnumMap.To<Wire.NotificationSeverity>(notification.Severity),
            notification.Title,
            notification.Message,
            notification.ProcessId?.Value,
            notification.RunId?.Value);
}
