using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;

namespace GapActivator.Application.Ports;

public enum NotificationSeverity
{
    Information,
    Warning,
    Error,
}

public sealed record AppNotification(
    NotificationSeverity Severity,
    string Title,
    string Message,
    ProcessId? ProcessId,
    RunId? RunId);

public interface IAppNotifier
{
    Task NotifyAsync(AppNotification notification, CancellationToken ct);
}
