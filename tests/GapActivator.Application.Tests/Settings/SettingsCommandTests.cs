using GapActivator.Application.Events;
using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using GapActivator.Application.Settings;
using GapActivator.Application.Tests.Cycle;
using GapActivator.Application.Tests.Messaging;
using GapActivator.Application.Tests.Runs;
using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Scheduling;
using GapActivator.Domain.Settings;
using GapActivator.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GapActivator.Application.Tests.Settings;

public class SettingsCommandTests
{
    private const string Exe = RunTestHost.Executable;
    private const string Plain = "hunter2-plain";

    private static readonly SecretChange Keep = new(SecretChangeKind.Keep, null);

    [Fact]
    public async Task UpdateSettings_Valid_Saved()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        SettingsInput input = Settings() with
        {
            CycleInterval = TimeSpan.FromSeconds(30),
            FailureLimit = 7,
            DefaultTimeLimit = TimeSpan.FromMinutes(5),
            DefaultGracePeriod = TimeSpan.FromSeconds(20),
            LogRetentionDays = 11,
            HistoryRetentionDays = 22,
            LogLevel = LogLevel.Warning,
            CheckForUpdates = false,
            Email = Email(true, Keep, username: null),
        };

        await host.Run<UpdateSettings, Unit>(new UpdateSettings(input));

        InstallationSettings saved = host.Store.Settings;
        saved.CycleInterval.ShouldBe(TimeSpan.FromSeconds(30));
        saved.FailureLimit.ShouldBe(7);
        saved.DefaultTimeLimit.ShouldBe(TimeSpan.FromMinutes(5));
        saved.DefaultGracePeriod.ShouldBe(TimeSpan.FromSeconds(20));
        saved.LogRetentionDays.ShouldBe(11);
        saved.HistoryRetentionDays.ShouldBe(22);
        saved.LogLevel.ShouldBe(LogLevel.Warning);
        saved.CheckForUpdates.ShouldBeFalse();
        saved.Email.Enabled.ShouldBeTrue();
        saved.Email.Host.ShouldBe("smtp.example.com");
        saved.Email.Recipients.ShouldBe(["ops@example.com"]);
        saved.Email.Alerts.ShouldBe([AlertKind.RunFailed]);
        saved.Email.Password.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateSettings_DoesNotChangeIsPaused()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        host.Store.Settings.Pause(host.Time.GetUtcNow());

        await host.Run<UpdateSettings, Unit>(new UpdateSettings(Settings()));

        host.Store.Settings.IsPaused.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateSettings_PasswordKeep_KeepsCiphertext()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        StoreEmailWithPassword(host, "enc:stored");

        await host.Run<UpdateSettings, Unit>(new UpdateSettings(Settings() with { Email = Email(true, Keep) }));

