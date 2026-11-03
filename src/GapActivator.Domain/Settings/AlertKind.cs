namespace GapActivator.Domain.Settings;

/// <summary>Events that can trigger an e-mail alert.</summary>
public enum AlertKind
{
    ProcessBlocked,
    RunFailed,
    RunTimedOut,
    ServiceProcessCrashed,
    CycleFailed,
    ServiceRestarted,
}
