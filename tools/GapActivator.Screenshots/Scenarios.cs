using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Settings;
using GapActivator.App.ViewModels;
using GapActivator.Ipc.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GapActivator.Screenshots;

/// <summary>One fully wired main window view model, plus the pieces a scenario drives.</summary>
internal sealed class Scene
{
    public required MainWindowViewModel Main { get; init; }

    public required Navigator Navigator { get; init; }

    public required ProcessesViewModel Processes { get; init; }

    public required HistoryViewModel History { get; init; }

    public required SettingsViewModel Settings { get; init; }

    public required LiveState Live { get; init; }

    public required FakeTimeProvider Time { get; init; }

    public required Guid NightlyId { get; init; }

    public required Guid FailedRunId { get; init; }
}

internal static class Scenes
{
    // The fake clock and the display zone are both UTC, so the times on screen equal the times below.
    public static readonly DateTimeOffset Now = new(2026, 11, 6, 14, 32, 10, TimeSpan.Zero);

    private static DateTimeOffset At(int day, int h, int m, int s = 0) => new(2026, 11, day, h, m, s, TimeSpan.Zero);

    public static async Task<Scene> BuildAsync()
    {
        var l = new Localizer(AppLanguage.English);
        var time = new FakeTimeProvider(Now);
        var zone = TimeZoneInfo.Utc;
        var ui = new ImmediateUiDispatcher();
        var gateway = new ScreenshotGateway { Settings = SettingsData() };

        var nightly = Guid.Parse("11111111-0000-0000-0000-000000000001");
        var report = Guid.Parse("11111111-0000-0000-0000-000000000002");
        var cleanup = Guid.Parse("11111111-0000-0000-0000-000000000003");
        var worker = Guid.Parse("11111111-0000-0000-0000-000000000004");
        var rotation = Guid.Parse("11111111-0000-0000-0000-000000000005");
        var invoices = Guid.Parse("11111111-0000-0000-0000-000000000006");

        var reportRun = Row(Run(1), report, "Report export", RunState.Running, null, RunTrigger.Schedule, At(6, 14, 28, 5), null, null);
        var cleanupFail = Row(Run(3), cleanup, "Disk cleanup", RunState.Completed, RunOutcome.Failed, RunTrigger.Schedule, At(6, 14, 0, 2), At(6, 14, 0, 19), 2);
        var invoiceTimeout = Row(Run(4), invoices, "Invoice sync", RunState.Completed, RunOutcome.TimedOut, RunTrigger.Retry, At(6, 9, 30), At(6, 9, 35), null);

        gateway.Processes.AddRange(
        [
            Summary(nightly, "Nightly backup", ProcessKind.RunToCompletion, ProcessLifecycle.Enabled, null, RunOutcome.Succeeded, At(7, 2, 0), At(6, 2, 4), 0),
            Summary(report, "Report export", ProcessKind.RunToCompletion, ProcessLifecycle.Enabled, new ActiveRunViewDto(reportRun.Id, RunState.Running, RunTrigger.Schedule, reportRun.StartedAt, 4120), RunOutcome.Succeeded, At(6, 15, 30), At(5, 14, 28), 0),
            Summary(cleanup, "Disk cleanup", ProcessKind.RunToCompletion, ProcessLifecycle.Enabled, null, RunOutcome.Failed, At(6, 15, 0), At(6, 14, 0), 1),
            Summary(worker, "Queue worker", ProcessKind.Service, ProcessLifecycle.Enabled, null, RunOutcome.Stopped, null, At(5, 23, 0), 0),
            Summary(rotation, "Log rotation", ProcessKind.RunToCompletion, ProcessLifecycle.Disabled, null, RunOutcome.Succeeded, null, At(1, 3, 0), 0),
            Summary(invoices, "Invoice sync", ProcessKind.RunToCompletion, ProcessLifecycle.Blocked, null, RunOutcome.TimedOut, null, At(6, 9, 35), 3),
        ]);

        gateway.Dashboard = new DashboardDto(
            false,
            [reportRun],
            [
                new UpcomingRunDto(cleanup, "Disk cleanup", At(6, 15, 0)),
                new UpcomingRunDto(report, "Report export", At(6, 15, 30)),
                new UpcomingRunDto(nightly, "Nightly backup", At(7, 2, 0)),
            ],
            [cleanupFail]);

        var summary = gateway.Processes[0];
        gateway.Details[nightly] = new ProcessDetailsDto(
            summary,
            "Copies the shared folders to the backup volume.",
            @"C:\Program Files\ExampleJobs\nightly-backup.exe",
            "--target backup --verify",
            @"C:\Program Files\ExampleJobs",
            [new EnvironmentVariableViewDto("BACKUP_TARGET", "backup", false), new EnvironmentVariableViewDto("API_TOKEN", null, true)],
            TimeSpan.FromMinutes(45),
            TimeSpan.FromSeconds(30),
            [
                new ScheduleViewDto(Guid.NewGuid(), ScheduleInputKind.Recurring, At(7, 2, 0), "FREQ=DAILY;BYHOUR=2;BYMINUTE=0", At(7, 2, 0), null),
            ],
            At(1, 9, 0),
            At(3, 11, 20),
            SensitiveHidden: false);

        gateway.Runs.AddRange(
        [
            reportRun,
            cleanupFail,
            invoiceTimeout,
            Row(Run(5), nightly, "Nightly backup", RunState.Completed, RunOutcome.Succeeded, RunTrigger.Schedule, At(6, 2, 0, 1), At(6, 2, 4, 12), 0),
            Row(Run(6), report, "Report export", RunState.Completed, RunOutcome.Succeeded, RunTrigger.Manual, At(5, 16, 10), At(5, 16, 12), 0),
            Row(Run(7), cleanup, "Disk cleanup", RunState.Completed, RunOutcome.Succeeded, RunTrigger.Schedule, At(5, 14, 0, 1), At(5, 14, 0, 33), 0),
            Row(Run(8), invoices, "Invoice sync", RunState.Completed, RunOutcome.Failed, RunTrigger.Schedule, At(5, 9, 0, 3), At(5, 9, 0, 21), 2),
            Row(Run(9), nightly, "Nightly backup", RunState.Completed, RunOutcome.Succeeded, RunTrigger.Schedule, At(5, 2, 0, 1), At(5, 2, 3, 48), 0),
            Row(Run(10), report, "Report export", RunState.Completed, RunOutcome.Cancelled, RunTrigger.Manual, At(4, 11, 45), At(4, 11, 46), null),
            Row(Run(11), cleanup, "Disk cleanup", RunState.Completed, RunOutcome.Skipped, RunTrigger.Schedule, At(4, 14, 0), At(4, 14, 0), null),
            Row(Run(12), nightly, "Nightly backup", RunState.Completed, RunOutcome.Succeeded, RunTrigger.Schedule, At(4, 2, 0, 1), At(4, 2, 4, 2), 0),
        ]);

        gateway.RunDetails[cleanupFail.Id] = new RunDetailsDto(
            cleanupFail,
            "The process exited with code 2.",
            [
                Entry(HistoryEntryKind.RunStarted, At(6, 14, 0, 2), null),
                Entry(HistoryEntryKind.RunCompleted, At(6, 14, 0, 19), "Failed with exit code 2."),
            ],
            new RunOutputDto(
                true,
                "Scanning the temp folder ...\r\nFound 1,204 files older than 30 days.\r\nDeleted 1,187 files.\r\nERROR: 17 files are locked by another process.\r\nCleanup finished with errors.",
                false));

        gateway.Preview = (anchor, count) =>
        {
            var times = new List<DateTimeOffset>();
            var day = anchor.Date;
            while (times.Count < count)
            {
                day = day.AddDays(1);
                if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                {
                    times.Add(new DateTimeOffset(day.AddHours(6), TimeSpan.Zero));
                }
            }

            return times;
        };

        var navigator = new Navigator();
        var permissions = new Permissions(l, gateway);
        var times = new TimeDisplay(l, zone);
        var live = new LiveState(gateway, NullLogger<LiveState>.Instance);
        var seen = new FailureAcknowledgements(time);
        var dialogs = new NoDialogs();
        var runs = new RunActions(l, gateway, dialogs);
        var appSettings = new InMemoryAppSettings();
        var service = new ServiceActions(l, gateway, dialogs);

        var dashboard = new DashboardViewModel(l, gateway, live, permissions, runs, seen, navigator, times);
        var actions = new ProcessActions(l, gateway, dialogs, runs, time);
        ScheduleEditorViewModel NewSchedule() => new(l, gateway, time, zone, times, ui);
        ProcessEditorViewModel NewEditor() => new(l, gateway, permissions, dialogs, seen, times, zone, NewSchedule, new NoFilePicker());
        var processes = new ProcessesViewModel(l, gateway, live, permissions, actions, runs, navigator, times, NewEditor);
        RunDetailViewModel NewDetail() => new(l, gateway, time, times, new NoClipboard(), ui);
        var history = new HistoryViewModel(l, gateway, live, seen, navigator, times, zone, NewDetail);
        var settings = new SettingsViewModel(l, gateway, permissions, appSettings, service);
        var about = new AboutViewModel(l, gateway, new QuietUpdateChecker(), appSettings, new NoShell(), new AppPaths("app"), new ServicePaths(@"C:\ProgramData\GapActivator"), ui, "0.1.0");
        var banner = new ConnectionBannerViewModel(l, gateway, new NoServiceStarter(), time);
        var main = new MainWindowViewModel(l, navigator, banner, permissions, dashboard, processes, history, settings, about, new QuietUpdateChecker(), ui);

        await live.RefreshAsync();
        await history.ApplyCommand.ExecuteAsync(null);
        await settings.LoadCommand.ExecuteAsync(null);

        return new Scene
        {
            Main = main,
            Time = time,
            Navigator = navigator,
            Processes = processes,
            History = history,
            Settings = settings,
            Live = live,
            NightlyId = nightly,
            FailedRunId = cleanupFail.Id,
        };
    }

