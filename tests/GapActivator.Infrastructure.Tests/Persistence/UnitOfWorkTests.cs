using GapActivator.Application.Events;
using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using GapActivator.Application.Queries;
using GapActivator.Application.Runs;
using GapActivator.Application.Settings;
using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Scheduling;
using GapActivator.Domain.Settings;
using GapActivator.Infrastructure.Persistence;
using GapActivator.Infrastructure.Tests.Support;
using GapActivator.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MelLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace GapActivator.Infrastructure.Tests.Persistence;

[Trait("Category", "Integration")]
public sealed class UnitOfWorkTests
{
    private static readonly DateTimeOffset Start = ApplicationTestHost.StartTime;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Carry-over: the aggregates and their history entries are saved in one transaction.
    [Fact]
    public async Task Commit_HistoryInsertFails_NothingPersistedAndNothingPublished()
    {
        var interceptor = new FailingCommandInterceptor("INSERT INTO \"HistoryEntries\"");
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync(
            configureDb: options => options.AddInterceptors(interceptor));

        await Should.ThrowAsync<Exception>(() => host.Send<PauseInstallation, Unit>(new PauseInstallation(), ct: Ct));

        // The interceptor throws after the insert ran inside the transaction, so finding no entry proves the rollback.
        interceptor.Executed[^1].ShouldStartWith("INSERT INTO \"HistoryEntries\"");
        await using (AsyncServiceScope scope = host.CreateScope())
        {
            InstallationSettings settings = await scope.ServiceProvider.GetRequiredService<ISettingsRepository>().GetAsync(Ct);
            settings.IsPaused.ShouldBeFalse();
        }

        await using GapActivatorDbContext db = host.NewScopeDb();
        (await db.HistoryEntries.CountAsync(Ct)).ShouldBe(0);
        host.Published.Events.ShouldBeEmpty();
        host.AppNotifier.Notifications.ShouldBeEmpty();
    }

    // The in-memory fake keeps in-place mutations after a failed commit; the real unit of work must not.
    [Fact]
    public async Task Commit_AfterFailure_LaterScopeSeesOriginalState()
    {
        var interceptor = new FailingCommandInterceptor("INSERT INTO \"Runs\"");
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync(
            configureDb: options => options.AddInterceptors(interceptor));
        Process process = RepositoryTests.DueProcess(
            "Nightly export", @"C:\Tools\sample.exe", new FakeOccurrenceCalculator(TimeSpan.FromHours(1)));
        DateTimeOffset? dueAt = process.Schedules.Single().NextOccurrenceAt;
        await host.SeedAsync(process);

        await Should.ThrowAsync<Exception>(() => host.Send<RunNow, RunId>(new RunNow(process.Id), ct: Ct));

        // EF saves Processes before Runs: the process update really ran and was rolled back.
        interceptor.Executed.ShouldContain(c => c.StartsWith("UPDATE \"Processes\"", StringComparison.Ordinal));
        interceptor.Executed[^1].ShouldStartWith("INSERT INTO \"Runs\"");
        await using (AsyncServiceScope scope = host.CreateScope())
        {
            Process reloaded = (await scope.ServiceProvider.GetRequiredService<IProcessRepository>().GetByIdAsync(process.Id, Ct))
                .ShouldNotBeNull();
            reloaded.Schedules.Single().NextOccurrenceAt.ShouldBe(dueAt);
            reloaded.UpdatedAt.ShouldBe(process.UpdatedAt);
        }

        await using GapActivatorDbContext db = host.NewScopeDb();
        (await db.Runs.CountAsync(Ct)).ShouldBe(0);
        host.Launcher.LaunchRequests.ShouldBeEmpty();
    }

    // BI-042
    [Fact]
    public async Task Commit_RunChangedByAnotherScope_ThrowsAndFirstWriteWins()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();
        Run run = new RunBuilder().InState(RunState.Running).Build();
        await host.SeedAsync(run);
        await using AsyncServiceScope first = host.CreateScope();
        await using AsyncServiceScope second = host.CreateScope();
        Run firstCopy = (await first.ServiceProvider.GetRequiredService<IRunRepository>().GetByIdAsync(run.Id, Ct)).ShouldNotBeNull();
        Run secondCopy = (await second.ServiceProvider.GetRequiredService<IRunRepository>().GetByIdAsync(run.Id, Ct)).ShouldNotBeNull();

        firstCopy.RequestStop(StopReason.UserCancel, Start.AddMinutes(1));
        await first.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(Ct);
        secondCopy.Exit(0, false, Start.AddMinutes(2));

