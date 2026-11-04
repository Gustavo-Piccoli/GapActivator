using GapActivator.Application.Ports;
using GapActivator.Ipc.Contracts;
using Microsoft.Extensions.Logging;
using AppSeverity = GapActivator.Application.Ports.NotificationSeverity;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using WireSeverity = GapActivator.Ipc.Contracts.NotificationSeverity;

namespace GapActivator.Service.Ipc;

/// <summary>
/// Writes each notification to the log at the matching level and pushes it to every connected pipe client. The text is
/// sanitized once, so the log line and the push are both free of control characters. It never waits for a client.
/// </summary>
internal sealed partial class PipeBroadcastNotifier(IpcConnectionRegistry registry, ILogger<PipeBroadcastNotifier> logger) : IAppNotifier
{
    public Task NotifyAsync(AppNotification notification, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notification);

        string title = NotificationText.Sanitize(notification.Title);
        string message = NotificationText.Sanitize(notification.Message);

        LogLevel level = notification.Severity switch
        {
            AppSeverity.Warning => LogLevel.Warning,
            AppSeverity.Error => LogLevel.Error,
            _ => LogLevel.Information,
        };

        KeyValuePair<string, object?>[] state =
        [
            new("Title", title),
            new("Message", message),
            new("ProcessId", notification.ProcessId),
            new("RunId", notification.RunId),
        ];

        logger.Log(level, default, state, null, (_, _) => $"{title}: {message}");

        WireSeverity severity = notification.Severity switch
        {
            AppSeverity.Warning => WireSeverity.Warning,
            AppSeverity.Error => WireSeverity.Error,
            _ => WireSeverity.Information,
        };
        registry.BroadcastNotification(
            new NotificationDto(severity, title, message, notification.ProcessId?.Value, notification.RunId?.Value));
        return Task.CompletedTask;
    }
}
