using System.Globalization;
using GapActivator.Application.Events;
using GapActivator.Application.Ports;
using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Settings;

namespace GapActivator.Application.Notifications;

/// <summary>The title, detail lines and time of one alert. <see cref="Lines"/> excludes the time.</summary>
internal sealed record AlertContent(
    AlertKind Kind,
    string Title,
    IReadOnlyList<string> Lines,
    string TimeLine,
    ProcessId? ProcessId,
    RunId? RunId);

/// <summary>Builds the alert text once, for both the e-mail and the app notification.</summary>
internal static class AlertFormatter
{
    private const string DeletedProcess = "(deleted process)";

    public static async Task<AlertContent> BuildAsync(
        AlertKind kind,
        IDomainEvent e,
        IProcessRepository processes,
        TimeProvider time,
        CancellationToken ct)
    {
        (ProcessId? processId, RunId? runId) = IdsOf(e);
        string? name = null;
        if (processId is { } id)
        {
            Process? process = await processes.GetByIdAsync(id, ct);
            name = process?.Name.Value ?? DeletedProcess;
        }

        List<string> lines = [];
        if (name is not null)
        {
            lines.Add($"Process: {name}");
        }

        if (runId is { } run)
        {
            lines.Add($"Run: {run}");
        }

        if (e is RunCompleted completed)
        {
            lines.Add($"Outcome: {completed.Outcome}");
        }

        if (ExitCodeOf(e) is { } exitCode)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Exit code: {exitCode}"));
        }

        if (e is ProcessBlocked blocked)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"Failures reached: {blocked.FailuresReached}"));
        }

        if (e is CycleFailed cycle)
        {
            lines.Add($"Error: {cycle.Error}");
        }

        DateTimeOffset local = TimeZoneInfo.ConvertTime(e.OccurredAt, time.LocalTimeZone);
        string timeLine = "Time: " + local.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);

        return new AlertContent(kind, TitleOf(kind, name ?? DeletedProcess), lines, timeLine, processId, runId);
    }

    public static NotificationSeverity SeverityOf(AlertKind kind) => kind switch
    {
        AlertKind.ProcessBlocked or AlertKind.RunFailed or AlertKind.CycleFailed => NotificationSeverity.Error,
        AlertKind.RunTimedOut or AlertKind.ServiceProcessCrashed => NotificationSeverity.Warning,
        _ => NotificationSeverity.Information,
    };

    private static string TitleOf(AlertKind kind, string name) => kind switch
    {
        AlertKind.ProcessBlocked => $"Process blocked: {name}",
        AlertKind.RunFailed => $"Run failed: {name}",
        AlertKind.RunTimedOut => $"Run timed out: {name}",
        AlertKind.ServiceProcessCrashed => $"Service crashed: {name}",
        AlertKind.CycleFailed => "Scheduler cycle failed",
        AlertKind.ServiceRestarted => "GapActivator restarted",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown alert kind."),
    };

    private static (ProcessId? ProcessId, RunId? RunId) IdsOf(IDomainEvent e) => e switch
    {
        ProcessBlocked x => (x.ProcessId, null),
        RunCompleted x => (x.ProcessId, x.RunId),
        ServiceProcessCrashed x => (x.ProcessId, x.RunId),
        _ => (null, null),
    };

    private static int? ExitCodeOf(IDomainEvent e) => e switch
    {
        RunCompleted x => x.ExitCode,
        ServiceProcessCrashed x => x.ExitCode,
        _ => null,
    };
}
