using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Scheduling;
using GapActivator.Testing;

namespace GapActivator.Domain.Tests.Processes;

public class ProcessLifecycleTests
{
    private static readonly DateTimeOffset Now = ProcessBuilder.BaseTime;
    private static readonly DateTimeOffset Later = Now.AddMinutes(10);
    private static readonly FakeOccurrenceCalculator Calculator = new(TimeSpan.FromHours(1));

    [Fact]
    public void Register_Valid_IsEnabledWithCleanCounters()
    {
        var schedule = OneTimeSchedule.Create(Now.AddHours(8));

        var process = new ProcessBuilder()
            .Named("  Nightly backup  ")
            .WithDescription("  Copies the sample folder.  ")
            .WithSchedule(schedule)
            .CreatedAt(Now)
            .Build();

        process.Id.Value.ShouldNotBe(Guid.Empty);
        process.Name.ShouldBe(ProcessName.Create("Nightly backup"));
        process.Description.ShouldBe("Copies the sample folder.");
        process.Kind.ShouldBe(ProcessKind.RunToCompletion);
        process.Launch.Executable.Value.ShouldBe(@"C:\Tools\sample.exe");
        process.Schedules.ShouldBe([schedule]);
        process.Lifecycle.ShouldBe(ProcessLifecycle.Enabled);
        process.ConsecutiveFailures.ShouldBe(0);
        process.RetryPending.ShouldBeFalse();
        process.CreatedAt.ShouldBe(Now);
        process.UpdatedAt.ShouldBe(Now);
        process.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Register_EmptyDescription_IsAllowed()
    {
        new ProcessBuilder().WithDescription("   ").Build().Description.ShouldBeEmpty();
    }

    [Fact]
    public void Register_DescriptionTooLong_Throws()
    {
        Should.Throw<DomainException>(() => new ProcessBuilder().WithDescription(new string('d', 1_001)).Build());
        new ProcessBuilder().WithDescription(new string('d', 1_000)).Build().Description.Length.ShouldBe(1_000);
    }

    [Fact]
    public void Register_ServiceWithSchedule_Throws()
    {
        Should.Throw<DomainException>(() => new ProcessBuilder()
            .OfKind(ProcessKind.Service)
            .WithSchedule(OneTimeSchedule.Create(Now.AddHours(1)))
            .Build());
    }

    [Fact]
    public void Register_ServiceWithTimeLimit_Throws()
    {
        Should.Throw<DomainException>(() => new ProcessBuilder()
            .OfKind(ProcessKind.Service)
            .WithLimits(new ExecutionLimits(TimeSpan.FromHours(1), null))
            .Build());
    }

    [Fact]
    public void Register_ServiceWithGracePeriodOnly_Succeeds()
    {
        var process = new ProcessBuilder()
            .OfKind(ProcessKind.Service)
            .WithLimits(new ExecutionLimits(null, TimeSpan.FromSeconds(5)))
            .Build();

        process.Kind.ShouldBe(ProcessKind.Service);
        process.Schedules.ShouldBeEmpty();
    }

    [Fact]
    public void Schedules_CannotBeMutatedThroughACast()
    {
        var process = new ProcessBuilder().WithSchedule(OneTimeSchedule.Create(Now.AddHours(1))).Build();

        (process.Schedules is ICollection<Schedule> { IsReadOnly: false }).ShouldBeFalse();
        (process.Schedules is List<Schedule>).ShouldBeFalse();
    }

    [Fact]
    public void Register_ScheduleListChangedAfterwards_DoesNotAffectProcess()
    {
        var schedules = new List<Schedule> { OneTimeSchedule.Create(Now.AddHours(1)) };
        var process = Process.Register(
            ProcessName.Create("Sample job"),
            string.Empty,
            ProcessKind.RunToCompletion,
            SampleLaunch(),
            new ExecutionLimits(null, null),
            schedules,
            Now);

        schedules.Add(OneTimeSchedule.Create(Now.AddHours(2)));

        process.Schedules.Count.ShouldBe(1);
    }

    [Fact]
    public void Update_Valid_ReplacesDefinitionAndKeepsState()
    {
        var process = new ProcessBuilder().CreatedAt(Now).Build();
        process.ApplyRunOutcome(RunOutcome.Failed, failureLimit: 3, Now.AddMinutes(1));
        var schedule = RecurringSchedule.Create(RecurrenceRule.Parse("FREQ=HOURLY"), Now, Calculator);
        var launch = new LaunchOptions(
            ExecutablePath.Create(@"C:\Tools\other.exe"), "--fast", @"C:\Work", EnvironmentVariables.Empty);
        var limits = new ExecutionLimits(TimeSpan.FromHours(2), TimeSpan.FromSeconds(5));

        process.Update(ProcessName.Create("Renamed job"), " New text ", ProcessKind.RunToCompletion, launch, limits, [schedule], Later);

        process.Name.ShouldBe(ProcessName.Create("Renamed job"));
        process.Description.ShouldBe("New text");
        process.Launch.ShouldBe(launch);
        process.Limits.ShouldBe(limits);
        process.Schedules.ShouldBe([schedule]);
        process.Lifecycle.ShouldBe(ProcessLifecycle.Enabled);
        process.ConsecutiveFailures.ShouldBe(1);
        process.RetryPending.ShouldBeTrue();
        process.CreatedAt.ShouldBe(Now);
        process.UpdatedAt.ShouldBe(Later);
    }

    [Fact]
    public void Update_ServiceWithSchedule_Throws()
    {
        var process = new ProcessBuilder().Build();

        Should.Throw<DomainException>(() => process.Update(
            process.Name,
            process.Description,
            ProcessKind.Service,
            process.Launch,
            process.Limits,
            [OneTimeSchedule.Create(Now.AddHours(1))],
            Later));
        process.Kind.ShouldBe(ProcessKind.RunToCompletion);
        process.UpdatedAt.ShouldBe(Now);
    }

    [Fact]
    public void Update_ServiceWithTimeLimit_Throws()
    {
        var process = new ProcessBuilder().Build();

        Should.Throw<DomainException>(() => process.Update(
            process.Name,
            process.Description,
            ProcessKind.Service,
            process.Launch,
            new ExecutionLimits(TimeSpan.FromHours(1), null),
            [],
            Later));
    }

    [Fact]
    public void Update_DescriptionTooLong_Throws()
    {
        var process = new ProcessBuilder().Build();

        Should.Throw<DomainException>(() => process.Update(
            process.Name,
            new string('d', 1_001),
            process.Kind,
            process.Launch,
            process.Limits,
            [],
            Later));
    }

    [Fact]
    public void Update_DescriptionTooLongOnlyBecauseOfWhitespace_IsAcceptedTrimmed()
    {
        var process = new ProcessBuilder().Build();

        process.Update(
            process.Name,
            "  " + new string('d', 1_000) + "  ",
            process.Kind,
            process.Launch,
            process.Limits,
            [],
            Later);

        process.Description.Length.ShouldBe(1_000);
    }

    [Fact]
    public void Update_KindChangedButInvalid_KeepsCounterAndRetry()
    {
        var process = new ProcessBuilder().Build();
        process.ApplyRunOutcome(RunOutcome.Failed, failureLimit: 3, Now.AddMinutes(1));

        Should.Throw<DomainException>(() => process.Update(
            process.Name,
            process.Description,
            ProcessKind.Service,
            process.Launch,
            process.Limits,
            [OneTimeSchedule.Create(Now.AddHours(8))],
            Later));

        process.Kind.ShouldBe(ProcessKind.RunToCompletion);
        process.ConsecutiveFailures.ShouldBe(1);
        process.RetryPending.ShouldBeTrue();
    }

    [Fact]
    public void Update_KindChanged_ResetsFailuresAndRetry()
    {
        var process = new ProcessBuilder().Build();
        process.ApplyRunOutcome(RunOutcome.Failed, failureLimit: 3, Now.AddMinutes(1));

        process.Update(process.Name, process.Description, ProcessKind.Service, process.Launch, process.Limits, [], Later);

        process.Kind.ShouldBe(ProcessKind.Service);
        process.ConsecutiveFailures.ShouldBe(0);
        process.RetryPending.ShouldBeFalse();
        process.UpdatedAt.ShouldBe(Later);
    }

    [Fact]
    public void Update_SameKind_KeepsFailures()
    {
        var process = new ProcessBuilder().Build();
        process.ApplyRunOutcome(RunOutcome.Failed, failureLimit: 3, Now.AddMinutes(1));
        process.ApplyRunOutcome(RunOutcome.Failed, failureLimit: 3, Now.AddMinutes(2));

        process.Update(
            ProcessName.Create("Renamed job"), process.Description, ProcessKind.RunToCompletion, process.Launch, process.Limits, [], Later);

        process.ConsecutiveFailures.ShouldBe(2);
        process.RetryPending.ShouldBeTrue();
    }

    [Fact]
    public void Disable_WhenEnabled_Disables()
    {
        var process = new ProcessBuilder().Build();

        process.Disable(Later);

        process.Lifecycle.ShouldBe(ProcessLifecycle.Disabled);
        process.UpdatedAt.ShouldBe(Later);
    }

    [Fact]
    public void Disable_WithRetryPending_ClearsRetry()
    {
        var process = new ProcessBuilder().Build();
        process.ApplyRunOutcome(RunOutcome.Failed, failureLimit: 3, Now.AddMinutes(1));

        process.Disable(Later);

        process.RetryPending.ShouldBeFalse();
        process.ConsecutiveFailures.ShouldBe(1);
    }

    [Fact]
    public void Enable_WhenDisabled_Enables()
    {
        var process = new ProcessBuilder().Build();
        process.Disable(Now.AddMinutes(1));

        process.Enable(Later, Calculator);

        process.Lifecycle.ShouldBe(ProcessLifecycle.Enabled);
        process.UpdatedAt.ShouldBe(Later);
    }

    [Fact]
    public void Unblock_WhenBlocked_EnablesResetsCounterAndRaisesEvent()
    {
        var process = BlockedProcess();

        process.Unblock(Later, Calculator);

        process.Lifecycle.ShouldBe(ProcessLifecycle.Enabled);
        process.ConsecutiveFailures.ShouldBe(0);
        process.RetryPending.ShouldBeFalse();
        process.UpdatedAt.ShouldBe(Later);
        process.DomainEvents.ShouldHaveSingleItem().ShouldBe(new ProcessUnblocked(process.Id, Later));
    }

    [Fact]
    public void Enable_WhenEnabled_Throws()
    {
        Should.Throw<DomainException>(() => new ProcessBuilder().Build().Enable(Later, Calculator));
    }

    [Fact]
    public void Enable_WhenBlocked_Throws()
    {
        var process = BlockedProcess();

        Should.Throw<DomainException>(() => process.Enable(Later, Calculator));
        process.Lifecycle.ShouldBe(ProcessLifecycle.Blocked);
    }

    [Fact]
    public void Disable_WhenDisabled_Throws()
    {
        var process = new ProcessBuilder().Build();
        process.Disable(Now.AddMinutes(1));

        Should.Throw<DomainException>(() => process.Disable(Later));
    }

    [Fact]
    public void Disable_WhenBlocked_Throws()
    {
        var process = BlockedProcess();

        Should.Throw<DomainException>(() => process.Disable(Later));
        process.Lifecycle.ShouldBe(ProcessLifecycle.Blocked);
    }

    [Fact]
    public void Unblock_WhenEnabled_Throws()
    {
        Should.Throw<DomainException>(() => new ProcessBuilder().Build().Unblock(Later, Calculator));
    }

    [Fact]
    public void Unblock_WhenDisabled_Throws()
    {
        var process = new ProcessBuilder().Build();
        process.Disable(Now.AddMinutes(1));

        Should.Throw<DomainException>(() => process.Unblock(Later, Calculator));
        process.Lifecycle.ShouldBe(ProcessLifecycle.Disabled);
    }

    [Fact]
    public void EffectiveTimeLimit_Service_IsNull()
    {
        var process = new ProcessBuilder().OfKind(ProcessKind.Service).Build();

        process.EffectiveTimeLimit(TimeSpan.FromHours(8)).ShouldBeNull();
    }

    [Fact]
    public void EffectiveTimeLimit_RunToCompletion_UsesOwnLimitThenDefault()
    {
        var own = new ProcessBuilder().WithLimits(new ExecutionLimits(TimeSpan.FromHours(2), null)).Build();
        var inherited = new ProcessBuilder().Build();

        own.EffectiveTimeLimit(TimeSpan.FromHours(8)).ShouldBe(TimeSpan.FromHours(2));
        inherited.EffectiveTimeLimit(TimeSpan.FromHours(8)).ShouldBe(TimeSpan.FromHours(8));
        inherited.EffectiveTimeLimit(null).ShouldBeNull();
    }

    [Fact]
    public void EffectiveGracePeriod_UsesOwnPeriodThenDefault()
    {
        var own = new ProcessBuilder().WithLimits(new ExecutionLimits(null, TimeSpan.FromSeconds(5))).Build();
        var inherited = new ProcessBuilder().Build();

        own.EffectiveGracePeriod(TimeSpan.FromSeconds(30)).ShouldBe(TimeSpan.FromSeconds(5));
        inherited.EffectiveGracePeriod(TimeSpan.FromSeconds(30)).ShouldBe(TimeSpan.FromSeconds(30));
    }

    private static Process BlockedProcess()
    {
        var process = new ProcessBuilder().Build();
        process.ApplyRunOutcome(RunOutcome.Failed, failureLimit: 1, Now.AddMinutes(1));
        process.ClearDomainEvents();
        return process;
    }

    private static LaunchOptions SampleLaunch() =>
        new(ExecutablePath.Create(@"C:\Tools\sample.exe"), string.Empty, null, EnvironmentVariables.Empty);
}
