namespace GapActivator.Domain.Processes;

/// <summary>Whether a process may be launched.</summary>
public enum ProcessLifecycle
{
    /// <summary>Runs on its schedules (or stays up, for Service kind).</summary>
    Enabled,

    /// <summary>Paused manually; no new runs until enabled again.</summary>
    Disabled,

    /// <summary>The failure limit was reached; no new runs until unblocked manually.</summary>
    Blocked,
}
