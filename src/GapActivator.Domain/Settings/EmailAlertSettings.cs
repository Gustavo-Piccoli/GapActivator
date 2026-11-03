using System.Collections.Immutable;
using System.Net.Mail;
using GapActivator.Domain.Common;

namespace GapActivator.Domain.Settings;

/// <summary>
/// SMTP and alert configuration. Value equality: recipients compare in order, alerts as a set.
/// </summary>
public sealed record EmailAlertSettings
{
    public static EmailAlertSettings Disabled { get; } =
        new(false, string.Empty, 587, TlsMode.StartTls, null, null, string.Empty, [], []);

    private EmailAlertSettings(
        bool enabled,
        string host,
        int port,
        TlsMode tls,
        string? username,
        ProtectedSecret? password,
        string sender,
        ImmutableArray<string> recipients,
        ImmutableArray<AlertKind> alerts)
    {
        Enabled = enabled;
        Host = host;
        Port = port;
        Tls = tls;
        Username = username;
        Password = password;
        Sender = sender;
        Recipients = recipients;
        Alerts = alerts;
    }

    public bool Enabled { get; }

    public string Host { get; }

    public int Port { get; }

    public TlsMode Tls { get; }

    public string? Username { get; }

    public ProtectedSecret? Password { get; }

    public string Sender { get; }

    public IReadOnlyList<string> Recipients { get; }

    /// <summary>Distinct alert kinds, sorted so equality does not depend on input order.</summary>
    public IReadOnlyList<AlertKind> Alerts { get; }

    public static EmailAlertSettings Create(
        bool enabled,
        string host,
        int port,
        TlsMode tls,
        string? username,
        ProtectedSecret? password,
        string sender,
        IEnumerable<string> recipients,
        IEnumerable<AlertKind> alerts)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(recipients);
        ArgumentNullException.ThrowIfNull(alerts);

        var recipientList = recipients.ToImmutableArray();
        var alertList = alerts.Distinct().Order().ToImmutableArray();

        if (enabled)
        {
            Validate(host, port, username, password, sender, recipientList, alertList);
        }

        return new EmailAlertSettings(enabled, host, port, tls, username, password, sender, recipientList, alertList);
    }

    public bool Equals(EmailAlertSettings? other) =>
        other is not null
        && Enabled == other.Enabled
        && Host == other.Host
        && Port == other.Port
        && Tls == other.Tls
        && Username == other.Username
        && Equals(Password, other.Password)
        && Sender == other.Sender
        && Recipients.SequenceEqual(other.Recipients)
        && Alerts.SequenceEqual(other.Alerts);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Enabled);
        hash.Add(Host);
        hash.Add(Port);
        hash.Add(Tls);
        hash.Add(Username);
        hash.Add(Password);
        hash.Add(Sender);
        foreach (var recipient in Recipients)
        {
            hash.Add(recipient);
        }

        foreach (var alert in Alerts)
        {
            hash.Add(alert);
        }

        return hash.ToHashCode();
    }

    private static void Validate(
        string host,
        int port,
        string? username,
        ProtectedSecret? password,
        string sender,
        ImmutableArray<string> recipients,
        ImmutableArray<AlertKind> alerts)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new DomainException("SMTP host must not be empty.");
        }

        if (port is < 1 or > 65535)
        {
            throw new DomainException("SMTP port must be between 1 and 65535.");
        }

        if (!IsValidAddress(sender))
        {
            throw new DomainException("Sender is not a valid e-mail address.");
        }

        if (recipients.IsEmpty)
        {
            throw new DomainException("At least one recipient is required.");
        }

        if (recipients.Any(r => !IsValidAddress(r)))
        {
            throw new DomainException("A recipient is not a valid e-mail address.");
        }

        if (alerts.IsEmpty)
        {
            throw new DomainException("At least one alert kind must be selected.");
        }

        if (password is not null && string.IsNullOrWhiteSpace(username))
        {
            throw new DomainException("A password requires a username.");
        }
    }

    private static bool IsValidAddress(string? value) =>
        !string.IsNullOrWhiteSpace(value) && MailAddress.TryCreate(value, out _);
}
