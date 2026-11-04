using GapActivator.Application.Events;
using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using GapActivator.Application.Processes;
using GapActivator.Application.Queries;
using GapActivator.Application.Tests.Messaging;
using GapActivator.Application.Tests.Processes;
using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Scheduling;
using GapActivator.Domain.Settings;
using GapActivator.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GapActivator.Application.Tests.Queries;

public class QueryTests
{
    private static readonly Caller Viewer = Caller.Viewer("viewer");
    private static readonly Caller Operator = Caller.Operator("operator");

    // Spec §10 (masking)
    [Fact]
    public async Task GetProcess_Viewer_HidesArgumentsAndValues()
    {
        await using var host = new ApplicationTestHost();
        ProcessId id = await RegisterWithSecrets(host);

        ProcessDetails details = await host.Ask<GetProcess, ProcessDetails>(new GetProcess(id), Viewer);

        details.Arguments.ShouldBeNull();
        details.SensitiveHidden.ShouldBeTrue();
        details.Environment.Select(v => v.Name).ShouldBe(["MODE", "TOKEN"]);
        details.Environment.ShouldAllBe(v => v.Value == null);
        details.Executable.ShouldBe(ProcessDefinitions.SampleExecutable);
    }

    // Spec §10 (masking)
    [Fact]
    public async Task GetProcess_Operator_ShowsPlainValuesButNeverSecrets()
    {
        await using var host = new ApplicationTestHost();
        ProcessId id = await RegisterWithSecrets(host);

        ProcessDetails details = await host.Ask<GetProcess, ProcessDetails>(new GetProcess(id), Operator);

        details.Arguments.ShouldBe("--full");
        details.SensitiveHidden.ShouldBeFalse();
        details.Environment.ShouldBe(
        [
            new EnvironmentVariableView("MODE", "fast", false),
            new EnvironmentVariableView("TOKEN", null, true),
        ]);
        details.TimeLimit.ShouldBe(TimeSpan.FromMinutes(5));
        details.GracePeriod.ShouldBe(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task GetProcess_SystemCaller_NeverShowsSecrets()
    {
        await using var host = new ApplicationTestHost();
        ProcessId id = await RegisterWithSecrets(host);

        ProcessDetails details = await host.Ask<GetProcess, ProcessDetails>(new GetProcess(id));

        details.Environment.Single(v => v.IsSecret).Value.ShouldBeNull();
    }

    [Fact]
    public async Task GetProcess_Missing_IsRejectedNotFound()
    {
        await using var host = new ApplicationTestHost();

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Ask<GetProcess, ProcessDetails>(new GetProcess(ProcessId.New()), Viewer));

        error.Code.ShouldBe(RejectionCodes.NotFound);
    }

    // Q-12
    [Fact]
    public async Task GetProcess_FulfilledOneTime_ShowsFulfilledAt()
    {
        await using var host = new ApplicationTestHost();
        DateTimeOffset now = host.Time.GetUtcNow();
        DateTimeOffset at = now.AddHours(1);
        DateTimeOffset anchor = now.AddHours(2);
        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(ProcessDefinitions.Basic(
            schedules: [ProcessDefinitions.OneTime(at), ProcessDefinitions.Recurring(anchor, "freq=hourly")])));
        host.Time.Advance(TimeSpan.FromHours(1));
        DateTimeOffset fulfilledAt = host.Time.GetUtcNow();
        host.Store.Processes[saved.Id].ConsumeDueOccurrences(fulfilledAt, host.Services.GetRequiredService<IOccurrenceCalculator>());

        ProcessDetails details = await host.Ask<GetProcess, ProcessDetails>(new GetProcess(saved.Id), Viewer);

        ScheduleView oneTime = details.Schedules.Single(s => s.Kind == ScheduleInputKind.OneTime);
        oneTime.At.ShouldBe(at);
        oneTime.Rule.ShouldBeNull();
        oneTime.FulfilledAt.ShouldBe(fulfilledAt);
        oneTime.NextOccurrenceAt.ShouldBeNull();
        ScheduleView recurring = details.Schedules.Single(s => s.Kind == ScheduleInputKind.Recurring);
        recurring.At.ShouldBe(anchor);
        recurring.Rule.ShouldBe("FREQ=HOURLY");
        recurring.NextOccurrenceAt.ShouldBe(anchor);
        recurring.FulfilledAt.ShouldBeNull();
        details.Summary.NextScheduledAt.ShouldBe(anchor);
        details.CreatedAt.ShouldBe(ApplicationTestHost.StartTime);
    }

