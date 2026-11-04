using GapActivator.Application.Ports;
using Microsoft.Extensions.Logging;

namespace GapActivator.Infrastructure.Notifications;

/// <summary>Writes each notification to the log at the matching level. Replaced by the desktop notifier in a later plan.</summary>
internal sealed class LoggingAppNotifier(ILogger<LoggingAppNotifier> logger) : IAppNotifier
{
    public Task NotifyAsync(AppNotification notification, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notification);

        LogLevel level = notification.Severity switch
        {
            NotificationSeverity.Warning => LogLevel.Warning,
            NotificationSeverity.Error => LogLevel.Error,
            _ => LogLevel.Information,
        };

        KeyValuePair<string, object?>[] state =
        [
            new("Title", notification.Title),
            new("Message", notification.Message),
            new("ProcessId", notification.ProcessId),
            new("RunId", notification.RunId),
        ];

        logger.Log(level, default, state, null, (_, _) => $"{notification.Title}: {notification.Message}");
        return Task.CompletedTask;
    }
}