        await Should.ThrowAsync<DbUpdateConcurrencyException>(
            () => second.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(Ct));
        await using GapActivatorDbContext db = host.NewScopeDb();
        Run reloaded = await db.Runs.SingleAsync(Ct);
        reloaded.State.ShouldBe(RunState.Stopping);
        reloaded.StopReason.ShouldBe(StopReason.UserCancel);
        reloaded.Outcome.ShouldBeNull();
    }

    // BI-042: a change to the schedules alone still guards the process.
    [Fact]
    public async Task Commit_ScheduleOnlyChangeByTwoScopes_SecondThrowsConcurrency()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();
        var calculator = new FakeOccurrenceCalculator(TimeSpan.FromHours(1));

        // Due exactly at its creation time, so consuming it leaves UpdatedAt as it was: only the schedule changes.
        Process process = new ProcessBuilder().CreatedAt(Start).WithSchedule(OneTimeSchedule.Create(Start)).Build();
        await host.SeedAsync(process);
        await using AsyncServiceScope first = host.CreateScope();
        await using AsyncServiceScope second = host.CreateScope();
        Process firstCopy = (await first.ServiceProvider.GetRequiredService<IProcessRepository>().GetByIdAsync(process.Id, Ct))
            .ShouldNotBeNull();
        Process secondCopy = (await second.ServiceProvider.GetRequiredService<IProcessRepository>().GetByIdAsync(process.Id, Ct))
            .ShouldNotBeNull();

        firstCopy.ConsumeDueOccurrences(Start, calculator);
        secondCopy.ConsumeDueOccurrences(Start, calculator);
        await first.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(Ct);

        await Should.ThrowAsync<DbUpdateConcurrencyException>(
            () => second.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(Ct));
        await using GapActivatorDbContext db = host.NewScopeDb();
        (await db.Processes.Select(p => EF.Property<int>(p, "Version")).SingleAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Commit_SettingsLogLevelChanged_UpdatesLogLevelSwitch()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();
        host.LogLevel.Minimum.ShouldBe(MelLogLevel.Information);

        await host.Send<UpdateSettings, Unit>(new UpdateSettings(RepositoryTests.SettingsInput(LogLevel.Warning)), ct: Ct);

        host.LogLevel.Minimum.ShouldBe(MelLogLevel.Warning);
    }

    [Fact]
    public async Task DequeueDomainEvents_LoadedAndAddedAggregates_ReturnsTheirEventsThenNone()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();
        Process removed = new ProcessBuilder().Build();
        await host.SeedAsync(removed);
        await using AsyncServiceScope scope = host.CreateScope();
        IServiceProvider services = scope.ServiceProvider;

        (await services.GetRequiredService<ISettingsRepository>().GetAsync(Ct)).Pause(Start);
        Process loaded = (await services.GetRequiredService<IProcessRepository>().GetByIdAsync(removed.Id, Ct)).ShouldNotBeNull();
        loaded.ApplyRunOutcome(RunOutcome.Failed, 1, Start);
        services.GetRequiredService<IProcessRepository>().Remove(loaded);
        Run skipped = Run.Skip(loaded.Id, loaded.Name, loaded.Kind, Start.AddMinutes(-1), Start);
        services.GetRequiredService<IRunRepository>().Add(skipped);
        IUnitOfWork unitOfWork = services.GetRequiredService<IUnitOfWork>();

        IReadOnlyList<IDomainEvent> events = unitOfWork.DequeueDomainEvents();

        events.Select(e => e.GetType()).ShouldBe(
            [typeof(InstallationPaused), typeof(ProcessBlocked), typeof(RunSkipped), typeof(RunCompleted)],
            ignoreOrder: true);
        events.OfType<RunSkipped>().Single().RunId.ShouldBe(skipped.Id);
        events.OfType<ProcessBlocked>().Single().ProcessId.ShouldBe(removed.Id);
        unitOfWork.DequeueDomainEvents().ShouldBeEmpty();
        loaded.DomainEvents.ShouldBeEmpty();
        skipped.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task HistoryWriter_EntriesOfOneScope_KeepTheirInsertionOrder()
    {
        await using PersistenceTestHost host = await PersistenceTestHost.StartAsync();
        HistoryEntry[] entries =
        [
            Entry(HistoryEntryKind.InstallationPaused),
            Entry(HistoryEntryKind.InstallationResumed),
            Entry(HistoryEntryKind.InstallationPaused),
        ];

        await host.SeedAsync(entries);

        await using GapActivatorDbContext db = host.NewScopeDb();
        var stored = await db.HistoryEntries
            .Select(e => new { e.Id, Ordinal = EF.Property<int>(e, "Ordinal") })
            .ToListAsync(Ct);
        stored.OrderBy(e => e.Ordinal).Select(e => e.Id).ShouldBe(entries.Select(e => e.Id));
        stored.Select(e => e.Ordinal).ShouldBe([0, 1, 2], ignoreOrder: true);
    }

    // BI-076, Q-1
    [Fact]
    public async Task Pause_HostRestarted_StaysPaused()
    {
        await using var root = new TempDataRoot();
        await using (PersistenceTestHost before = await PersistenceTestHost.StartAsync(existingRoot: root))
        {
            await before.Send<PauseInstallation, Unit>(new PauseInstallation(), ct: Ct);
        }

        await using PersistenceTestHost after = await PersistenceTestHost.StartAsync(existingRoot: root);
        SettingsView settings = await after.Query<GetSettings, SettingsView>(new GetSettings(), ct: Ct);

        settings.IsPaused.ShouldBeTrue();
    }

    private static HistoryEntry Entry(HistoryEntryKind kind) =>
        new(Guid.CreateVersion7(), Start, kind, null, null, null, null, null);
}
