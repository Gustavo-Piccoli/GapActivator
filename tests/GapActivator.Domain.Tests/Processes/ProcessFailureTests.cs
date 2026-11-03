using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Testing;

namespace GapActivator.Domain.Tests.Processes;

public class ProcessFailureTests
{
    private const int FailureLimit = 3;
    private static readonly DateTimeOffset Now = ProcessBuilder.BaseTime;
    private static readonly FakeOccurrenceCalculator Calculator = new(TimeSpan.FromHours(1));

    [Fact]
    public void ApplyRunOutcome_TwoFailures_CountsAndSetsRetryPending()
    {
        var process = new ProcessBuilder().Build();

        process.ApplyRunOutcome(RunOutcome.Failed, FailureLimit, Now.AddMinutes(1));
        process.ApplyRunOutcome(RunOutcome.Failed, FailureLimit, Now.AddMinutes(2));

        process.ConsecutiveFailures.ShouldBe(2);
        process.RetryPending.ShouldBeTrue();
        process.Lifecycle.ShouldBe(ProcessLifecycle.Enabled);
        process.UpdatedAt.ShouldBe(Now.AddMinutes(2));
        process.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void ApplyRunOutcome_ThirdFailure_BlocksResetsCounterAndRaisesEvent()
    {
        var process = new ProcessBuilder().Build();
        process.ApplyRunOutcome(RunOutcome.Failed, FailureLimit, Now.AddMinutes(1));
        process.ApplyRunOutcome(RunOutcome.Failed, FailureLimit, Now.AddMinutes(2));

        process.ApplyRunOutcome(RunOutcome.Failed, FailureLimit, Now.AddMinutes(3));

        process.Lifecycle.ShouldBe(ProcessLifecycle.Blocked);
        process.ConsecutiveFailures.ShouldBe(0);
        process.RetryPending.ShouldBeFalse();
        process.UpdatedAt.ShouldBe(Now.AddMinutes(3));
        var blocked = process.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ProcessBlocked>();
        blocked.ShouldBe(new ProcessBlocked(process.Id, FailuresReached: 3, Now.AddMinutes(3)));
    }

    [Theory]
    [InlineData(RunOutcome.FailedToStart)]
    [InlineData(RunOutcome.TimedOut)]
    [InlineData(RunOutcome.Abandoned)]
    public void ApplyRunOutcome_OtherFailureOutcomes_Count(RunOutcome outcome)
    {
        var process = new ProcessBuilder().Build();

        process.ApplyRunOutcome(outcome, FailureLimit, Now.AddMinutes(1));

        process.ConsecutiveFailures.ShouldBe(1);
        process.RetryPending.ShouldBeTrue();
    }

    [Fact]
    public void ApplyRunOutcome_SucceededAfterFailure_ResetsCounterAndRetry()
    {
        var process = new ProcessBuilder().Build();
        process.ApplyRunOutcome(RunOutcome.Failed, FailureLimit, Now.AddMinutes(1));

        process.ApplyRunOutcome(RunOutcome.Succeeded, FailureLimit, Now.AddMinutes(2));

        process.ConsecutiveFailures.ShouldBe(0);
        process.RetryPending.ShouldBeFalse();
        process.UpdatedAt.ShouldBe(Now.AddMinutes(2));
    }

    [Theory]
    [InlineData(RunOutcome.Cancelled)]
    [InlineData(RunOutcome.Interrupted)]
    [InlineData(RunOutcome.Skipped)]
    public void ApplyRunOutcome_NeutralOutcomeAfterTwoFailures_LeavesCounterUnchanged(RunOutcome outcome)
    {
        var process = new ProcessBuilder().Build();
        process.ApplyRunOutcome(RunOutcome.Failed, FailureLimit, Now.AddMinutes(1));
        process.ApplyRunOutcome(RunOutcome.Failed, FailureLimit, Now.AddMinutes(2));

        process.ApplyRunOutcome(outcome, FailureLimit, Now.AddMinutes(3));

        process.ConsecutiveFailures.ShouldBe(2);
        process.RetryPending.ShouldBeTrue();
        process.Lifecycle.ShouldBe(ProcessLifecycle.Enabled);
        process.UpdatedAt.ShouldBe(Now.AddMinutes(2));
    }

    [Fact]
    public void ApplyRunOutcome_ServiceCrashed_CountsWithoutRetryPending()
    {
        var process = new ProcessBuilder().OfKind(ProcessKind.Service).Build();

        process.ApplyRunOutcome(RunOutcome.Crashed, FailureLimit, Now.AddMinutes(1));

        process.ConsecutiveFailures.ShouldBe(1);
        process.RetryPending.ShouldBeFalse();
    }

    [Fact]
    public void MarkServiceFoundRunning_AfterTwoCrashes_ResetsCounter()
    {
        var process = new ProcessBuilder().OfKind(ProcessKind.Service).Build();
        process.ApplyRunOutcome(RunOutcome.Crashed, FailureLimit, Now.AddMinutes(1));
        process.ApplyRunOutcome(RunOutcome.Crashed, FailureLimit, Now.AddMinutes(2));

        process.MarkServiceFoundRunning(Now.AddMinutes(3));

        process.ConsecutiveFailures.ShouldBe(0);
        process.Lifecycle.ShouldBe(ProcessLifecycle.Enabled);
        process.UpdatedAt.ShouldBe(Now.AddMinutes(3));
    }

    [Fact]
    public void MarkServiceFoundRunning_CounterZero_DoesNotTouchUpdatedAt()
    {
        var process = new ProcessBuilder().OfKind(ProcessKind.Service).CreatedAt(Now).Build();

        process.MarkServiceFoundRunning(Now.AddMinutes(1));

        process.ConsecutiveFailures.ShouldBe(0);
        process.UpdatedAt.ShouldBe(Now);
    }

    [Fact]
    public void ApplyRunOutcome_FailureReachingLimitWhileDisabled_StaysDisabledAndCounts()
    {
        var process = new ProcessBuilder().Build();
        process.ApplyRunOutcome(RunOutcome.Failed, FailureLimit, Now.AddMinutes(1));
        process.ApplyRunOutcome(RunOutcome.Failed, FailureLimit, Now.AddMinutes(2));
        process.Disable(Now.AddMinutes(3));

        process.ApplyRunOutcome(RunOutcome.Failed, FailureLimit, Now.AddMinutes(4));

        process.Lifecycle.ShouldBe(ProcessLifecycle.Disabled);
        process.ConsecutiveFailures.ShouldBe(3);
        process.RetryPending.ShouldBeFalse();
        process.UpdatedAt.ShouldBe(Now.AddMinutes(4));
        process.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void ApplyRunOutcome_FailedWhileBlocked_CountsWithoutEventOrRetry(int failureLimit)
    {
        var process = new ProcessBuilder().Build();
        for (var minute = 1; minute <= failureLimit; minute++)
        {
            process.ApplyRunOutcome(RunOutcome.Failed, failureLimit, Now.AddMinutes(minute));
        }

        process.Lifecycle.ShouldBe(ProcessLifecycle.Blocked);
        process.ClearDomainEvents();

        process.ApplyRunOutcome(RunOutcome.Failed, failureLimit, Now.AddMinutes(10));

        process.Lifecycle.ShouldBe(ProcessLifecycle.Blocked);
        process.ConsecutiveFailures.ShouldBe(1);
        process.RetryPending.ShouldBeFalse();
        process.UpdatedAt.ShouldBe(Now.AddMinutes(10));
        process.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Unblock_WithRetryPending_ClearsIt()
    {
        var process = new ProcessBuilder().Build();
        for (var minute = 1; minute <= 4; minute++)
        {
            process.ApplyRunOutcome(RunOutcome.Failed, failureLimit: 10, Now.AddMinutes(minute));
        }

        process.RetryPending.ShouldBeTrue();
        process.EnforceFailureLimit(FailureLimit, Now.AddMinutes(5));
        process.Lifecycle.ShouldBe(ProcessLifecycle.Blocked);

        process.Unblock(Now.AddMinutes(6), Calculator);

        process.Lifecycle.ShouldBe(ProcessLifecycle.Enabled);
        process.ConsecutiveFailures.ShouldBe(0);
        process.RetryPending.ShouldBeFalse();
    }

    [Fact]
    public void ApplyRunOutcome_ServiceStopped_LeavesCounterUnchanged()
    {
        var process = new ProcessBuilder().OfKind(ProcessKind.Service).Build();
        process.ApplyRunOutcome(RunOutcome.Crashed, FailureLimit, Now.AddMinutes(1));

        process.ApplyRunOutcome(RunOutcome.Stopped, FailureLimit, Now.AddMinutes(2));

        process.ConsecutiveFailures.ShouldBe(1);
        process.RetryPending.ShouldBeFalse();
        process.UpdatedAt.ShouldBe(Now.AddMinutes(1));
    }

    [Fact]
    public void ApplyRunOutcome_ServiceCrashesReachingLimit_Blocks()
    {
        var process = new ProcessBuilder().OfKind(ProcessKind.Service).Build();

        for (var minute = 1; minute <= FailureLimit; minute++)
        {
            process.ApplyRunOutcome(RunOutcome.Crashed, FailureLimit, Now.AddMinutes(minute));
        }

        process.Lifecycle.ShouldBe(ProcessLifecycle.Blocked);
        process.ConsecutiveFailures.ShouldBe(0);
        process.RetryPending.ShouldBeFalse();
        process.DomainEvents.ShouldHaveSingleItem()
            .ShouldBe(new ProcessBlocked(process.Id, FailuresReached: 3, Now.AddMinutes(3)));
    }

    [Fact]
    public void Enable_ThenEnforceFailureLimit_BlocksWhenCounterAtLimit()
    {
        var process = new ProcessBuilder().Build();
        process.Disable(Now.AddMinutes(1));
        for (var minute = 2; minute <= 4; minute++)
        {
            process.ApplyRunOutcome(RunOutcome.Failed, FailureLimit, Now.AddMinutes(minute));
        }

        process.Enable(Now.AddMinutes(5), Calculator);
        process.Lifecycle.ShouldBe(ProcessLifecycle.Enabled);

        process.EnforceFailureLimit(FailureLimit, Now.AddMinutes(6));

        process.Lifecycle.ShouldBe(ProcessLifecycle.Blocked);
        process.ConsecutiveFailures.ShouldBe(0);
        process.DomainEvents.ShouldHaveSingleItem()
            .ShouldBe(new ProcessBlocked(process.Id, FailuresReached: 3, Now.AddMinutes(6)));
    }

    [Fact]
    public void MarkServiceFoundRunning_RunToCompletion_Throws()
    {
        var process = new ProcessBuilder().Build();

        Should.Throw<DomainException>(() => process.MarkServiceFoundRunning(Now.AddMinutes(1)));
    }

    [Fact]
    public void EnforceFailureLimit_LimitLoweredBelowCounter_Blocks()
    {
        var process = new ProcessBuilder().Build();
        for (var minute = 1; minute <= 4; minute++)
        {
            process.ApplyRunOutcome(RunOutcome.Failed, failureLimit: 10, Now.AddMinutes(minute));
        }

        process.ConsecutiveFailures.ShouldBe(4);

        process.EnforceFailureLimit(FailureLimit, Now.AddMinutes(5));

        process.Lifecycle.ShouldBe(ProcessLifecycle.Blocked);
        process.ConsecutiveFailures.ShouldBe(0);
        process.RetryPending.ShouldBeFalse();
        process.UpdatedAt.ShouldBe(Now.AddMinutes(5));
        process.DomainEvents.ShouldHaveSingleItem()
            .ShouldBe(new ProcessBlocked(process.Id, FailuresReached: 4, Now.AddMinutes(5)));
    }

    [Fact]
    public void EnforceFailureLimit_BelowLimit_ChangesNothing()
    {
        var process = new ProcessBuilder().Build();
        process.ApplyRunOutcome(RunOutcome.Failed, FailureLimit, Now.AddMinutes(1));

        process.EnforceFailureLimit(FailureLimit, Now.AddMinutes(2));

        process.Lifecycle.ShouldBe(ProcessLifecycle.Enabled);
        process.ConsecutiveFailures.ShouldBe(1);
        process.UpdatedAt.ShouldBe(Now.AddMinutes(1));
        process.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void EnforceFailureLimit_WhenDisabled_ChangesNothing()
    {
        var process = new ProcessBuilder().Build();
        for (var minute = 1; minute <= 4; minute++)
        {
            process.ApplyRunOutcome(RunOutcome.Failed, failureLimit: 10, Now.AddMinutes(minute));
        }

        process.Disable(Now.AddMinutes(5));

        process.EnforceFailureLimit(FailureLimit, Now.AddMinutes(6));

        process.Lifecycle.ShouldBe(ProcessLifecycle.Disabled);
        process.ConsecutiveFailures.ShouldBe(4);
        process.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void ApplyRunOutcome_FailureLimitBelowOne_Throws()
    {
        var process = new ProcessBuilder().Build();

        Should.Throw<ArgumentOutOfRangeException>(() => process.ApplyRunOutcome(RunOutcome.Failed, 0, Now));
        Should.Throw<ArgumentOutOfRangeException>(() => process.EnforceFailureLimit(0, Now));
    }

    [Theory]
    [MemberData(nameof(AllOutcomes))]
    public void ApplyRunOutcome_AnyOutcomeOnFreshProcess_NeverMakesCounterNegative(RunOutcome outcome)
    {
        var process = new ProcessBuilder().Build();

        process.ApplyRunOutcome(outcome, FailureLimit, Now.AddMinutes(1));

        process.ConsecutiveFailures.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void ApplyRunOutcome_CancelledOnFreshProcess_DoesNotScheduleRetry()
    {
        var process = new ProcessBuilder().Build();

        process.ApplyRunOutcome(RunOutcome.Cancelled, FailureLimit, Now.AddMinutes(1));

        process.RetryPending.ShouldBeFalse();
        process.EvaluateDue(Now.AddMinutes(2), TimeSpan.FromMinutes(2), Calculator).ShouldBe(DueDecision.NotDue);
    }

    public static TheoryData<RunOutcome> AllOutcomes() => [.. Enum.GetValues<RunOutcome>()];
}
