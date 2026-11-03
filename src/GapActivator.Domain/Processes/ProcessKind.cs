namespace GapActivator.Domain.Processes;

/// <summary>How a process is expected to behave once launched.</summary>
public enum ProcessKind
{
    /// <summary>Runs, finishes, and reports an exit code.</summary>
    RunToCompletion,

    /// <summary>Is expected to keep running until stopped.</summary>
    Service,
}
