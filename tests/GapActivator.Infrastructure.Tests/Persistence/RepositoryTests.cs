using GapActivator.Application.Cycle;
using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using GapActivator.Application.Processes;
using GapActivator.Application.Queries;
using GapActivator.Application.Runs;
using GapActivator.Application.Settings;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Scheduling;
using GapActivator.Domain.Settings;
using GapActivator.Infrastructure.Persistence;
using GapActivator.Infrastructure.Tests.Support;
using GapActivator.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GapActivator.Infrastructure.Tests.Persistence;

[Trait("Category", "Integration")]
public sealed class RepositoryTests
{
    private const string Executable = @"C:\Tools\sample.exe";

    private static readonly DateTimeOffset Start = ApplicationTestHost.StartTime;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Register_ThenGetProcessInNewScope_ReturnsIt()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();

        ProcessSaved saved = await host.Send<RegisterProcess, ProcessSaved>(new RegisterProcess(Definition(
            "Nightly export",
            schedules: [Recurring(Start.AddHours(1)), OneTime(Start.AddHours(2))])), ct: Ct);

        await using AsyncServiceScope scope = host.CreateScope();
        Process? loaded = await scope.ServiceProvider.GetRequiredService<IProcessRepository>().GetByIdAsync(saved.Id, Ct);
        loaded.ShouldNotBeNull();
        loaded.Name.Value.ShouldBe("Nightly export");
        loaded.Launch.Executable.Value.ShouldBe(Executable);
        loaded.Schedules.Count.ShouldBe(2);
        loaded.Schedules.OfType<RecurringSchedule>().Single().NextOccurrenceAt.ShouldBe(Start.AddHours(1));
        loaded.Schedules.OfType<OneTimeSchedule>().Single().At.ShouldBe(Start.AddHours(2));
    }

    [Fact]
    public async Task Update_ReplacingSchedules_InsertsNewKeepsUnchangedDeletesRemoved()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();
        ProcessSaved saved = await host.Send<RegisterProcess, ProcessSaved>(new RegisterProcess(Definition(
            "Nightly export",
            schedules: [Recurring(Start.AddMinutes(10)), OneTime(Start.AddHours(2))])), ct: Ct);
        Process registered = await LoadAsync(host, saved.Id);
        RecurringSchedule kept = registered.Schedules.OfType<RecurringSchedule>().Single();
        OneTimeSchedule dropped = registered.Schedules.OfType<OneTimeSchedule>().Single();

        // A recurring schedule created now would get its next occurrence after now (Start + 1 h 10 min).
        host.Time.Advance(TimeSpan.FromMinutes(30));
        await host.Send<UpdateProcess, ProcessSaved>(new UpdateProcess(saved.Id, Definition(
            "Nightly export",
            schedules: [Recurring(Start.AddMinutes(10), id: kept.Id.Value), OneTime(Start.AddHours(3))])), ct: Ct);

        Process reloaded = await LoadAsync(host, saved.Id);
        reloaded.Schedules.Count.ShouldBe(2);
        Schedule reloadedKept = reloaded.Schedules.Single(s => s.Id == kept.Id);
        reloadedKept.NextOccurrenceAt.ShouldBe(Start.AddMinutes(10));
        reloaded.Schedules.OfType<OneTimeSchedule>().Single().At.ShouldBe(Start.AddHours(3));
        await using GapActivatorDbContext db = host.NewScopeDb();
        (await db.Set<Schedule>().CountAsync(Ct)).ShouldBe(2);
        (await db.Set<Schedule>().AnyAsync(s => s.Id == dropped.Id, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Update_ReplacingEverySchedule_DeletesOldRowsAndReloadsEqual()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();
        ProcessSaved saved = await host.Send<RegisterProcess, ProcessSaved>(new RegisterProcess(Definition(
            "Nightly export",
            schedules: [Recurring(Start.AddMinutes(10)), OneTime(Start.AddHours(2))])), ct: Ct);
        ScheduleId[] oldIds = [.. (await LoadAsync(host, saved.Id)).Schedules.Select(s => s.Id)];

        await host.Send<UpdateProcess, ProcessSaved>(new UpdateProcess(saved.Id, Definition(
            "Nightly export",
            schedules: [Recurring(Start.AddHours(4), "FREQ=DAILY"), OneTime(Start.AddHours(5))])), ct: Ct);

        Process reloaded = await LoadAsync(host, saved.Id);
        reloaded.Schedules.Count.ShouldBe(2);
        reloaded.Schedules.ShouldNotContain(s => oldIds.Contains(s.Id));
        RecurringSchedule recurring = reloaded.Schedules.OfType<RecurringSchedule>().Single();
        recurring.Rule.Value.ShouldBe("FREQ=DAILY");
        recurring.Anchor.ShouldBe(Start.AddHours(4));
        recurring.NextOccurrenceAt.ShouldBe(Start.AddHours(4));
        reloaded.Schedules.OfType<OneTimeSchedule>().Single().At.ShouldBe(Start.AddHours(5));
        await using GapActivatorDbContext db = host.NewScopeDb();
        (await db.Set<Schedule>().CountAsync(Ct)).ShouldBe(2);
    }

    [Fact]
    public async Task Delete_Idle_RemovesProcessAndSchedulesKeepsRuns()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();
        ProcessSaved saved = await host.Send<RegisterProcess, ProcessSaved>(new RegisterProcess(Definition(
            "Nightly export", schedules: [OneTime(Start.AddHours(2))])), ct: Ct);
        Run finished = Run.Begin(
            saved.Id, ProcessName.Create("Nightly export"), ProcessKind.RunToCompletion, RunTrigger.Manual, null, false, Start);
        finished.MarkRunning(4242, Start, Start);
        finished.Exit(0, false, Start.AddMinutes(1));
        await host.SeedAsync(finished);

        await host.Send<DeleteProcess, Unit>(new DeleteProcess(saved.Id), ct: Ct);

        await using AsyncServiceScope scope = host.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<IProcessRepository>().GetByIdAsync(saved.Id, Ct)).ShouldBeNull();
        await using GapActivatorDbContext db = host.NewScopeDb();
        (await db.Set<Schedule>().CountAsync(Ct)).ShouldBe(0);
        Run kept = await db.Runs.SingleAsync(Ct);
        kept.Id.ShouldBe(finished.Id);
        kept.ProcessNameSnapshot.Value.ShouldBe("Nightly export");
    }

    // BI-001
    [Fact]
    public async Task Register_NameDifferingOnlyInNonAsciiCase_IsRejectedNameTaken()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();
        await host.Send<RegisterProcess, ProcessSaved>(new RegisterProcess(Definition("Relatório")), ct: Ct);

        CommandRejectedException rejected = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Send<RegisterProcess, ProcessSaved>(new RegisterProcess(Definition("RELATÓRIO")), ct: Ct));

        rejected.Code.ShouldBe(RejectionCodes.NameTaken);
        await using GapActivatorDbContext db = host.NewScopeDb();
        (await db.Processes.CountAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Schema_NameKeyUnique_RejectsDirectInsertOfNonAsciiCaseDuplicate()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();
        await using (GapActivatorDbContext first = host.NewScopeDb())
        {
            first.Processes.Add(new ProcessBuilder().Named("Relatório").Build());
            await first.SaveChangesAsync(Ct);
        }

        await using GapActivatorDbContext second = host.NewScopeDb();
        second.Processes.Add(new ProcessBuilder().Named("RELATÓRIO").WithExecutable(@"C:\Tools\other.exe").Build());

        DbUpdateException failure = await Should.ThrowAsync<DbUpdateException>(() => second.SaveChangesAsync(Ct));
        SqliteException sqlite = failure.InnerException.ShouldBeOfType<SqliteException>();
        sqlite.SqliteErrorCode.ShouldBe(19);
        sqlite.Message.ShouldContain("Processes.NameKey");
    }

    // BI-010
    [Fact]
    public async Task ListByExecutable_DifferentCase_Matches()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();
        ProcessSaved first = await host.Send<RegisterProcess, ProcessSaved>(
            new RegisterProcess(Definition("First", executable: @"C:\Tools Ü\Relatório.exe")), ct: Ct);

        ProcessSaved second = await host.Send<RegisterProcess, ProcessSaved>(
            new RegisterProcess(Definition("Second", executable: @"c:\tools ü\RELATÓRIO.EXE")), ct: Ct);

        second.ProcessesSharingExecutable.ShouldBe(["First"]);
        await using AsyncServiceScope scope = host.CreateScope();
        IProcessRepository processes = scope.ServiceProvider.GetRequiredService<IProcessRepository>();
        IReadOnlyList<Process> excludingFirst = await processes.ListByExecutableAsync(
            ExecutablePath.Create(@"C:\TOOLS Ü\relatório.exe"), first.Id, Ct);
        excludingFirst.Select(p => p.Id).ShouldBe([second.Id]);
    }

    [Fact]
    public async Task ListIds_ProcessesSaved_ReturnsEveryId()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();
        Process first = new ProcessBuilder().Named("First").Build();
        Process second = new ProcessBuilder().Named("Second").Build();
        await host.SeedAsync(first, second);

        IReadOnlyList<ProcessId> ids = await host.Query<ListProcessIds, IReadOnlyList<ProcessId>>(new ListProcessIds(), ct: Ct);

        ids.ShouldBe([first.Id, second.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task Runs_ActiveQueries_ExcludeCompletedRuns()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();
        var busy = ProcessId.New();
        var idle = ProcessId.New();
        Run busyDone = CompletedRun(busy);
        Run busyActive = Run.Begin(busy, ProcessName.Create("Busy"), ProcessKind.RunToCompletion, RunTrigger.Manual, null, false, Start);
        busyActive.MarkRunning(4243, Start, Start);
        await host.SeedAsync(busyDone, busyActive, CompletedRun(idle));

        await using AsyncServiceScope scope = host.CreateScope();
        IRunRepository runs = scope.ServiceProvider.GetRequiredService<IRunRepository>();
        (await runs.GetActiveForProcessAsync(busy, Ct)).ShouldNotBeNull().Id.ShouldBe(busyActive.Id);
        (await runs.GetActiveForProcessAsync(idle, Ct)).ShouldBeNull();
        (await runs.ListActiveAsync(Ct)).Select(r => r.Id).ShouldBe([busyActive.Id]);
        (await runs.GetByIdAsync(busyDone.Id, Ct)).ShouldNotBeNull().State.ShouldBe(RunState.Completed);
    }

    [Fact]
    public async Task GetSettings_RowMissing_ThrowsNotInitialized()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();
        await using (GapActivatorDbContext db = host.NewScopeDb())
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM \"Settings\";", Ct);
        }

        await using AsyncServiceScope scope = host.CreateScope();
        ISettingsRepository settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();

        InvalidOperationException failure = await Should.ThrowAsync<InvalidOperationException>(() => settings.GetAsync(Ct));
        failure.Message.ShouldBe("The database is not initialized.");
    }

    // Carry-over: SendTestEmail is ungated, so it reads the settings while a command may hold the write transaction.
    [Fact]
    public async Task Settings_ReadWhileAnotherScopeHoldsAWriteTransaction_Succeeds()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();
        await using AsyncServiceScope writer = host.CreateScope();
        GapActivatorDbContext writerDb = writer.ServiceProvider.GetRequiredService<GapActivatorDbContext>();
        await using var transaction = await writerDb.Database.BeginTransactionAsync(Ct);
        (await writer.ServiceProvider.GetRequiredService<ISettingsRepository>().GetAsync(Ct)).Pause(Start);
        await writerDb.SaveChangesAsync(Ct);

        await using AsyncServiceScope reader = host.CreateScope();
        InstallationSettings read = await reader.ServiceProvider.GetRequiredService<ISettingsRepository>()
            .GetAsync(Ct)
            .WaitAsync(TimeSpan.FromSeconds(2), Ct);

        read.IsPaused.ShouldBeFalse();
        await transaction.RollbackAsync(Ct);
    }

    [Fact]
    public async Task Secrets_StoredColumns_HoldOnlyCiphertext()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync(
            services => services.AddSingleton<ISecretProtector, ReversingSecretProtector>());

        await host.Send<RegisterProcess, ProcessSaved>(new RegisterProcess(Definition(
            "Nightly export",
            environment: [new EnvironmentVariableInput("TOKEN", "plain-token-value", IsSecret: true)])), ct: Ct);
        await host.Send<UpdateSettings, Unit>(new UpdateSettings(SettingsInput(
            LogLevel.Information,
            new EmailInput(
                true, "smtp.example.com", 587, TlsMode.StartTls, "mailer",
                new SecretChange(SecretChangeKind.Set, "plain-password-value"),
                "alerts@example.com", ["ops@example.com"], [AlertKind.RunFailed]))), ct: Ct);

        await using GapActivatorDbContext db = host.NewScopeDb();
        string environment = await db.Database
            .SqlQueryRaw<string>("SELECT \"Environment\" AS \"Value\" FROM \"Processes\"")
            .SingleAsync(Ct);
        string email = await db.Database.SqlQueryRaw<string>("SELECT \"Email\" AS \"Value\" FROM \"Settings\"").SingleAsync(Ct);
        environment.ShouldBe("[{\"name\":\"TOKEN\",\"secret\":\"rev:eulav-nekot-nialp\"}]");
        environment.ShouldNotContain("plain-token-value");
        email.ShouldContain("\"password\":\"rev:eulav-drowssap-nialp\"");
        email.ShouldNotContain("plain-password-value");
    }

    // BI-039
    [Fact]
    public async Task Cycle_ProcessRowWithCorruptRule_OtherProcessesStillEvaluated()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();
        var calculator = new FakeOccurrenceCalculator(TimeSpan.FromHours(1));
        Process broken = DueProcess("Broken", @"C:\Tools\broken.exe", calculator);
        Process healthy = DueProcess("Healthy", @"C:\Tools\healthy.exe", calculator);
        await host.SeedAsync(broken, healthy);
        await using (GapActivatorDbContext db = host.NewScopeDb())
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE \"Schedules\" SET \"Rule\" = 'NOT A RULE' "
                + "WHERE \"ProcessId\" = (SELECT \"Id\" FROM \"Processes\" WHERE \"Name\" = 'Broken');",
                Ct);
        }

        FakeRunningProcess child = host.Launcher.QueueSucceed(@"C:\Tools\healthy.exe");

        IReadOnlyList<ProcessId> ids = await host.Query<ListProcessIds, IReadOnlyList<ProcessId>>(new ListProcessIds(), ct: Ct);
        await host.Services.GetRequiredService<CycleRunner>().RunOnceAsync(Ct);

        ids.ShouldBe([broken.Id, healthy.Id], ignoreOrder: true);
        RunId runId;
        await using (GapActivatorDbContext db = host.NewScopeDb())
        {
            Run run = await db.Runs.SingleAsync(Ct);
            run.ProcessId.ShouldBe(healthy.Id);
            runId = run.Id;
        }

        child.Exit(0);
        await host.Services.GetRequiredService<RunSupervisor>()
            .WaitForEndAsync(runId, Ct)
            .WaitAsync(TimeSpan.FromSeconds(30), Ct);
        host.Launcher.LaunchRequests.Select(r => r.Executable).ShouldBe([@"C:\Tools\healthy.exe"]);
    }

    internal static ProcessDefinition Definition(
        string name,
        string executable = Executable,
        IReadOnlyList<EnvironmentVariableInput>? environment = null,
        IReadOnlyList<ScheduleInput>? schedules = null) =>
        new(name, string.Empty, ProcessKind.RunToCompletion, executable, string.Empty, null, environment ?? [], null, null, schedules ?? []);

    internal static ScheduleInput OneTime(DateTimeOffset at, Guid? id = null) => new(id, ScheduleInputKind.OneTime, at, null);

    internal static ScheduleInput Recurring(DateTimeOffset anchor, string rule = "FREQ=HOURLY", Guid? id = null) =>
        new(id, ScheduleInputKind.Recurring, anchor, rule);

    internal static SettingsInput SettingsInput(LogLevel logLevel, EmailInput? email = null) => new(
        TimeSpan.FromSeconds(60), 3, null, TimeSpan.FromSeconds(30), 30, 90, logLevel, true,
        email ?? new EmailInput(
            false, "smtp.example.com", 587, TlsMode.StartTls, "mailer", new SecretChange(SecretChangeKind.Keep, null),
            "alerts@example.com", ["ops@example.com"], [AlertKind.RunFailed]));

    /// <summary>A process whose hourly schedule came due ten minutes before <see cref="ApplicationTestHost.StartTime"/>.</summary>
    internal static Process DueProcess(string name, string executable, IOccurrenceCalculator calculator) =>
        new ProcessBuilder()
            .Named(name)
            .WithExecutable(executable)
            .CreatedAt(Start.AddHours(-1))
            .WithSchedule(RecurringSchedule.Create(RecurrenceRule.Parse("FREQ=HOURLY"), Start.AddMinutes(-10), calculator))
            .Build();

    private static Run CompletedRun(ProcessId processId)
    {
        Run run = Run.Begin(processId, ProcessName.Create("Done"), ProcessKind.RunToCompletion, RunTrigger.Manual, null, false, Start);
        run.MarkRunning(4242, Start, Start);
        run.Exit(0, false, Start.AddMinutes(1));
        return run;
    }

    private static async Task<Process> LoadAsync(PersistenceTestHost host, ProcessId id)
    {
        await using AsyncServiceScope scope = host.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IProcessRepository>().GetByIdAsync(id, Ct)).ShouldNotBeNull();
    }

    /// <summary>A protector whose ciphertext never contains the plaintext.</summary>
    private sealed class ReversingSecretProtector : ISecretProtector
    {
        private const string Prefix = "rev:";

        public ProtectedSecret Protect(string plaintext) => new(Prefix + new string([.. plaintext.Reverse()]));

        public string Unprotect(ProtectedSecret secret) => new([.. secret.Ciphertext[Prefix.Length..].Reverse()]);
    }
}
