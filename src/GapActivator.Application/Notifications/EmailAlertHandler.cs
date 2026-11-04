using GapActivator.Application.Events;
using GapActivator.Application.Ports;
using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Settings;

namespace GapActivator.Application.Notifications;

/// <summary>
/// Sends one e-mail for an event whose alert kind is enabled in the settings. The settings go to the sender as
/// stored, since the sender unprotects the password. A failure is thrown for the publisher to log (BI-096).
/// </summary>
public sealed class EmailAlertHandler(
    ISettingsRepository settings,
    IProcessRepository processes,
    IEmailSender sender,
    TimeProvider time) : INotificationHandler
{
    public async Task HandleAsync(IDomainEvent domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        if (AlertRules.KindOf(domainEvent) is not { } kind)
        {
            return;
        }

        EmailAlertSettings email = (await settings.GetAsync(ct)).Email;
        if (!email.Enabled || !email.Alerts.Contains(kind))
        {
            return;
        }

        AlertContent content = await AlertFormatter.BuildAsync(kind, domainEvent, processes, time, ct);
        string subject = $"[GapActivator] {Environment.MachineName}: {content.Title}";
        string body = string.Join('\n', [.. content.Lines, content.TimeLine]);
        await sender.SendAsync(email, new EmailMessage(subject, body), ct);
    }
}
