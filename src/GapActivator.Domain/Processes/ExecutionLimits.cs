using GapActivator.Domain.Common;

namespace GapActivator.Domain.Processes;

/// <summary>Optional per-process time limit and cancellation grace period.</summary>
public sealed record ExecutionLimits
{
    public static readonly TimeSpan MaxGracePeriod = TimeSpan.FromMinutes(10);

    public ExecutionLimits(TimeSpan? timeLimit, TimeSpan? gracePeriod)
    {
        if (timeLimit is { } limit && limit <= TimeSpan.Zero)
        {
            throw new DomainException("Time limit must be greater than zero.");
        }

        if (gracePeriod is { } grace && (grace <= TimeSpan.Zero || grace > MaxGracePeriod))
        {
            throw new DomainException("Grace period must be greater than zero and at most 10 minutes.");
        }

        TimeLimit = timeLimit;
        GracePeriod = gracePeriod;
    }

    public TimeSpan? TimeLimit { get; }

    public TimeSpan? GracePeriod { get; }

    public TimeSpan? EffectiveTimeLimit(TimeSpan? installationDefault) => TimeLimit ?? installationDefault;

    public TimeSpan EffectiveGracePeriod(TimeSpan installationDefault) => GracePeriod ?? installationDefault;
}