    [Fact]
    public async Task ListProcesses_IncludesActiveRunAndLastOutcome()
    {
        await using var host = new ApplicationTestHost();
        DateTimeOffset now = host.Time.GetUtcNow();
        Process busy = AddProcess(host, "Busy");
        Process idle = AddProcess(host, "Idle");
        AddRun(host, busy, RunOutcome.Failed, now.AddHours(-2));
        Run running = AddRunning(host, busy, now.AddMinutes(-3), 4242);
        AddRun(host, idle, RunOutcome.Succeeded, now.AddHours(-5));
        Run lastIdle = AddRun(host, idle, RunOutcome.Failed, now.AddHours(-1));

        IReadOnlyList<ProcessSummary> list = await host.Ask<ListProcesses, IReadOnlyList<ProcessSummary>>(new ListProcesses(), Viewer);

        ProcessSummary busySummary = list.Single(p => p.Id == busy.Id);
        busySummary.ActiveRun.ShouldBe(new ActiveRunView(running.Id, RunState.Running, RunTrigger.Manual, now.AddMinutes(-3), 4242));
        busySummary.LastOutcome.ShouldBe(RunOutcome.Failed);
        busySummary.LastEndedAt.ShouldBe(now.AddHours(-2));
        ProcessSummary idleSummary = list.Single(p => p.Id == idle.Id);
        idleSummary.ActiveRun.ShouldBeNull();
        idleSummary.LastOutcome.ShouldBe(RunOutcome.Failed);
        idleSummary.LastEndedAt.ShouldBe(lastIdle.EndedAt);
    }

    [Fact]
    public async Task ListProcesses_OrdersByNameIgnoringCase_AndShowsRetryAndNext()
    {
        await using var host = new ApplicationTestHost();
        DateTimeOffset now = host.Time.GetUtcNow();
        AddProcess(host, "charlie");
        AddProcess(host, "Bravo");
        Process alpha = AddProcess(host, "alpha");
        alpha.ApplyRunOutcome(RunOutcome.Failed, 3, now);
        var calculator = new FakeOccurrenceCalculator(TimeSpan.FromHours(1));
        DateTimeOffset early = now.AddMinutes(30);
        Process scheduled = new ProcessBuilder().Named("Delta")
            .WithSchedule(OneTimeSchedule.Create(now.AddHours(3)))
            .WithSchedule(RecurringSchedule.Create(RecurrenceRule.Parse("FREQ=HOURLY"), early, calculator))
            .Build();
        host.Store.Processes[scheduled.Id] = scheduled;

        IReadOnlyList<ProcessSummary> list = await host.Ask<ListProcesses, IReadOnlyList<ProcessSummary>>(new ListProcesses(), Viewer);

        list.Select(p => p.Name).ShouldBe(["alpha", "Bravo", "charlie", "Delta"]);
        list[0].RetryPending.ShouldBeTrue();
        list[0].ConsecutiveFailures.ShouldBe(1);
        list[0].NextScheduledAt.ShouldBeNull();
        list[3].NextScheduledAt.ShouldBe(early);
    }

