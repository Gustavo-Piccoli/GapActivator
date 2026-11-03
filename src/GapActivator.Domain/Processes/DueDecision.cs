using GapActivator.Domain.Runs;

namespace GapActivator.Domain.Processes;

/// <summary>Result of asking a process whether it should start a run now.</summary>
public sealed record DueDecision(bool IsDue, RunTrigger? Trigger, DateTimeOffset? ScheduledFor, bool IsLate)
{
    public static DueDecision NotDue { get; } = new(false, null, null, false);
}
