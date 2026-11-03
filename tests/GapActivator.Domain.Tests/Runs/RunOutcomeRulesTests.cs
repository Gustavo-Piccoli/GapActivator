using GapActivator.Domain.Runs;

namespace GapActivator.Domain.Tests.Runs;

public class RunOutcomeRulesTests
{
    [Theory]
    [InlineData(RunOutcome.Succeeded, FailureEffect.Reset)]
    [InlineData(RunOutcome.Failed, FailureEffect.Increment)]
    [InlineData(RunOutcome.FailedToStart, FailureEffect.Increment)]
    [InlineData(RunOutcome.TimedOut, FailureEffect.Increment)]
    [InlineData(RunOutcome.Abandoned, FailureEffect.Increment)]
    [InlineData(RunOutcome.Crashed, FailureEffect.Increment)]
    [InlineData(RunOutcome.Cancelled, FailureEffect.Unchanged)]
    [InlineData(RunOutcome.Interrupted, FailureEffect.Unchanged)]
    [InlineData(RunOutcome.Skipped, FailureEffect.Unchanged)]
    [InlineData(RunOutcome.Stopped, FailureEffect.Unchanged)]
    public void EffectOf_Outcome_ReturnsExpectedEffect(RunOutcome outcome, FailureEffect expected) =>
        RunOutcomeRules.EffectOf(outcome).ShouldBe(expected);

    [Fact]
    public void EffectOf_EveryRunOutcomeValue_ReturnsADefinedEffectWithoutThrowing()
    {
        foreach (var outcome in Enum.GetValues<RunOutcome>())
        {
            Enum.IsDefined(RunOutcomeRules.EffectOf(outcome)).ShouldBeTrue($"outcome {outcome}");
        }
    }
}