    // Spec §10 (masking)
    [Fact]
    public async Task ListRuns_ViewerCanList()
    {
        await using var host = new ApplicationTestHost();
        DateTimeOffset now = host.Time.GetUtcNow();
        Process kept = AddProcess(host, "Kept");
        Process other = AddProcess(host, "Other");
        Run older = AddRun(host, kept, RunOutcome.Failed, now.AddHours(-3));
        Run newer = AddRun(host, kept, RunOutcome.Succeeded, now.AddHours(-1));
        AddRun(host, other, RunOutcome.Succeeded, now.AddHours(-2));
        host.Store.Processes.Remove(kept.Id);

        RunRowPage page = await host.Ask<ListRuns, RunRowPage>(
            new ListRuns(new RunFilter(kept.Id, null, null, null, 1, 10)), Viewer);

        page.TotalCount.ShouldBe(2);
        page.Items.Select(r => r.Id).ShouldBe([newer.Id, older.Id]);
        RunRow row = page.Items[1];
        row.ProcessName.ShouldBe("Kept");
        row.ProcessId.ShouldBe(kept.Id);
        row.Kind.ShouldBe(ProcessKind.RunToCompletion);
        row.Outcome.ShouldBe(RunOutcome.Failed);
        row.State.ShouldBe(RunState.Completed);
        row.ExitCode.ShouldBe(1);
        row.EndedAt.ShouldBe(now.AddHours(-3));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 201)]
    public async Task ListRuns_InvalidPaging_ThrowsDomainException(int pageNumber, int pageSize)
    {
        await using var host = new ApplicationTestHost();

        await Should.ThrowAsync<DomainException>(() => host.Ask<ListRuns, RunRowPage>(
            new ListRuns(new RunFilter(null, null, null, null, pageNumber, pageSize)), Viewer));
    }

    [Fact]
    public async Task GetRun_IncludesEntriesInOrderAndOutputTail()
    {
        await using var host = new ApplicationTestHost();
        DateTimeOffset now = host.Time.GetUtcNow();
        Process process = AddProcess(host, "Job");
        Run run = AddRun(host, process, RunOutcome.Failed, now.AddHours(-1));
        Run unrelated = AddRun(host, process, RunOutcome.Succeeded, now.AddHours(-2));
        HistoryEntry second = Entry(now.AddMinutes(-30), HistoryEntryKind.RunCompleted, run.Id);
        HistoryEntry first = Entry(now.AddMinutes(-50), HistoryEntryKind.RunStarted, run.Id);
        lock (host.Store.Sync)
        {
            host.Store.History.AddRange([second, first, Entry(now.AddMinutes(-40), HistoryEntryKind.RunStarted, unrelated.Id)]);
        }

        host.Services.GetRequiredService<FakeRunOutputReader>().Outputs[run.Id] = new RunOutput(true, "tail text", true);

        RunDetails details = await host.Ask<GetRun, RunDetails>(new GetRun(run.Id), Viewer);

        details.Run.Id.ShouldBe(run.Id);
        details.Entries.ShouldBe([first, second]);
        details.Output.ShouldBe(new RunOutput(true, "tail text", true));
        details.Error.ShouldBeNull();
    }

    [Fact]
    public async Task GetRun_Missing_IsRejectedNotFound()
    {
        await using var host = new ApplicationTestHost();

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Ask<GetRun, RunDetails>(new GetRun(RunId.New()), Viewer));

        error.Code.ShouldBe(RejectionCodes.NotFound);
    }

    [Fact]
    public async Task GetDashboard_UpcomingOrderedAndLimitedToTen()
    {
        await using var host = new ApplicationTestHost();
        DateTimeOffset now = host.Time.GetUtcNow();
        for (int hours = 12; hours >= 1; hours--)
        {
            Process process = new ProcessBuilder().Named($"Job {hours:00}")
                .WithSchedule(OneTimeSchedule.Create(now.AddHours(hours))).Build();
            host.Store.Processes[process.Id] = process;
        }

        Process disabled = new ProcessBuilder().Named("Disabled soon")
            .WithSchedule(OneTimeSchedule.Create(now.AddMinutes(1))).Build();
        disabled.Disable(now);
        host.Store.Processes[disabled.Id] = disabled;
        Process unscheduled = AddProcess(host, "Unscheduled");
        host.Store.Settings.Pause(now);
        Run active = AddRunning(host, unscheduled, now.AddMinutes(-1), 7);

        Dashboard dashboard = await host.Ask<GetDashboard, Dashboard>(new GetDashboard(), Viewer);

        dashboard.IsPaused.ShouldBeTrue();
        dashboard.Upcoming.Count.ShouldBe(10);
        dashboard.Upcoming.Select(u => u.ProcessName).ShouldBe(Enumerable.Range(1, 10).Select(h => $"Job {h:00}"));
        dashboard.Upcoming[0].At.ShouldBe(now.AddHours(1));
        dashboard.ActiveRuns.Select(r => r.Id).ShouldBe([active.Id]);
        dashboard.ActiveRuns[0].ProcessName.ShouldBe("Unscheduled");
    }

    [Fact]
    public async Task GetDashboard_RecentFailuresExcludeCancelledAndOlderThanADay()
    {
        await using var host = new ApplicationTestHost();
        DateTimeOffset now = host.Time.GetUtcNow();
        Process process = AddProcess(host, "Job");
        Run failed = AddRun(host, process, RunOutcome.Failed, now.AddHours(-3));
        Run abandoned = AddRun(host, process, RunOutcome.Abandoned, now.AddHours(-1));
        AddRun(host, process, RunOutcome.Cancelled, now.AddMinutes(-30));
        AddRun(host, process, RunOutcome.Succeeded, now.AddMinutes(-20));
        AddRun(host, process, RunOutcome.Failed, now.AddHours(-25));
        Process service = new ProcessBuilder().Named("Svc").OfKind(ProcessKind.Service).Build();
        host.Store.Processes[service.Id] = service;
        Run crashed = AddRun(host, service, RunOutcome.Crashed, now.AddHours(-2));

        Dashboard dashboard = await host.Ask<GetDashboard, Dashboard>(new GetDashboard(), Viewer);

        dashboard.RecentFailures.Select(r => r.Id).ShouldBe([abandoned.Id, crashed.Id, failed.Id]);
    }

    [Fact]
    public async Task GetDashboard_RecentFailures_AreLimitedToTen()
    {
        await using var host = new ApplicationTestHost();
        DateTimeOffset now = host.Time.GetUtcNow();
        Process process = AddProcess(host, "Job");
        for (int minutes = 1; minutes <= 12; minutes++)
        {
            AddRun(host, process, RunOutcome.Failed, now.AddMinutes(-minutes));
        }

        Dashboard dashboard = await host.Ask<GetDashboard, Dashboard>(new GetDashboard(), Viewer);

        dashboard.RecentFailures.Count.ShouldBe(10);
        dashboard.RecentFailures[0].EndedAt.ShouldBe(now.AddMinutes(-1));
        dashboard.RecentFailures[9].EndedAt.ShouldBe(now.AddMinutes(-10));
    }

    [Fact]
    public async Task GetSettings_NeverReturnsPassword()
    {
        await using var host = new ApplicationTestHost();
        host.Store.Settings.ChangeEmail(EmailAlertSettings.Create(
            true, "smtp.example.com", 2525, TlsMode.Implicit, "ops", new ProtectedSecret("enc:hunter2"),
            "ops@example.com", ["ops@example.com", "dev@example.com"], [AlertKind.RunFailed, AlertKind.ProcessBlocked]));

        SettingsView view = await host.Ask<GetSettings, SettingsView>(new GetSettings(), Viewer);

        view.Email.HasPassword.ShouldBeTrue();
        view.Email.Enabled.ShouldBeTrue();
        view.Email.Host.ShouldBe("smtp.example.com");
        view.Email.Port.ShouldBe(2525);
        view.Email.Tls.ShouldBe(TlsMode.Implicit);
        view.Email.Username.ShouldBe("ops");
        view.Email.Sender.ShouldBe("ops@example.com");
        view.Email.Recipients.ShouldBe(["ops@example.com", "dev@example.com"]);
        view.Email.Alerts.ShouldBe([AlertKind.ProcessBlocked, AlertKind.RunFailed]);
        view.ToString().ShouldNotContain("hunter2");
        view.ToString().ShouldNotContain("enc:");
        view.CycleInterval.ShouldBe(TimeSpan.FromSeconds(60));
        view.FailureLimit.ShouldBe(3);
        view.IsPaused.ShouldBeFalse();
    }

    [Fact]
    public async Task GetSettings_NoPassword_HasPasswordIsFalse()
    {
        await using var host = new ApplicationTestHost();

        SettingsView view = await host.Ask<GetSettings, SettingsView>(new GetSettings(), Viewer);

        view.Email.HasPassword.ShouldBeFalse();
        view.Email.Enabled.ShouldBeFalse();
    }

    // Q-13
    [Fact]
    public async Task PreviewSchedule_OldAnchor_StartsFromNow()
    {
        await using var host = new ApplicationTestHost();
        DateTimeOffset now = host.Time.GetUtcNow();

        IReadOnlyList<DateTimeOffset> occurrences = await host.Ask<PreviewSchedule, IReadOnlyList<DateTimeOffset>>(
            new PreviewSchedule("FREQ=HOURLY", now.AddHours(-10), 3), Viewer);

        occurrences.ShouldBe([now, now.AddHours(1), now.AddHours(2)]);
    }

    [Fact]
    public async Task PreviewSchedule_FutureAnchor_StartsAtAnchor()
    {
        await using var host = new ApplicationTestHost();
        DateTimeOffset now = host.Time.GetUtcNow();

        IReadOnlyList<DateTimeOffset> occurrences = await host.Ask<PreviewSchedule, IReadOnlyList<DateTimeOffset>>(
            new PreviewSchedule("FREQ=HOURLY", now.AddHours(5), 2), Viewer);

        occurrences.ShouldBe([now.AddHours(5), now.AddHours(6)]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(51)]
    public async Task PreviewSchedule_CountOutOfRange_IsRejected(int count)
    {
        await using var host = new ApplicationTestHost();

        var error = await Should.ThrowAsync<DomainException>(() => host.Ask<PreviewSchedule, IReadOnlyList<DateTimeOffset>>(
            new PreviewSchedule("FREQ=HOURLY", host.Time.GetUtcNow(), count), Viewer));

        error.Message.ShouldContain("between 1 and 50");
    }

    [Fact]
    public async Task PreviewSchedule_InvalidRule_ThrowsDomainException()
    {
        await using var host = new ApplicationTestHost();

        await Should.ThrowAsync<DomainException>(() => host.Ask<PreviewSchedule, IReadOnlyList<DateTimeOffset>>(
            new PreviewSchedule("NOT A RULE", host.Time.GetUtcNow(), 3), Viewer));
    }

    // Q-13
    [Fact]
    public async Task PreviewSchedule_ExhaustedRule_ReturnsFewer()
    {
        await using var host = new ApplicationTestHost(services =>
            services.AddSingleton<IOccurrenceCalculator>(new FakeOccurrenceCalculator(TimeSpan.FromHours(1), count: 3)));
        DateTimeOffset now = host.Time.GetUtcNow();

        IReadOnlyList<DateTimeOffset> occurrences = await host.Ask<PreviewSchedule, IReadOnlyList<DateTimeOffset>>(
            new PreviewSchedule("FREQ=HOURLY;COUNT=3", now.AddHours(1), 10), Viewer);

        occurrences.ShouldBe([now.AddHours(1), now.AddHours(2), now.AddHours(3)]);
    }

    // Q-13
    [Fact]
    public async Task PreviewSchedule_CountSpentBeforeNow_ReturnsNothing()
    {
        await using var host = new ApplicationTestHost(services =>
            services.AddSingleton<IOccurrenceCalculator>(new FakeOccurrenceCalculator(TimeSpan.FromHours(1), count: 3)));
        DateTimeOffset now = host.Time.GetUtcNow();

        IReadOnlyList<DateTimeOffset> occurrences = await host.Ask<PreviewSchedule, IReadOnlyList<DateTimeOffset>>(
            new PreviewSchedule("FREQ=HOURLY;COUNT=3", now.AddHours(-10), 5), Viewer);

        occurrences.ShouldBeEmpty();
    }

    private static async Task<ProcessId> RegisterWithSecrets(ApplicationTestHost host)
    {
        ProcessDefinition definition = ProcessDefinitions.Basic(environment:
        [
            new EnvironmentVariableInput("MODE", "fast", false),
            new EnvironmentVariableInput("TOKEN", "s3cr3t", true),
        ]) with
        {
            Arguments = "--full",
            TimeLimit = TimeSpan.FromMinutes(5),
            GracePeriod = TimeSpan.FromSeconds(20),
        };
        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(definition), Operator);
        return saved.Id;
    }

    private static Process AddProcess(ApplicationTestHost host, string name)
    {
        Process process = new ProcessBuilder().Named(name).Build();
        host.Store.Processes[process.Id] = process;
        return process;
    }

    private static Run AddRun(ApplicationTestHost host, Process process, RunOutcome outcome, DateTimeOffset endedAt)
    {
        DateTimeOffset started = endedAt.AddMinutes(-1);
        Run run = Run.Begin(process.Id, process.Name, process.Kind, RunTrigger.Schedule, null, false, started);
        run.MarkRunning(100, started, started);
        switch (outcome)
        {
            case RunOutcome.Succeeded:
                run.Exit(0, false, endedAt);
                break;
            case RunOutcome.Failed:
            case RunOutcome.Crashed:
                run.Exit(1, false, endedAt);
                break;
            case RunOutcome.Abandoned:
                run.Abandon(endedAt);
                break;
            case RunOutcome.Cancelled:
                run.RequestStop(StopReason.UserCancel, started);
                run.Exit(2, false, endedAt);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        run.Outcome.ShouldBe(outcome);
        run.ClearDomainEvents();
        host.Store.Runs[run.Id] = run;
        return run;
    }

    private static Run AddRunning(ApplicationTestHost host, Process process, DateTimeOffset startedAt, int pid)
    {
        Run run = Run.Begin(process.Id, process.Name, process.Kind, RunTrigger.Manual, null, false, startedAt);
        run.MarkRunning(pid, startedAt, startedAt);
        run.ClearDomainEvents();
        host.Store.Runs[run.Id] = run;
        return run;
    }

    private static HistoryEntry Entry(DateTimeOffset at, HistoryEntryKind kind, RunId runId) =>
        new(Guid.NewGuid(), at, kind, null, runId, null, null, null);
}
