using GapActivator.Application.Cycle;
using GapActivator.Application.Events;
using GapActivator.Application.Messaging;
using GapActivator.Application.Notifications;
using GapActivator.Application.Ports;
using GapActivator.Application.Runs;
using GapActivator.Application.Settings;
using GapActivator.Application.Tests.Cycle;
using GapActivator.Application.Tests.Messaging;
using GapActivator.Application.Tests.Runs;
using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Settings;
using GapActivator.Testing;
using Microsoft.Extensions.DependencyInjection;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace GapActivator.Application.Tests.Notifications;

public class NotificationTests
{
    private static readonly DateTimeOffset At = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProcessId AProcess = new(Guid.NewGuid());
    private static readonly RunId ARun = new(Guid.NewGuid());

    public static TheoryData<RunOutcome, AlertKind?> CompletedOutcomes() => new()
    {
        { RunOutcome.Succeeded, null },
        { RunOutcome.Failed, AlertKind.RunFailed },
        { RunOutcome.FailedToStart, AlertKind.RunFailed },
        { RunOutcome.TimedOut, AlertKind.RunTimedOut },
        { RunOutcome.Cancelled, null },
        { RunOutcome.Interrupted, null },
        { RunOutcome.Abandoned, AlertKind.RunFailed },
        { RunOutcome.Skipped, null },
        { RunOutcome.Crashed, null },
        { RunOutcome.Stopped, null },
    };

    [Theory]
    [MemberData(nameof(CompletedOutcomes))]
    public void KindOf_RunCompleted_MapsEveryOutcome(RunOutcome outcome, AlertKind? expected) =>
        AlertRules.KindOf(new RunCompleted(ARun, AProcess, outcome, 1, false, At)).ShouldBe(expected);

    [Fact]
    public void KindOf_RunCompleted_CoversEveryOutcomeValue()
    {
        var mapped = CompletedOutcomes().Select(row => row.Data.Item1).ToHashSet();

        foreach (RunOutcome outcome in Enum.GetValues<RunOutcome>())
        {
            mapped.ShouldContain(outcome, $"{outcome} has no explicit mapping row.");
        }
    }

    [Fact]
    public void KindOf_OtherEvents_MapsEveryRow()
    {
        AlertRules.KindOf(new ProcessBlocked(AProcess, 3, At)).ShouldBe(AlertKind.ProcessBlocked);
        AlertRules.KindOf(new ServiceProcessCrashed(ARun, AProcess, 9, At)).ShouldBe(AlertKind.ServiceProcessCrashed);
        AlertRules.KindOf(new CycleFailed("boom", At)).ShouldBe(AlertKind.CycleFailed);
        AlertRules.KindOf(new GapActivatorRestarted(At)).ShouldBe(AlertKind.ServiceRestarted);
        AlertRules.KindOf(new RunStarted(ARun, AProcess, 10, At)).ShouldBeNull();
    }

    // BI-095
    [Fact]
    public async Task Blocked_EmailEnabledForBlocked_SendsOneEmailNamingProcess()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        ConfigureEmail(host, true, AlertKind.ProcessBlocked);
        Process process = host.AddProcess();
        Run run = host.AddRunningRun(process);
        ChangeFailureLimit(host, 1);

        await host.Run<RecordRunExited, Unit>(new RecordRunExited(run.Id, 1, false));

