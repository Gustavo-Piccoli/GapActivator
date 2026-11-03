using GapActivator.Domain.Settings;

namespace GapActivator.Application.Ports;

public sealed record EmailMessage(string Subject, string Body);

public sealed class EmailDeliveryException(string message, Exception? inner = null) : Exception(message, inner);

public interface IEmailSender
{
    Task SendAsync(EmailAlertSettings smtp, EmailMessage message, CancellationToken ct);
}
