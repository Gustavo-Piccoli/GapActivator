using GapActivator.Application.Events;
using GapActivator.Application.Ports;
using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;

namespace GapActivator.Application.Notifications;

/// <summary>Notifies the app of every alerting event, whatever the e-mail settings are.</summary>
public sealed class AppNotificationHandler(
    IProcessRepository processes,
    IAppNotifier notifier,
    TimeProvider time) : INotificationHandler
{
    public async Task HandleAsync(IDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        if (AlertRules.KindOf(domainEvent) is not { } kind)
        {
            return;
        }

        AlertContent content = await AlertFormatter.BuildAsync(kind, domainEvent, processes, time, ct);
        await notifier.NotifyAsync(
            new AppNotification(
                AlertFormatter.SeverityOf(kind),
                content.Title,
                string.Join("; ", content.Lines),
                content.ProcessId,
                content.RunId),
            ct);
    }
}
