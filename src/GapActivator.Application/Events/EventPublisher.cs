using GapActivator.Domain.Common;
using Microsoft.Extensions.Logging;

namespace GapActivator.Application.Events;

/// <summary>
/// Logs each event and hands it to every <see cref="INotificationHandler"/>. A failing handler is logged and does
/// not stop the others or fail the command (BI-096). Cancellation of the caller's token propagates.
/// </summary>
public sealed partial class EventPublisher(IEnumerable<INotificationHandler> handlers, ILogger<EventPublisher> logger)
{
    public async Task PublishAsync(IReadOnlyList<IDomainEvent> events, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(events);
        INotificationHandler[] all = [.. handlers];

        foreach (IDomainEvent e in events)
        {
            LogEvent(e.GetType().Name, e.OccurredAt);
            foreach (INotificationHandler handler in all)
            {
                try
                {
                    await handler.HandleAsync(e, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogHandlerFailed(ex, handler.GetType().Name, e.GetType().Name);
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Event {EventName} occurred at {OccurredAt}.")]
    private partial void LogEvent(string eventName, DateTimeOffset occurredAt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification handler {Handler} failed for {EventName}.")]
    private partial void LogHandlerFailed(Exception exception, string handler, string eventName);
}
