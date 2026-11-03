using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Scheduling;
using GapActivator.Testing;

namespace GapActivator.Domain.Tests.Processes;

public class ProcessDueTests
{
    private static readonly DateTimeOffset Anchor = new(2027, 1, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan LateThreshold = TimeSpan.FromMinutes(2);
    private static readonly FakeOccurrenceCalculator Calculator = new(TimeSpan.FromHours(1));
    private static readonly RecurrenceRule Hourly = RecurrenceRule.Parse("FREQ=HOURLY");

    [Fact]
    public void EvaluateDue_BeforeFirstOccurrence_IsNotDue()
    {
        var process = HourlyProcess();

        var decision = process.EvaluateDue(At(7, 59), LateThreshold, Calculator);

        decision.ShouldBe(DueDecision.NotDue);
        process.NextScheduledAt.ShouldBe(At(8, 0));
    }

    [Fact]
    public void EvaluateDue_ExactlyAtOccurrence_IsDueAndNotLate()
    {
        var process = HourlyProcess();

        var decision = process.EvaluateDue(At(8, 0), LateThreshold, Calculator);

        decision.ShouldBe(new DueDecision(true, RunTrigger.Schedule, At(8, 0), false));
        process.NextScheduledAt.ShouldBe(At(9, 0));
    }

    [Fact]
    public void EvaluateDue_WithinLateThreshold_IsNotLate()
    {
        var process = HourlyProcess();

        process.EvaluateDue(At(8, 2), LateThreshold, Calculator).IsLate.ShouldBeFalse();
    }

    [Fact]
    public void EvaluateDue_MissedOccurrences_IsDueOnceAndLate()
    {
        var process = HourlyProcess();

        var decision = process.EvaluateDue(At(11, 30), LateThreshold, Calculator);

        decision.ShouldBe(new DueDecision(true, RunTrigger.Schedule, At(8, 0), true));
        process.NextScheduledAt.ShouldBe(At(12, 0));
        process.EvaluateDue(At(11, 30), LateThreshold, Calculator).ShouldBe(DueDecision.NotDue);
    }

    [Fact]
    public void EvaluateDue_TwoSchedulesDue_IsDueOnceForTheEarliest()
    {
        var recurring = RecurringSchedule.Create(Hourly, Anchor, Calculator);
        var oneTime = OneTimeSchedule.Create(At(8, 30));
        var process = new ProcessBuilder().WithSchedule(recurring).WithSchedule(oneTime).Build();

        var decision = process.EvaluateDue(At(8, 45), LateThreshold, Calculator);

        decision.ShouldBe(new DueDecision(true, RunTrigger.Schedule, At(8, 0), true));
        recurring.NextOccurrenceAt.ShouldBe(At(9, 0));
        oneTime.FulfilledAt.ShouldBe(At(8, 45));
        oneTime.IsExhausted.ShouldBeTrue();
        process.NextScheduledAt.ShouldBe(At(9, 0));
        process.EvaluateDue(At(8, 45), LateThreshold, Calculator).ShouldBe(DueDecision.NotDue);
    }

    [Fact]
    public void EvaluateDue_ClockMovedBackwards_IsNotDueAndDoesNotThrow()
    {
        var process = HourlyProcess();
        process.EvaluateDue(At(11, 30), LateThreshold, Calculator).IsDue.ShouldBeTrue();

        var decision = process.EvaluateDue(At(9, 0), LateThreshold, Calculator);

        decision.ShouldBe(DueDecision.NotDue);
        process.NextScheduledAt.ShouldBe(At(12, 0));
    }

    [Fact]
    public void EvaluateDue_RetryPendingAndNothingScheduled_IsDueAsRetry()
    {
        var process = new ProcessBuilder().Build();
        process.ApplyRunOutcome(RunOutcome.Failed, failureLimit: 3, At(7, 0));

        var decision = process.EvaluateDue(At(7, 1), LateThreshold, Calculator);

        decision.ShouldBe(new DueDecision(true, RunTrigger.Retry, null, false));
        process.RetryPending.ShouldBeTrue();
    }

    [Fact]
    public void EvaluateDue_RetryPendingAndScheduleDue_IsDueAsSchedule()
    {
        var process = HourlyProcess();
        process.ApplyRunOutcome(RunOutcome.Failed, failureLimit: 3, At(7, 0));

        var decision = process.EvaluateDue(At(8, 0), LateThreshold, Calculator);

        decision.ShouldBe(new DueDecision(true, RunTrigger.Schedule, At(8, 0), false));
        process.RetryPending.ShouldBeTrue();
    }

    [Fact]
    public void EvaluateDue_WhenDisabled_IsNotDueAndConsumesNothing()
    {
        var process = HourlyProcess();
        process.Disable(At(7, 1));

        process.EvaluateDue(At(8, 30), LateThreshold, Calculator).ShouldBe(DueDecision.NotDue);
        process.NextScheduledAt.ShouldBe(At(8, 0));
    }

    [Fact]
    public void EvaluateDue_NullCalculatorWhileDisabled_Throws()
    {
        var process = HourlyProcess();
        process.Disable(At(7, 1));

        Should.Throw<ArgumentNullException>(() => process.EvaluateDue(At(8, 30), LateThreshold, null!));
        Should.Throw<ArgumentNullException>(() => process.ConsumeDueOccurrences(At(8, 30), null!));
    }

    [Fact]
    public void EvaluateDue_WhenBlocked_IsNotDue()
    {
        var process = HourlyProcess();
        process.ApplyRunOutcome(RunOutcome.Failed, failureLimit: 1, At(7, 0));

        process.EvaluateDue(At(8, 30), LateThreshold, Calculator).ShouldBe(DueDecision.NotDue);
    }

    [Fact]
    public void EvaluateDue_ServiceKind_IsNotDue()
    {
        var process = new ProcessBuilder().OfKind(ProcessKind.Service).Build();
        process.ApplyRunOutcome(RunOutcome.Crashed, failureLimit: 3, At(7, 0));

        process.EvaluateDue(At(8, 30), LateThreshold, Calculator).ShouldBe(DueDecision.NotDue);
    }

    [Fact]
    public void ConsumeDueOccurrences_WithDueSchedules_AdvancesThem()
    {
        var recurring = RecurringSchedule.Create(Hourly, Anchor, Calculator);
        var oneTime = OneTimeSchedule.Create(At(8, 15));
        var process = new ProcessBuilder().WithSchedule(recurring).WithSchedule(oneTime).Build();

        process.ConsumeDueOccurrences(At(8, 30), Calculator);

        recurring.NextOccurrenceAt.ShouldBe(At(9, 0));
        oneTime.FulfilledAt.ShouldBe(At(8, 30));
        process.UpdatedAt.ShouldBe(At(8, 30));
    }

    [Fact]
    public void ConsumeDueOccurrences_NothingDue_ChangesNothing()
    {
        var process = HourlyProcess();
        var updatedAt = process.UpdatedAt;

        process.ConsumeDueOccurrences(At(7, 59), Calculator);

        process.NextScheduledAt.ShouldBe(At(8, 0));
        process.UpdatedAt.ShouldBe(updatedAt);
    }

    [Fact]
    public void ConsumeDueOccurrences_WhenDisabled_StillAdvances()
    {
        var process = HourlyProcess();
        process.Disable(At(7, 0));

        process.ConsumeDueOccurrences(At(8, 30), Calculator);

        process.NextScheduledAt.ShouldBe(At(9, 0));
    }

    [Fact]
    public void NextScheduledAt_IgnoresExhaustedSchedules()
    {
        var oneTime = OneTimeSchedule.Create(At(8, 15));
        var later = OneTimeSchedule.Create(At(10, 0));
        var process = new ProcessBuilder().WithSchedule(oneTime).WithSchedule(later).Build();

        process.ConsumeDueOccurrences(At(8, 30), Calculator);

        process.NextScheduledAt.ShouldBe(At(10, 0));
    }

    [Fact]
    public void NextScheduledAt_NoSchedules_IsNull()
    {
        new ProcessBuilder().Build().NextScheduledAt.ShouldBeNull();
    }

    [Fact]
    public void Enable_AfterMissedOccurrences_DiscardsThem()
    {
        var process = HourlyProcess();
        process.Disable(At(7, 0));

        process.Enable(At(10, 30), Calculator);

        process.NextScheduledAt.ShouldBe(At(11, 0));
        process.EvaluateDue(At(10, 30), LateThreshold, Calculator).ShouldBe(DueDecision.NotDue);
    }

    [Fact]
    public void Unblock_AfterMissedOccurrences_DiscardsThem()
    {
        var process = HourlyProcess();
        process.ApplyRunOutcome(RunOutcome.Failed, failureLimit: 3, At(7, 0));
        process.EnforceFailureLimit(failureLimit: 1, At(7, 0));
        process.Lifecycle.ShouldBe(ProcessLifecycle.Blocked);

        process.Unblock(At(10, 30), Calculator);

        process.NextScheduledAt.ShouldBe(At(11, 0));
        process.EvaluateDue(At(10, 30), LateThreshold, Calculator).ShouldBe(DueDecision.NotDue);
    }

    [Fact]
    public void Enable_OneTimeMissedWhileDisabled_IsFulfilledWithoutRun()
    {
        var oneTime = OneTimeSchedule.Create(At(9, 0));
        var process = new ProcessBuilder().WithSchedule(oneTime).Build();
        process.Disable(At(7, 0));

        process.Enable(At(10, 30), Calculator);

        oneTime.FulfilledAt.ShouldBe(At(10, 30));
        oneTime.IsExhausted.ShouldBeTrue();
        process.EvaluateDue(At(10, 30), LateThreshold, Calculator).ShouldBe(DueDecision.NotDue);
    }

    [Fact]
    public void Enable_NothingMissed_KeepsNextOccurrence()
    {
        var process = HourlyProcess();
        process.Disable(At(7, 0));

        process.Enable(At(7, 30), Calculator);

        process.NextScheduledAt.ShouldBe(At(8, 0));
    }

    [Fact]
    public void Enable_NullCalculator_Throws()
    {
        var process = HourlyProcess();
        process.Disable(At(7, 0));

        Should.Throw<ArgumentNullException>(() => process.Enable(At(10, 30), null!));
    }

    [Fact]
    public void Unblock_NullCalculator_Throws()
    {
        var process = HourlyProcess();
        process.EnforceFailureLimit(failureLimit: 1, At(7, 0));
        process.ApplyRunOutcome(RunOutcome.Failed, failureLimit: 1, At(7, 0));

        Should.Throw<ArgumentNullException>(() => process.Unblock(At(10, 30), null!));
    }

    private static Process HourlyProcess() =>
        new ProcessBuilder().WithSchedule(RecurringSchedule.Create(Hourly, Anchor, Calculator)).Build();

    private static DateTimeOffset At(int hour, int minute) => new(2027, 1, 1, hour, minute, 0, TimeSpan.Zero);
}
