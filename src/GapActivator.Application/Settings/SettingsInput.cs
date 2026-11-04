using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using GapActivator.Domain.Settings;

namespace GapActivator.Application.Settings;

public enum SecretChangeKind
{
    Keep,
    Clear,
    Set,
}

/// <summary>What to do with a stored secret. <paramref name="NewValue"/> is required for <see cref="SecretChangeKind.Set"/> and ignored otherwise.</summary>
public sealed record SecretChange(SecretChangeKind Kind, string? NewValue)
{
    /// <summary>Masks the new value so a logged or displayed command never exposes it.</summary>
    public override string ToString() =>
        $"SecretChange {{ Kind = {Kind}, NewValue = {(NewValue is null ? "null" : "***")} }}";
}

public sealed record EmailInput(
    bool Enabled,
    string Host,
    int Port,
    TlsMode Tls,
    string? Username,
    SecretChange Password,
    string Sender,
    IReadOnlyList<string> Recipients,
    IReadOnlyList<AlertKind> Alerts);

public sealed record SettingsInput(
    TimeSpan CycleInterval,
    int FailureLimit,
    TimeSpan? DefaultTimeLimit,
    TimeSpan DefaultGracePeriod,
    int LogRetentionDays,
    int HistoryRetentionDays,
    LogLevel LogLevel,
    bool CheckForUpdates,
    EmailInput Email);

/// <summary>Turns an <see cref="EmailInput"/> into domain settings; shared by UpdateSettings and SendTestEmail.</summary>
internal sealed class EmailSettingsMapper(ISecretProtector protector, ISettingsRepository settings)
{
    /// <param name="input">The e-mail input.</param>
    /// <param name="enabledOverride">When set, replaces <see cref="EmailInput.Enabled"/> (the test e-mail always validates as enabled).</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<EmailAlertSettings> MapAsync(EmailInput input, bool? enabledOverride, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Password);

        ProtectedSecret? password = input.Password.Kind switch
        {
            SecretChangeKind.Keep => (await settings.GetAsync(ct)).Email.Password,
            SecretChangeKind.Set when string.IsNullOrEmpty(input.Password.NewValue) =>
                throw new CommandRejectedException(RejectionCodes.SecretValueRequired, "The new password needs a value."),
            SecretChangeKind.Set => protector.Protect(input.Password.NewValue),
            _ => null,
        };

        return EmailAlertSettings.Create(
            enabledOverride ?? input.Enabled,
            input.Host,
            input.Port,
            input.Tls,
            input.Username,
            password,
            input.Sender,
            input.Recipients,
            input.Alerts);
    }
}