    private static Guid Run(int n) => Guid.Parse($"22222222-0000-0000-0000-{n:D12}");

    private static RunRowDto Row(Guid id, Guid processId, string name, RunState state, RunOutcome? outcome, RunTrigger trigger, DateTimeOffset startedAt, DateTimeOffset? endedAt, int? exitCode) =>
        new(id, processId, name, name == "Queue worker" ? ProcessKind.Service : ProcessKind.RunToCompletion, trigger, state, outcome, false, null, startedAt, startedAt, endedAt, exitCode, false);

    private static ProcessSummaryDto Summary(Guid id, string name, ProcessKind kind, ProcessLifecycle lifecycle, ActiveRunViewDto? active, RunOutcome? last, DateTimeOffset? next, DateTimeOffset? lastEnded, int failures) =>
        new(id, name, kind, lifecycle, failures, false, next, active, last, lastEnded);

    private static HistoryEntryDto Entry(HistoryEntryKind kind, DateTimeOffset at, string? message) =>
        new(Guid.NewGuid(), at, kind, null, null, null, null, message);

    private static SettingsViewDto SettingsData() =>
        new(
            TimeSpan.FromSeconds(30),
            3,
            TimeSpan.FromHours(1),
            TimeSpan.FromSeconds(30),
            30,
            180,
            GapActivator.Ipc.Contracts.LogLevel.Information,
            CheckForUpdates: true,
            IsPaused: false,
            new EmailViewDto(true, "smtp.example.com", 587, TlsMode.StartTls, "alerts@example.com", HasPassword: true, "gapactivator@example.com", ["ops@example.com"], [AlertKind.ProcessBlocked, AlertKind.RunFailed, AlertKind.RunTimedOut]));
}
