using GapActivator.Domain.Common;

namespace GapActivator.Domain.Runs;

/// <summary>What a finished run does to the process's consecutive-failure counter.</summary>
public enum FailureEffect
{
    Increment,
    Reset,
    Unchanged,
}

public static class RunOutcomeRules
{
    public static FailureEffect EffectOf(RunOutcome outcome) => outcome switch
    {
        RunOutcome.Succeeded => FailureEffect.Reset,
        RunOutcome.Failed
            or RunOutcome.FailedToStart
            or RunOutcome.TimedOut
            or RunOutcome.Abandoned
            or RunOutcome.Crashed => FailureEffect.Increment,
        RunOutcome.Cancelled
            or RunOutcome.Interrupted
            or RunOutcome.Skipped
            or RunOutcome.Stopped => FailureEffect.Unchanged,
        _ => throw new DomainException($"Unknown run outcome '{outcome}'."),
    };
}
