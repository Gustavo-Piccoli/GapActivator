using GapActivator.Ipc.Contracts;

namespace GapActivator.App.Tests.Fakes;

/// <summary>Small builders for the service DTOs, so a test states only what it cares about.</summary>
internal static class Dtos
{
    public static readonly DateTimeOffset Epoch = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public static ProcessSummaryDto Summary(
        string name = "Sample",
        Guid? id = null,
        ProcessKind kind = ProcessKind.RunToCompletion,
        ProcessLifecycle lifecycle = ProcessLifecycle.Enabled,
        ActiveRunViewDto? activeRun = null,
        RunOutcome? lastOutcome = null,
        DateTimeOffset? nextScheduledAt = null,
        int consecutiveFailures = 0,
        bool retryPending = false) =>
        new(
            id ?? Guid.NewGuid(),
            name,
            kind,
            lifecycle,
            consecutiveFailures,
            retryPending,
            nextScheduledAt,
            activeRun,
            lastOutcome,
            lastOutcome is null ? null : Epoch);

    public static ProcessDetailsDto Details(ProcessSummaryDto? summary = null, IReadOnlyList<ScheduleViewDto>? schedules = null) =>
        new(
            summary ?? Summary(),
            "A sample process.",
            @"C:\Tools\sample.exe",
            null,
            null,
            [],
            null,
            null,
            schedules ?? [],
            Epoch,
            Epoch,
            SensitiveHidden: false);

    public static RunRowDto RunRow(
        string processName = "Sample",
        Guid? id = null,
        Guid? processId = null,
        RunState state = RunState.Completed,
        RunOutcome? outcome = RunOutcome.Succeeded,
        ProcessKind kind = ProcessKind.RunToCompletion,
        RunTrigger trigger = RunTrigger.Manual,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? endedAt = null,
        int? exitCode = 1) =>
        new(
            id ?? Guid.NewGuid(),
            processId ?? Guid.NewGuid(),
            processName,
            kind,
            trigger,
            state,
            outcome,
            IsLate: false,
            ScheduledFor: null,
            CreatedAt: Epoch,
            startedAt ?? Epoch,
            endedAt ?? (state == RunState.Completed ? Epoch.AddSeconds(5) : null),
            exitCode,
            Killed: false);

    public static RunDetailsDto RunDetails(
        RunRowDto? run = null,
        string? error = null,
        IReadOnlyList<HistoryEntryDto>? entries = null,
        string output = "",
        bool exists = true,
        bool truncated = false) =>
        new(run ?? RunRow(), error, entries ?? [], new RunOutputDto(exists, output, truncated));

    public static HistoryEntryDto Entry(HistoryEntryKind kind, DateTimeOffset? at = null, string? message = null) =>
        new(Guid.NewGuid(), at ?? Epoch, kind, null, null, null, null, message);

    public static DashboardDto Dashboard(
        bool isPaused = false,
        IReadOnlyList<RunRowDto>? activeRuns = null,
        IReadOnlyList<UpcomingRunDto>? upcoming = null,
        IReadOnlyList<RunRowDto>? recentFailures = null) =>
        new(isPaused, activeRuns ?? [], upcoming ?? [], recentFailures ?? []);

    public static SettingsViewDto SettingsView() =>
        new(
            TimeSpan.FromSeconds(30),
            3,
            null,
            TimeSpan.FromSeconds(10),
            30,
            90,
            LogLevel.Information,
            CheckForUpdates: true,
            IsPaused: false,
            new EmailViewDto(false, "smtp.example.com", 25, TlsMode.None, null, HasPassword: false, "from@example.com", [], []));

    public static SettingsInputDto SettingsInput() =>
        new(
            TimeSpan.FromSeconds(30),
            3,
            null,
            TimeSpan.FromSeconds(10),
            30,
            90,
            LogLevel.Information,
            CheckForUpdates: true,
            new EmailInputDto(false, "smtp.example.com", 25, TlsMode.None, null, new SecretChangeDto(SecretChangeKind.Keep, null), "from@example.com", [], []));
}
