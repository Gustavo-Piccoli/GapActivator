using GapActivator.Application.Events;
using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Settings;

namespace GapActivator.Application.Notifications;

/// <summary>Decides which events raise an alert and of what kind. Both notification handlers use it.</summary>
public static class AlertRules
{
    /// <summary>The alert kind of an event, or null when the event does not alert.</summary>
    public static AlertKind? KindOf(IDomainEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        return e switch
        {
            ProcessBlocked => AlertKind.ProcessBlocked,
            RunCompleted completed => KindOf(completed.Outcome),
            ServiceProcessCrashed => AlertKind.ServiceProcessCrashed,
            CycleFailed => AlertKind.CycleFailed,
            GapActivatorRestarted => AlertKind.ServiceRestarted,
            _ => null,
        };
    }

    // Crashed is null on purpose: ServiceProcessCrashed already alerts for it.
    private static AlertKind? KindOf(RunOutcome outcome) => outcome switch
    {
        RunOutcome.Failed or RunOutcome.FailedToStart or RunOutcome.Abandoned => AlertKind.RunFailed,
        RunOutcome.TimedOut => AlertKind.RunTimedOut,
        _ => null,
    };
}