        (EmailAlertSettings smtp, EmailMessage message) = host.Email.Sent.ShouldHaveSingleItem();
        smtp.ShouldBe(host.Store.Settings.Email);
        message.Subject.ShouldBe($"[GapActivator] {Environment.MachineName}: Process blocked: {process.Name.Value}");
        message.Body.ShouldContain($"Process: {process.Name.Value}");
        message.Body.ShouldContain("Failures reached: 1");
        message.Body.ShouldContain("Time: 2030-01-01 12:00:00 +00:00");
    }

    [Fact]
    public async Task Failure_EmailEnabledForFailed_BodyListsTheLabelledLinesInOrder()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        host.Time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(-3), "test", "test"));
        ConfigureEmail(host, true, AlertKind.RunFailed);
        Process process = host.AddProcess();
        Run run = host.AddRunningRun(process);

        await host.Run<RecordRunExited, Unit>(new RecordRunExited(run.Id, 7, false));

        EmailMessage message = host.Email.Sent.ShouldHaveSingleItem().Message;
        message.Subject.ShouldBe($"[GapActivator] {Environment.MachineName}: Run failed: {process.Name.Value}");
        message.Body.Split('\n').ShouldBe(
        [
            $"Process: {process.Name.Value}",
            $"Run: {run.Id}",
            "Outcome: Failed",
            "Exit code: 7",
            "Time: 2030-01-01 09:00:00 -03:00",
        ]);
        AppNotification note = host.AppNotifier.Notifications.ShouldHaveSingleItem();
        note.Message.ShouldBe($"Process: {process.Name.Value}; Run: {run.Id}; Outcome: Failed; Exit code: 7");
        note.ProcessId.ShouldBe(process.Id);
        note.RunId.ShouldBe(run.Id);
    }

    [Fact]
    public async Task Failure_EmailDisabled_SendsNoEmailButNotifiesApp()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        ConfigureEmail(host, false, AlertKind.RunFailed);
        Process process = host.AddProcess();
        Run run = host.AddRunningRun(process);

        await host.Run<RecordRunExited, Unit>(new RecordRunExited(run.Id, 1, false));

        host.Email.Sent.ShouldBeEmpty();
        AppNotification note = host.AppNotifier.Notifications.ShouldHaveSingleItem();
        note.Severity.ShouldBe(NotificationSeverity.Error);
        note.Title.ShouldBe($"Run failed: {process.Name.Value}");
    }

    [Fact]
    public async Task Failure_KindNotSelected_SendsNoEmail()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        ConfigureEmail(host, true, AlertKind.CycleFailed);
        Process process = host.AddProcess();
        Run run = host.AddRunningRun(process);

        await host.Run<RecordRunExited, Unit>(new RecordRunExited(run.Id, 1, false));

        host.Email.Sent.ShouldBeEmpty();
        host.AppNotifier.Notifications.ShouldHaveSingleItem();
    }

    // BI-096
    [Fact]
    public async Task EmailSenderThrows_BlockStillSaved()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        ConfigureEmail(host, true, AlertKind.ProcessBlocked);
        host.Email.Failure = new EmailDeliveryException("smtp down");
        Process process = host.AddProcess();
        Run run = host.AddRunningRun(process);
        ChangeFailureLimit(host, 1);

        await host.Run<RecordRunExited, Unit>(new RecordRunExited(run.Id, 1, false));

        host.Email.Sent.ShouldHaveSingleItem();
        process.Lifecycle.ShouldBe(ProcessLifecycle.Blocked);
        host.Store.History.ShouldContain(h => h.Kind == HistoryEntryKind.ProcessBlocked);
        host.AppNotifier.Notifications.ShouldContain(n => n.Title.StartsWith("Process blocked", StringComparison.Ordinal));
        host.LoggedAt(MsLogLevel.Warning, nameof(EventPublisher)).ShouldBeTrue();
    }

    // BI-093
    [Fact]
    public async Task CycleFailed_NotifiesAppAndEmails()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        ConfigureEmail(host, true, AlertKind.CycleFailed);

        await host.Run<RecordCycleFailure, Unit>(new RecordCycleFailure("boom"));

        EmailMessage message = host.Email.Sent.ShouldHaveSingleItem().Message;
        message.Subject.ShouldBe($"[GapActivator] {Environment.MachineName}: Scheduler cycle failed");
        message.Body.Split('\n').ShouldBe(["Error: boom", "Time: 2030-01-01 12:00:00 +00:00"]);
        AppNotification note = host.AppNotifier.Notifications.ShouldHaveSingleItem();
        note.Severity.ShouldBe(NotificationSeverity.Error);
        note.Title.ShouldBe("Scheduler cycle failed");
        note.Message.ShouldBe("Error: boom");
        note.ProcessId.ShouldBeNull();
        note.RunId.ShouldBeNull();
    }

    // BI-094
    [Fact]
    public async Task Restarted_NotifiesAppWithInformationAndEmailsWhenSelected()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        ConfigureEmail(host, true, AlertKind.ServiceRestarted);

        await host.Run<RestartGapActivator, Unit>(new RestartGapActivator());

        host.Email.Sent.ShouldHaveSingleItem().Message.Subject
            .ShouldBe($"[GapActivator] {Environment.MachineName}: GapActivator restarted");
        AppNotification note = host.AppNotifier.Notifications.ShouldHaveSingleItem();
        note.Severity.ShouldBe(NotificationSeverity.Information);
        note.Title.ShouldBe("GapActivator restarted");
    }

    [Fact]
    public async Task Crashed_SendsOneCrashAlertNotAlsoRunFailed()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        ConfigureEmail(host, true, AlertKind.ServiceProcessCrashed, AlertKind.RunFailed);
        Process process = host.AddProcess(ProcessKind.Service);
        Run run = host.AddRunningRun(process);

        await host.Run<RecordRunExited, Unit>(new RecordRunExited(run.Id, 5, false));

        run.Outcome.ShouldBe(RunOutcome.Crashed);
        EmailMessage message = host.Email.Sent.ShouldHaveSingleItem().Message;
        message.Subject.ShouldBe($"[GapActivator] {Environment.MachineName}: Service crashed: {process.Name.Value}");
        AppNotification note = host.AppNotifier.Notifications.ShouldHaveSingleItem();
        note.Severity.ShouldBe(NotificationSeverity.Warning);
    }

    [Fact]
    public async Task TimedOut_NotifiesAppWithWarning()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        ConfigureEmail(host, false, AlertKind.RunTimedOut);
        Process process = host.AddProcess();
        Run run = host.AddRunningRun(process);
        run.RequestStop(StopReason.TimeLimit, host.Time.GetUtcNow());

        await host.Run<RecordRunExited, Unit>(new RecordRunExited(run.Id, -1, true));

        AppNotification note = host.AppNotifier.Notifications.ShouldHaveSingleItem();
        note.Severity.ShouldBe(NotificationSeverity.Warning);
        note.Title.ShouldBe($"Run timed out: {process.Name.Value}");
    }

    [Fact]
    public async Task Failure_ProcessDeleted_NamesTheRunSnapshot()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        ConfigureEmail(host, true, AlertKind.RunFailed);
        Process process = host.AddProcess();
        Run run = host.AddRunningRun(process);
        host.Store.Processes.Remove(process.Id, out _);

        await host.Run<RecordRunExited, Unit>(new RecordRunExited(run.Id, 1, false));

        EmailMessage message = host.Email.Sent.ShouldHaveSingleItem().Message;
        message.Subject.ShouldEndWith($"Run failed: {run.ProcessNameSnapshot.Value}");
        message.Body.Split('\n')[0].ShouldBe($"Process: {run.ProcessNameSnapshot.Value}");
        host.AppNotifier.Notifications.ShouldHaveSingleItem().Title.ShouldBe($"Run failed: {run.ProcessNameSnapshot.Value}");
    }

    [Fact]
    public async Task Failure_ProcessAndRunBothMissing_NamesDeletedProcess()
    {
        await using ApplicationTestHost host = RunTestHost.Create();
        ConfigureEmail(host, true, AlertKind.RunFailed);
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        EventPublisher publisher = scope.ServiceProvider.GetRequiredService<EventPublisher>();

        await publisher.PublishAsync([new RunCompleted(ARun, AProcess, RunOutcome.Failed, 1, false, At)], CancellationToken.None);

        EmailMessage message = host.Email.Sent.ShouldHaveSingleItem().Message;
        message.Subject.ShouldEndWith("Run failed: (deleted process)");
        message.Body.Split('\n')[0].ShouldBe("Process: (deleted process)");
        host.AppNotifier.Notifications.ShouldHaveSingleItem().Title.ShouldBe("Run failed: (deleted process)");
    }

    [Fact]
    public async Task EmailSenderThrows_AppNotificationStillSent()
    {
        await using ApplicationTestHost host = CycleTestHost.Create();
        ConfigureEmail(host, true, AlertKind.CycleFailed);
        host.Email.Failure = new EmailDeliveryException("smtp down");

        await host.Run<RecordCycleFailure, Unit>(new RecordCycleFailure("boom"));

        host.Email.Sent.ShouldHaveSingleItem();
        host.AppNotifier.Notifications.ShouldHaveSingleItem();
    }

    private static void ConfigureEmail(ApplicationTestHost host, bool enabled, params AlertKind[] alerts) =>
        host.Store.Settings.ChangeEmail(EmailAlertSettings.Create(
            enabled, "smtp.example.com", 587, TlsMode.StartTls, "mailer", new ProtectedSecret("enc:stored"),
            "alerts@example.com", ["ops@example.com"], alerts));

    private static void ChangeFailureLimit(ApplicationTestHost host, int failureLimit)
    {
        InstallationSettings settings = host.Store.Settings;
        settings.Change(
            settings.CycleInterval,
            failureLimit,
            settings.DefaultTimeLimit,
            settings.DefaultGracePeriod,
            settings.LogRetentionDays,
            settings.HistoryRetentionDays,
            settings.LogLevel,
            settings.CheckForUpdates);
    }
}
