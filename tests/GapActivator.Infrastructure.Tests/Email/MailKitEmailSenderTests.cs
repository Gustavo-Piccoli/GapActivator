using System.Diagnostics;
using GapActivator.Application.Ports;
using GapActivator.Domain.Settings;
using GapActivator.Infrastructure.Email;
using GapActivator.Infrastructure.Tests.Support;
using GapActivator.Testing;
using MailKit.Security;

namespace GapActivator.Infrastructure.Tests.Email;

[Trait("Category", "Integration")]
public sealed class MailKitEmailSenderTests
{
    private static readonly EmailMessage Message = new("Run failed", "The run failed.");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(TlsMode.None, SecureSocketOptions.None)]
    [InlineData(TlsMode.StartTls, SecureSocketOptions.StartTls)]
    [InlineData(TlsMode.Implicit, SecureSocketOptions.SslOnConnect)]
    public void SecureSocketOptionsFor_MapsEveryTlsMode(TlsMode mode, SecureSocketOptions expected) =>
        MailKitEmailSender.SecureSocketOptionsFor(mode).ShouldBe(expected);

    // BI-098
    [Fact]
    public async Task Send_PlainSmtp_DeliversSenderAllRecipientsSubjectAndBody()
    {
        await using var server = new MiniSmtpServer();
        MailKitEmailSender sender = NewSender();

        await sender.SendAsync(Settings(server.Port), Message, Ct);

        ReceivedMail mail = server.Received.Single();
        mail.From.ShouldBe("gapactivator@example.com");
        mail.Recipients.ShouldBe(["alerts@example.com", "ops@example.com"]);
        mail.Data.ShouldContain("Subject: Run failed");
        mail.Data.ShouldContain("The run failed.");
    }

    // BI-098
    [Fact]
    public async Task Send_WithCredentials_AuthenticatesWithUnprotectedPassword()
    {
        await using var server = new MiniSmtpServer { AdvertiseAuthPlain = true };
        var secrets = new FakeSecretProtector();
        MailKitEmailSender sender = NewSender(secrets);

        await sender.SendAsync(Settings(server.Port, "user", secrets.Protect("secret")), Message, Ct);

        server.AuthenticatedUser.ShouldBe("user");
        server.AuthenticatedPassword.ShouldBe("secret");
        server.Received.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Send_StartTlsRequiredButNotOffered_ThrowsEmailDeliveryException()
    {
        await using var server = new MiniSmtpServer();

        await Should.ThrowAsync<EmailDeliveryException>(() =>
            NewSender().SendAsync(Settings(server.Port, tls: TlsMode.StartTls), Message, Ct));

        server.Received.ShouldBeEmpty();
    }

    [Fact]
    public async Task Send_RecipientRejected_ThrowsEmailDeliveryException()
    {
        await using var server = new MiniSmtpServer { RejectRecipientWith = "550 no such user" };

        EmailDeliveryException error = await Should.ThrowAsync<EmailDeliveryException>(() =>
            NewSender().SendAsync(Settings(server.Port), Message, Ct));

        error.Message.ShouldStartWith("SMTP delivery failed: ");
        error.InnerException.ShouldNotBeNull();
    }

    [Fact]
    public async Task Send_NothingListening_ThrowsEmailDeliveryException()
    {
        int port;
        await using (var server = new MiniSmtpServer())
        {
            port = server.Port;
        }

        await Should.ThrowAsync<EmailDeliveryException>(() =>
            NewSender().SendAsync(Settings(port), Message, Ct));
    }

    // Addition b
    [Fact]
    public async Task Send_ServerNeverGreets_FailsWithinTheOperationTimeout()
    {
        await using var server = new MiniSmtpServer { NeverGreet = true };
        MailKitEmailSender sender = NewSender(options: new EmailSenderOptions { OperationTimeout = TimeSpan.FromSeconds(1) });
        var watch = Stopwatch.StartNew();

        await Should.ThrowAsync<EmailDeliveryException>(() => sender.SendAsync(Settings(server.Port), Message, Ct));

        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Send_TotalTimeoutElapses_ThrowsEmailDeliveryException()
    {
        await using var server = new MiniSmtpServer { NeverGreet = true };
        MailKitEmailSender sender = NewSender(options: new EmailSenderOptions { TotalTimeout = TimeSpan.FromMilliseconds(300) });
        var watch = Stopwatch.StartNew();

        await Should.ThrowAsync<EmailDeliveryException>(() => sender.SendAsync(Settings(server.Port), Message, Ct));

        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Send_CallerCancels_ThrowsOperationCanceled()
    {
        await using var server = new MiniSmtpServer { NeverGreet = true };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        Task send = NewSender().SendAsync(Settings(server.Port), Message, cts.Token);

        await Task.Delay(300, Ct);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
    }

    private static MailKitEmailSender NewSender(ISecretProtector? secrets = null, EmailSenderOptions? options = null) =>
        new(secrets ?? new FakeSecretProtector(), options ?? new EmailSenderOptions());

    private static EmailAlertSettings Settings(int port, string? username = null, ProtectedSecret? password = null, TlsMode tls = TlsMode.None) =>
        EmailAlertSettings.Create(
            true,
            "127.0.0.1",
            port,
            tls,
            username,
            password,
            "gapactivator@example.com",
            ["alerts@example.com", "ops@example.com"],
            [AlertKind.RunFailed]);
}