        host.Store.Settings.Email.Password.ShouldBe(new ProtectedSecret("enc:stored"));
    }

    [Fact]
    public async Task UpdateSettings_PasswordKeepNothingStored_NoPassword()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();

        await host.Run<UpdateSettings, Unit>(new UpdateSettings(Settings() with { Email = Email(true, Keep, username: null) }));

        host.Store.Settings.Email.Password.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateSettings_PasswordSet_Protects()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        StoreEmailWithPassword(host, "enc:stored");

        await host.Run<UpdateSettings, Unit>(new UpdateSettings(
            Settings() with { Email = Email(true, new SecretChange(SecretChangeKind.Set, Plain)) }));

        host.Store.Settings.Email.Password.ShouldBe(new ProtectedSecret("enc:" + Plain));
    }

    [Fact]
    public async Task UpdateSettings_PasswordClear_Removes()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        StoreEmailWithPassword(host, "enc:stored");

        await host.Run<UpdateSettings, Unit>(new UpdateSettings(
            Settings() with { Email = Email(true, new SecretChange(SecretChangeKind.Clear, null)) }));

        host.Store.Settings.Email.Password.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task UpdateSettings_PasswordSetWithoutValue_IsRejected(string? value)
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        StoreEmailWithPassword(host, "enc:stored");

        var error = await Should.ThrowAsync<CommandRejectedException>(() => host.Run<UpdateSettings, Unit>(
            new UpdateSettings(Settings() with { Email = Email(true, new SecretChange(SecretChangeKind.Set, value)) })));

        error.Code.ShouldBe(RejectionCodes.SecretValueRequired);
        host.Store.Settings.Email.Password.ShouldBe(new ProtectedSecret("enc:stored"));
    }

    [Fact]
    public async Task UpdateSettings_ValueOnKeepOrClear_IsIgnored()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        StoreEmailWithPassword(host, "enc:stored");

        await host.Run<UpdateSettings, Unit>(new UpdateSettings(
            Settings() with { Email = Email(true, new SecretChange(SecretChangeKind.Keep, "ignored")) }));
        host.Store.Settings.Email.Password.ShouldBe(new ProtectedSecret("enc:stored"));

        await host.Run<UpdateSettings, Unit>(new UpdateSettings(
            Settings() with { Email = Email(true, new SecretChange(SecretChangeKind.Clear, "ignored")) }));
        host.Store.Settings.Email.Password.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateSettings_Invalid_ThrowsDomainException()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        TimeSpan before = host.Store.Settings.CycleInterval;

        await Should.ThrowAsync<DomainException>(() => host.Run<UpdateSettings, Unit>(
            new UpdateSettings(Settings() with { CycleInterval = TimeSpan.FromSeconds(1) })));
        await Should.ThrowAsync<DomainException>(() => host.Run<UpdateSettings, Unit>(
            new UpdateSettings(Settings() with { Email = Email(true, Keep) with { Recipients = [] } })));

        host.Store.Settings.CycleInterval.ShouldBe(before);
    }

    [Fact]
    public void ToString_NeverPrintsTheSecret()
    {
        var change = new SecretChange(SecretChangeKind.Set, Plain);
        EmailInput email = Email(true, change);
        SettingsInput settings = Settings() with { Email = email };

        change.ToString().ShouldNotContain(Plain);
        change.ToString().ShouldContain("***");
        email.ToString().ShouldNotContain(Plain);
        settings.ToString().ShouldNotContain(Plain);
        new UpdateSettings(settings).ToString().ShouldNotContain(Plain);
        new SendTestEmail(email).ToString().ShouldNotContain(Plain);
    }

    // Q-1
    [Fact]
    public async Task Pause_ThenCycle_StartsNothing()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        host.AddScheduledProcess(host.Time.GetUtcNow() - TimeSpan.FromSeconds(10));

        await host.Run<PauseInstallation, Unit>(new PauseInstallation());
        await host.Cycle().RunOnceAsync(TestContext.Current.CancellationToken);

        host.Store.Settings.IsPaused.ShouldBeTrue();
        host.Store.Runs.ShouldBeEmpty();
        host.Store.History.ShouldContain(h => h.Kind == HistoryEntryKind.InstallationPaused);
    }

    [Fact]
    public async Task Pause_AlreadyPaused_ThrowsDomainException()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        await host.Run<PauseInstallation, Unit>(new PauseInstallation());

        await Should.ThrowAsync<DomainException>(() => host.Run<PauseInstallation, Unit>(new PauseInstallation()));
    }

    [Fact]
    public async Task Resume_NotPaused_ThrowsDomainException()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();

        await Should.ThrowAsync<DomainException>(() => host.Run<ResumeInstallation, Unit>(new ResumeInstallation()));
    }

    // Q-1
    [Fact]
    public async Task Pause_ActiveRun_FinishesNormally()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process process = host.AddProcess();
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        Run run = await host.StartRunAsync(process);

        await host.Run<PauseInstallation, Unit>(new PauseInstallation());

        host.Store.Runs[run.Id].State.ShouldNotBe(RunState.Completed);
        child.Exit(0);
        await host.EndOf(run.Id);
        host.Store.Runs[run.Id].Outcome.ShouldBe(RunOutcome.Succeeded);
    }

    // Q-14
    [Fact]
    public async Task Resume_MissedOccurrencesDiscarded_NoCatchUpRun()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        DateTimeOffset now = host.Time.GetUtcNow();
        Process process = host.AddScheduledProcess(now - TimeSpan.FromMinutes(10));
        await host.Run<PauseInstallation, Unit>(new PauseInstallation());

        await host.Run<ResumeInstallation, Unit>(new ResumeInstallation());
        await host.Cycle().RunOnceAsync(TestContext.Current.CancellationToken);

        host.Store.Settings.IsPaused.ShouldBeFalse();
        process.Schedules.OfType<OneTimeSchedule>().Single().FulfilledAt.ShouldBe(now);
        host.Store.Runs.ShouldBeEmpty();
        host.Store.History.ShouldContain(h => h.Kind == HistoryEntryKind.InstallationResumed);
    }

    // Q-14
    [Fact]
    public async Task Resume_RetryPending_StillRetriedNextCycle()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        Process process = host.AddProcess();
        process.ApplyRunOutcome(RunOutcome.Failed, 5, host.Time.GetUtcNow());
        FakeRunningProcess child = host.Launcher.QueueSucceed(Exe);
        await host.Run<PauseInstallation, Unit>(new PauseInstallation());

        await host.Run<ResumeInstallation, Unit>(new ResumeInstallation());
        process.RetryPending.ShouldBeTrue();
        await host.Cycle().RunOnceAsync(TestContext.Current.CancellationToken);

        Run run = host.ActiveRun(process);
        run.Trigger.ShouldBe(RunTrigger.Retry);
        child.Exit(1);
        await host.EndOf(run.Id);
    }

    [Fact]
    public async Task SendTestEmail_UsesStoredPasswordOnKeep()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        StoreEmailWithPassword(host, "enc:stored");
        EmailAlertSettings before = host.Store.Settings.Email;

        await host.Run<SendTestEmail, Unit>(new SendTestEmail(Email(false, Keep)));

        (EmailAlertSettings smtp, EmailMessage message) = host.Email.Sent.ShouldHaveSingleItem();
        smtp.Enabled.ShouldBeTrue();
        smtp.Password.ShouldBe(new ProtectedSecret("enc:stored"));
        message.Subject.ShouldBe("GapActivator test e-mail");
        message.Body.ShouldNotContain("stored");
        host.Store.Settings.Email.ShouldBe(before);
    }

    [Fact]
    public async Task SendTestEmail_PasswordSet_ProtectsButStoresNothing()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        StoreEmailWithPassword(host, "enc:stored");
        EmailAlertSettings before = host.Store.Settings.Email;

        await host.Run<SendTestEmail, Unit>(
            new SendTestEmail(Email(true, new SecretChange(SecretChangeKind.Set, Plain))));

        host.Email.Sent.ShouldHaveSingleItem().Smtp.Password.ShouldBe(new ProtectedSecret("enc:" + Plain));
        host.Email.Sent[0].Message.Body.ShouldNotContain(Plain);
        host.Store.Settings.Email.ShouldBe(before);
    }

    [Fact]
    public async Task SendTestEmail_PasswordSetWithoutValue_IsRejected()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();

        var error = await Should.ThrowAsync<CommandRejectedException>(() => host.Run<SendTestEmail, Unit>(
            new SendTestEmail(Email(true, new SecretChange(SecretChangeKind.Set, "")))));

        error.Code.ShouldBe(RejectionCodes.SecretValueRequired);
        host.Email.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task SendTestEmail_InvalidSettings_ThrowsDomainException()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();

        await Should.ThrowAsync<DomainException>(() => host.Run<SendTestEmail, Unit>(
            new SendTestEmail(Email(false, Keep) with { Recipients = [] })));

        host.Email.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task SendTestEmail_DeliveryFails_IsRejectedEmailTestFailed()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        host.Email.Failure = new EmailDeliveryException("connection refused");

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Run<SendTestEmail, Unit>(new SendTestEmail(Email(true, Keep))));

        error.Code.ShouldBe(RejectionCodes.EmailTestFailed);
        error.Message.ShouldContain("connection refused");
    }

    [Fact]
    public async Task SendTestEmail_OtherException_Propagates()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        host.Email.Failure = new InvalidOperationException("unexpected");

        var error = await Should.ThrowAsync<InvalidOperationException>(
            () => host.Run<SendTestEmail, Unit>(new SendTestEmail(Email(true, Keep))));

        error.Message.ShouldBe("unexpected");
    }

    [Fact]
    public async Task SendTestEmail_DoesNotWaitForTheWriteGate()
    {
        var state = new ProbeState();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        state.OnHandle = async _ =>
        {
            entered.SetResult();
            await release.Task;
        };
        await using ApplicationTestHost host = ProbeHost.Create(state);

        Task<int> holder = host.Run<ProbeCommand, int>(new ProbeCommand());
        await entered.Task.Within();

        await host.Run<SendTestEmail, Unit>(new SendTestEmail(Email(true, Keep))).Within();

        holder.IsCompleted.ShouldBeFalse();
        host.Email.Sent.Count.ShouldBe(1);
        release.SetResult();
        await holder.Within();
    }

    // BI-094
    [Fact]
    public async Task Restart_RecordsEventThenCallsServiceControl()
    {
        await using ApplicationTestHost host = CycleTestHost.Create(services =>
        {
            services.AddSingleton<HistoryProbingServiceControl>();
            services.AddSingleton<IServiceControl>(sp => sp.GetRequiredService<HistoryProbingServiceControl>());
        });

        await host.Run<RestartGapActivator, Unit>(new RestartGapActivator());

        var probe = host.Services.GetRequiredService<HistoryProbingServiceControl>();
        probe.Calls.ShouldBe(1);
        probe.RestartedEntriesAtCall.ShouldBe(1);
        host.Notifications().Events.OfType<GapActivatorRestarted>().ShouldHaveSingleItem();
    }

    private static SettingsInput Settings() => new(
        TimeSpan.FromSeconds(60), 3, null, TimeSpan.FromSeconds(30), 30, 90, LogLevel.Information, true,
        Email(false, Keep));

    private static EmailInput Email(bool enabled, SecretChange password, string? username = "mailer") => new(
        enabled, "smtp.example.com", 587, TlsMode.StartTls, username, password, "alerts@example.com",
        ["ops@example.com"], [AlertKind.RunFailed]);

    private static void StoreEmailWithPassword(ApplicationTestHost host, string ciphertext) =>
        host.Store.Settings.ChangeEmail(EmailAlertSettings.Create(
            true, "smtp.example.com", 587, TlsMode.StartTls, "mailer", new ProtectedSecret(ciphertext),
            "alerts@example.com", ["ops@example.com"], [AlertKind.RunFailed]));

    private sealed class HistoryProbingServiceControl(InMemoryStore store) : IServiceControl
    {
        public int Calls { get; private set; }

        public int RestartedEntriesAtCall { get; private set; }

        public Task RestartAsync(CancellationToken ct)
        {
            Calls++;
            lock (store.Sync)
            {
                RestartedEntriesAtCall = store.History.Count(h => h.Kind == HistoryEntryKind.GapActivatorRestarted);
            }

            return Task.CompletedTask;
        }
    }
}
