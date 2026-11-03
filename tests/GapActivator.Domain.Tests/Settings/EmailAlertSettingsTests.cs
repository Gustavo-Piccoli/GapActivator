using GapActivator.Domain.Common;
using GapActivator.Domain.Settings;

namespace GapActivator.Domain.Tests.Settings;

public class EmailAlertSettingsTests
{
    private static EmailAlertSettings Valid(
        bool enabled = true,
        string host = "smtp.example.com",
        int port = 587,
        string? username = "user",
        ProtectedSecret? password = null,
        string sender = "activator@example.com",
        IEnumerable<string>? recipients = null,
        IEnumerable<AlertKind>? alerts = null) =>
        EmailAlertSettings.Create(
            enabled, host, port, TlsMode.StartTls, username, password, sender,
            recipients ?? ["ops@example.com"],
            alerts ?? [AlertKind.RunFailed]);

    [Fact]
    public void Disabled_Always_IsDisabledWithNoRecipientsOrAlerts()
    {
        var disabled = EmailAlertSettings.Disabled;

        disabled.Enabled.ShouldBeFalse();
        disabled.Recipients.ShouldBeEmpty();
        disabled.Alerts.ShouldBeEmpty();
    }

    [Fact]
    public void Create_EnabledWithValidValues_KeepsValues()
    {
        var secret = new ProtectedSecret("cipher");

        var email = Valid(password: secret, recipients: ["a@example.com", "b@example.com"],
            alerts: [AlertKind.RunFailed, AlertKind.CycleFailed]);

        email.Enabled.ShouldBeTrue();
        email.Host.ShouldBe("smtp.example.com");
        email.Port.ShouldBe(587);
        email.Tls.ShouldBe(TlsMode.StartTls);
        email.Username.ShouldBe("user");
        email.Password.ShouldBe(secret);
        email.Sender.ShouldBe("activator@example.com");
        email.Recipients.ShouldBe(["a@example.com", "b@example.com"]);
        email.Alerts.Count.ShouldBe(2);
    }

    [Fact]
    public void Create_EnabledWithZeroRecipients_Throws() =>
        Should.Throw<DomainException>(() => Valid(recipients: []));

    [Fact]
    public void Create_EnabledWithInvalidSender_Throws() =>
        Should.Throw<DomainException>(() => Valid(sender: "not-an-email"));

    [Fact]
    public void Create_EnabledWithInvalidRecipient_Throws() =>
        Should.Throw<DomainException>(() => Valid(recipients: ["ops@example.com", "not-an-email"]));

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Create_EnabledWithPortOutOfRange_Throws(int port) =>
        Should.Throw<DomainException>(() => Valid(port: port));

    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    public void Create_EnabledWithPortOnBoundary_Succeeds(int port) =>
        Valid(port: port).Port.ShouldBe(port);

    [Fact]
    public void Create_EnabledWithEmptyHost_Throws() =>
        Should.Throw<DomainException>(() => Valid(host: " "));

    [Fact]
    public void Create_EnabledWithNoAlerts_Throws() =>
        Should.Throw<DomainException>(() => Valid(alerts: []));

    [Fact]
    public void Create_EnabledWithPasswordButNoUsername_Throws() =>
        Should.Throw<DomainException>(() => Valid(username: null, password: new ProtectedSecret("cipher")));

    [Fact]
    public void Create_DisabledWithEmptyHost_Succeeds()
    {
        var email = EmailAlertSettings.Create(
            false, "", 0, TlsMode.None, null, null, "", [], []);

        email.Enabled.ShouldBeFalse();
        email.Host.ShouldBe("");
    }

    [Fact]
    public void Create_DisabledWithConfiguration_KeepsConfiguration()
    {
        var email = Valid(enabled: false, recipients: ["ops@example.com"]);

        email.Enabled.ShouldBeFalse();
        email.Recipients.ShouldBe(["ops@example.com"]);
    }

    [Fact]
    public void Create_SourceCollectionsMutatedAfterwards_DoNotAffectSettings()
    {
        var recipients = new List<string> { "ops@example.com" };
        var email = Valid(recipients: recipients);

        recipients.Add("other@example.com");

        email.Recipients.Count.ShouldBe(1);
    }

    [Fact]
    public void Recipients_CastToMutableCollection_IsNotPossible()
    {
        var email = Valid();

        (email.Recipients is string[]).ShouldBeFalse();
        (email.Recipients is List<string>).ShouldBeFalse();
        Should.Throw<NotSupportedException>(() => ((IList<string>)email.Recipients).Add("x@example.com"));
        Should.Throw<NotSupportedException>(() => ((ICollection<AlertKind>)email.Alerts).Add(AlertKind.CycleFailed));
    }

    [Fact]
    public void Equals_SameValuesDifferentCollectionInstances_AreEqual()
    {
        var a = Valid(recipients: ["a@example.com", "b@example.com"], alerts: [AlertKind.RunFailed, AlertKind.CycleFailed]);
        var b = Valid(recipients: ["a@example.com", "b@example.com"], alerts: [AlertKind.CycleFailed, AlertKind.RunFailed]);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Create_UndefinedTlsMode_Throws(bool enabled) =>
        Should.Throw<DomainException>(() => EmailAlertSettings.Create(
            enabled, "smtp.example.com", 587, (TlsMode)99, "user", null, "activator@example.com",
            ["ops@example.com"], [AlertKind.RunFailed]));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Create_UndefinedAlertKind_Throws(bool enabled) =>
        Should.Throw<DomainException>(() => EmailAlertSettings.Create(
            enabled, "smtp.example.com", 587, TlsMode.StartTls, "user", null, "activator@example.com",
            ["ops@example.com"], [AlertKind.RunFailed, (AlertKind)99]));

    [Fact]
    public void Create_DuplicateAlerts_CollapseToOne()
    {
        var email = Valid(alerts: [AlertKind.RunFailed, AlertKind.RunFailed, AlertKind.CycleFailed, AlertKind.RunFailed]);

        email.Alerts.Count.ShouldBe(2);
        email.Alerts.ShouldBe([AlertKind.RunFailed, AlertKind.CycleFailed]);
    }

    [Fact]
    public void Equals_DifferentRecipients_AreNotEqual()
    {
        var a = Valid(recipients: ["a@example.com"]);
        var b = Valid(recipients: ["b@example.com"]);

        a.ShouldNotBe(b);
    }

    [Fact]
    public void Equals_DifferentAlerts_AreNotEqual() =>
        Valid(alerts: [AlertKind.RunFailed]).ShouldNotBe(Valid(alerts: [AlertKind.CycleFailed]));
}
