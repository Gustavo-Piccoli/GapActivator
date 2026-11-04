using GapActivator.Application.Ports;
using GapActivator.Domain.Settings;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace GapActivator.Infrastructure.Email;

/// <summary>Sends alert e-mails over SMTP with MailKit. Every connect, send and receive is time-bounded.</summary>
internal sealed class MailKitEmailSender(ISecretProtector secrets, EmailSenderOptions options) : IEmailSender
{
    internal static SecureSocketOptions SecureSocketOptionsFor(TlsMode mode) => mode switch
    {
        TlsMode.None => SecureSocketOptions.None,
        TlsMode.StartTls => SecureSocketOptions.StartTls,
        TlsMode.Implicit => SecureSocketOptions.SslOnConnect,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown TLS mode."),
    };

    public async Task SendAsync(EmailAlertSettings smtp, EmailMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(smtp);
        ArgumentNullException.ThrowIfNull(message);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(options.TotalTimeout);

        try
        {
            using MimeMessage mime = BuildMessage(smtp, message);
            using var client = new SmtpClient { Timeout = ToMilliseconds(options.OperationTimeout) };

            await client.ConnectAsync(smtp.Host, smtp.Port, SecureSocketOptionsFor(smtp.Tls), linked.Token);

            if (!string.IsNullOrEmpty(smtp.Username))
            {
                string password = smtp.Password is null ? string.Empty : secrets.Unprotect(smtp.Password);
                await client.AuthenticateAsync(smtp.Username, password, linked.Token);
            }

            await client.SendAsync(mime, linked.Token);
            await client.DisconnectAsync(true, linked.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new EmailDeliveryException($"SMTP delivery failed: {ex.Message}", ex);
        }
    }

    private static MimeMessage BuildMessage(EmailAlertSettings smtp, EmailMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(smtp.Sender));
        foreach (string recipient in smtp.Recipients)
        {
            mime.To.Add(MailboxAddress.Parse(recipient));
        }

        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.Body };
        return mime;
    }

    private static int ToMilliseconds(TimeSpan value) =>
        (int)Math.Clamp(value.TotalMilliseconds, 1, int.MaxValue);
}
