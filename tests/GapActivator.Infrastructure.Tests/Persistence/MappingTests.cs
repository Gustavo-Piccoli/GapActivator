using GapActivator.Application.Events;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Scheduling;
using GapActivator.Domain.Settings;
using GapActivator.Infrastructure.Tests.Support;
using GapActivator.Testing;
using Microsoft.EntityFrameworkCore;

namespace GapActivator.Infrastructure.Tests.Persistence;

[Trait("Category", "Integration")]
public sealed class MappingTests : SqliteTestBase
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Process_RoundTrip_PreservesEveryField()
    {
        await InitializeDatabaseAsync();
        var calculator = new FakeOccurrenceCalculator(TimeSpan.FromHours(1));
        var oneTime = OneTimeSchedule.Create(Now.AddMinutes(30));
        var recurring = RecurringSchedule.Create(
            RecurrenceRule.Parse("FREQ=HOURLY"), new DateTimeOffset(2030, 1, 1, 14, 45, 0, TimeSpan.FromHours(2)), calculator);
        var environment = EnvironmentVariables.Create(
        [
            EnvironmentVariable.Plain("MODE", "full"),
            EnvironmentVariable.Secret("TOKEN", new ProtectedSecret("dpapi:v1:c2VjcmV0")),
        ]);
        var process = Process.Register(
            ProcessName.Create("Relatório noturno"),
            "Builds the nightly report",
            ProcessKind.RunToCompletion,
            new LaunchOptions(ExecutablePath.Create(@"C:\Tools Ü\report.exe"), "\"a b\" c", @"C:\Work Dir", environment),
            new ExecutionLimits(TimeSpan.FromDays(3650), TimeSpan.FromSeconds(45)),
            [oneTime, recurring],
            Now);
        process.ConsumeDueOccurrences(Now.AddHours(1), calculator);
        process.ApplyRunOutcome(RunOutcome.Failed, 5, Now.AddHours(1));
        process.ApplyRunOutcome(RunOutcome.Failed, 5, Now.AddHours(1));
        process.Disable(Now.AddHours(2));
        oneTime.FulfilledAt.ShouldNotBeNull();
        recurring.NextOccurrenceAt.ShouldNotBeNull();

        await using (var db = NewContext())
        {
            db.Processes.Add(process);
            await db.SaveChangesAsync(Ct);
        }

        await using var reader = NewContext();
        Process loaded = await reader.Processes.SingleAsync(p => p.Id == process.Id, Ct);

        loaded.Name.Value.ShouldBe("Relatório noturno");
        loaded.Description.ShouldBe("Builds the nightly report");
        loaded.Kind.ShouldBe(ProcessKind.RunToCompletion);
        loaded.Launch.Executable.Value.ShouldBe(@"C:\Tools Ü\report.exe");
        loaded.Launch.Arguments.ShouldBe("\"a b\" c");
        loaded.Launch.WorkingDirectory.ShouldBe(@"C:\Work Dir");
        loaded.Launch.Environment.ShouldBe(environment);
        loaded.Launch.Environment.Items[1].SecretValue!.Ciphertext.ShouldBe("dpapi:v1:c2VjcmV0");
        loaded.Launch.ShouldBe(process.Launch);
        loaded.Limits.TimeLimit.ShouldBe(TimeSpan.FromDays(3650));
        loaded.Limits.GracePeriod.ShouldBe(TimeSpan.FromSeconds(45));
        loaded.Lifecycle.ShouldBe(ProcessLifecycle.Disabled);
        loaded.ConsecutiveFailures.ShouldBe(2);
        loaded.RetryPending.ShouldBeFalse();
        loaded.CreatedAt.UtcTicks.ShouldBe(process.CreatedAt.UtcTicks);
        loaded.UpdatedAt.UtcTicks.ShouldBe(process.UpdatedAt.UtcTicks);

        loaded.Schedules.Count.ShouldBe(2);
        var loadedOneTime = loaded.Schedules.OfType<OneTimeSchedule>().ShouldHaveSingleItem();
        loadedOneTime.Id.ShouldBe(oneTime.Id);
        loadedOneTime.At.UtcTicks.ShouldBe(oneTime.At.UtcTicks);
        loadedOneTime.NextOccurrenceAt.ShouldBeNull();
        loadedOneTime.FulfilledAt!.Value.UtcTicks.ShouldBe(oneTime.FulfilledAt!.Value.UtcTicks);
        var loadedRecurring = loaded.Schedules.OfType<RecurringSchedule>().ShouldHaveSingleItem();
        loadedRecurring.Id.ShouldBe(recurring.Id);
        loadedRecurring.Rule.Value.ShouldBe("FREQ=HOURLY");
        loadedRecurring.Anchor.UtcTicks.ShouldBe(recurring.Anchor.UtcTicks);
        loadedRecurring.NextOccurrenceAt!.Value.UtcTicks.ShouldBe(recurring.NextOccurrenceAt!.Value.UtcTicks);
    }

    [Fact]
    public async Task Process_UpdateChangingOnlyCase_PersistsNewNameAndExecutable()
    {
        await InitializeDatabaseAsync();
        var process = new ProcessBuilder().Named("report").WithExecutable(@"C:\Tools\report.exe").Build();
        await using (var db = NewContext())
        {
            db.Processes.Add(process);
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = NewContext())
        {
            Process tracked = await db.Processes.SingleAsync(p => p.Id == process.Id, Ct);
            tracked.Update(
                ProcessName.Create("REPORT"),
                tracked.Description,
                tracked.Kind,
                new LaunchOptions(ExecutablePath.Create(@"C:\TOOLS\REPORT.EXE"), string.Empty, null, EnvironmentVariables.Empty),
                tracked.Limits,
                [],
                Now);
            await db.SaveChangesAsync(Ct);
        }

        await using var reader = NewContext();
        Process loaded = await reader.Processes.SingleAsync(p => p.Id == process.Id, Ct);
        loaded.Name.Value.ShouldBe("REPORT");
        loaded.Launch.Executable.Value.ShouldBe(@"C:\TOOLS\REPORT.EXE");
    }

    [Fact]
    public async Task Run_RoundTrip_PreservesEveryFieldToTheTick()
    {
        await InitializeDatabaseAsync();
        var processId = ProcessId.New();
        var processStart = new DateTimeOffset(2030, 1, 1, 14, 0, 1, TimeSpan.FromHours(2)).AddTicks(1_234_567);
        var exited = Run.Begin(
            processId, ProcessName.Create("Nightly backup"), ProcessKind.RunToCompletion, RunTrigger.Schedule,
            Now.AddMinutes(-10), isLate: true, Now);
        exited.MarkRunning(4242, processStart, Now.AddSeconds(1));
        exited.RequestStop(StopReason.UserCancel, Now.AddSeconds(2));
        exited.Exit(7, killed: true, Now.AddSeconds(3));
        var failed = Run.Begin(
            ProcessId.New(), ProcessName.Create("Watcher"), ProcessKind.Service, RunTrigger.ServiceKeepAlive,
            null, isLate: false, Now);
        failed.FailToStart("The system cannot find the file specified.", Now.AddSeconds(1));

        await using (var db = NewContext())
        {
            db.Runs.AddRange(exited, failed);
            await db.SaveChangesAsync(Ct);
        }

        await using var reader = NewContext();
        Run loaded = await reader.Runs.SingleAsync(r => r.Id == exited.Id, Ct);
        loaded.ProcessId.ShouldBe(processId);
        loaded.ProcessNameSnapshot.Value.ShouldBe("Nightly backup");
        loaded.Kind.ShouldBe(ProcessKind.RunToCompletion);
        loaded.Trigger.ShouldBe(RunTrigger.Schedule);
        loaded.ScheduledFor!.Value.UtcTicks.ShouldBe(Now.AddMinutes(-10).UtcTicks);
        loaded.IsLate.ShouldBeTrue();
        loaded.State.ShouldBe(RunState.Completed);
        loaded.StopReason.ShouldBe(StopReason.UserCancel);
        loaded.Pid.ShouldBe(4242);
        loaded.ProcessStartTime!.Value.UtcTicks.ShouldBe(processStart.UtcTicks);
        loaded.CreatedAt.UtcTicks.ShouldBe(Now.UtcTicks);
        loaded.StartedAt!.Value.UtcTicks.ShouldBe(Now.AddSeconds(1).UtcTicks);
        loaded.EndedAt!.Value.UtcTicks.ShouldBe(Now.AddSeconds(3).UtcTicks);
        loaded.Outcome.ShouldBe(RunOutcome.Cancelled);
        loaded.ExitCode.ShouldBe(7);
        loaded.Killed.ShouldBeTrue();
        loaded.Error.ShouldBeNull();

        Run loadedFailed = await reader.Runs.SingleAsync(r => r.Id == failed.Id, Ct);
        loadedFailed.Kind.ShouldBe(ProcessKind.Service);
        loadedFailed.Trigger.ShouldBe(RunTrigger.ServiceKeepAlive);
        loadedFailed.ScheduledFor.ShouldBeNull();
        loadedFailed.IsLate.ShouldBeFalse();
        loadedFailed.StopReason.ShouldBeNull();
        loadedFailed.Pid.ShouldBeNull();
        loadedFailed.ProcessStartTime.ShouldBeNull();
        loadedFailed.StartedAt.ShouldBeNull();
        loadedFailed.Outcome.ShouldBe(RunOutcome.FailedToStart);
        loadedFailed.ExitCode.ShouldBeNull();
        loadedFailed.Killed.ShouldBeFalse();
        loadedFailed.Error.ShouldBe("The system cannot find the file specified.");
    }

    [Fact]
    public async Task Settings_RoundTrip_PreservesEmailWithPasswordAndPausedFlag()
    {
        await InitializeDatabaseAsync();
        var email = EmailAlertSettings.Create(
            true, "smtp.example.com", 2525, TlsMode.Implicit, "alerts", new ProtectedSecret("dpapi:v1:cGFzcw=="),
            "gapactivator@example.com", ["ops@example.com", "dev@example.com"],
            [AlertKind.RunFailed, AlertKind.ProcessBlocked]);
        await using (var db = NewContext())
        {
            InstallationSettings tracked = await db.Settings.SingleAsync(Ct);
            tracked.Change(
                TimeSpan.FromSeconds(15), 7, TimeSpan.FromHours(2), TimeSpan.FromMinutes(1), 10, 20, LogLevel.Warning, false);
            tracked.ChangeEmail(email);
            tracked.Pause(Now);
            await db.SaveChangesAsync(Ct);
        }

        await using var reader = NewContext();
        InstallationSettings loaded = await reader.Settings.SingleAsync(Ct);
        loaded.Id.ShouldBe(1);
        loaded.CycleInterval.ShouldBe(TimeSpan.FromSeconds(15));
        loaded.FailureLimit.ShouldBe(7);
        loaded.DefaultTimeLimit.ShouldBe(TimeSpan.FromHours(2));
        loaded.DefaultGracePeriod.ShouldBe(TimeSpan.FromMinutes(1));
        loaded.LogRetentionDays.ShouldBe(10);
        loaded.HistoryRetentionDays.ShouldBe(20);
        loaded.LogLevel.ShouldBe(LogLevel.Warning);
        loaded.CheckForUpdates.ShouldBeFalse();
        loaded.IsPaused.ShouldBeTrue();
        loaded.Email.ShouldBe(email);
        loaded.Email.Password!.Ciphertext.ShouldBe("dpapi:v1:cGFzcw==");
        loaded.Email.Recipients.ShouldBe(["ops@example.com", "dev@example.com"]);
        reader.Entry(loaded).Property<int>("Version").CurrentValue.ShouldBe(1);
    }

    [Fact]
    public async Task HistoryEntry_RoundTrip_PreservesNullableIdsAndOutcome()
    {
        await InitializeDatabaseAsync();
        var completed = new HistoryEntry(
            Guid.CreateVersion7(), Now, HistoryEntryKind.RunCompleted, ProcessId.New(), RunId.New(), RunOutcome.Failed, -1,
            "Exited with code -1.");
        var paused = new HistoryEntry(
            Guid.CreateVersion7(), Now.ToOffset(TimeSpan.FromHours(-3)), HistoryEntryKind.InstallationPaused, null, null,
            null, null, null);
        await using (var db = NewContext())
        {
            db.HistoryEntries.AddRange(completed, paused);
            await db.SaveChangesAsync(Ct);
        }

        await using var reader = NewContext();
        HistoryEntry loadedCompleted = await reader.HistoryEntries.SingleAsync(e => e.Id == completed.Id, Ct);
        HistoryEntry loadedPaused = await reader.HistoryEntries.SingleAsync(e => e.Id == paused.Id, Ct);
        loadedCompleted.ShouldBe(completed);
        loadedPaused.ShouldBe(paused);
        loadedPaused.OccurredAt.UtcTicks.ShouldBe(paused.OccurredAt.UtcTicks);
    }

    [Fact]
    public async Task Dates_DifferentOffsets_SortByInstantInSql()
    {
        await InitializeDatabaseAsync();
        // 09:00Z, 10:00Z and 09:30Z: their local clock readings (12:00, 10:00, 04:30) sort the other way.
        Run first = NewRun(new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.FromHours(3)));
        Run third = NewRun(new DateTimeOffset(2030, 1, 1, 10, 0, 0, TimeSpan.Zero));
        Run second = NewRun(new DateTimeOffset(2030, 1, 1, 4, 30, 0, TimeSpan.FromHours(-5)));
        await using (var db = NewContext())
        {
            db.Runs.AddRange(first, third, second);
            await db.SaveChangesAsync(Ct);
        }

        await using var reader = NewContext();
        List<RunId> ordered = await reader.Runs.OrderBy(r => r.CreatedAt).Select(r => r.Id).ToListAsync(Ct);

        ordered.ShouldBe([first.Id, second.Id, third.Id]);
    }

    [Fact]
    public async Task Schema_SecondActiveRunForSameProcess_IsRejected()
    {
        await InitializeDatabaseAsync();
        var processId = ProcessId.New();
        await using (var db = NewContext())
        {
            db.Runs.AddRange(NewRun(Now, processId), NewRun(Now.AddSeconds(1), processId));
            await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        }

        Run completed = NewRun(Now, processId);
        completed.FailToStart("Access is denied.", Now);
        await using (var db = NewContext())
        {
            db.Runs.AddRange(completed, NewRun(Now.AddSeconds(1), processId));
            await db.SaveChangesAsync(Ct);
        }

        await using var reader = NewContext();
        (await reader.Runs.CountAsync(r => r.ProcessId == processId, Ct)).ShouldBe(2);
    }

    private static Run NewRun(DateTimeOffset createdAt, ProcessId? processId = null) =>
        Run.Begin(
            processId ?? ProcessId.New(), ProcessName.Create("Nightly backup"), ProcessKind.RunToCompletion, RunTrigger.Manual,
            null, isLate: false, createdAt);
}
