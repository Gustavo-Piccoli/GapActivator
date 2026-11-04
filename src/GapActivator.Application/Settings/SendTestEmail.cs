using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using GapActivator.Domain.Settings;

namespace GapActivator.Application.Settings;

public sealed record SendTestEmail(EmailInput Email) : ICommand<Unit>, IUngatedCommand;

/// <summary>Sends one message with the given settings, validated as enabled. Nothing is stored.</summary>
internal sealed class SendTestEmailHandler(
    EmailSettingsMapper mapper,
    IEmailSender sender) : ICommandHandler<SendTestEmail, Unit>
{
    public async Task<Unit> HandleAsync(SendTestEmail command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        EmailAlertSettings smtp = await mapper.MapAsync(command.Email, true, ct);

        try
        {
            await sender.SendAsync(
                smtp, new EmailMessage("GapActivator test e-mail", "This is a test message from GapActivator."), ct);
        }
        catch (EmailDeliveryException ex)
        {
            throw new CommandRejectedException(RejectionCodes.EmailTestFailed, ex.Message);
        }

        return Unit.Value;
    }
}
