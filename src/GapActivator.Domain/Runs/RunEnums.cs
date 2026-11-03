namespace GapActivator.Domain.Runs;

public enum RunState
{
    Launching,
    Running,
    Stopping,
    Completed,
}

public enum RunOutcome
{
    Succeeded,
    Failed,
    FailedToStart,
    TimedOut,
    Cancelled,
    Interrupted,
    Abandoned,
    Skipped,
    Crashed,
    Stopped,
}

public enum RunTrigger
{
    Schedule,
    Retry,
    Manual,
    Restart,
    ServiceKeepAlive,
}

public enum StopReason
{
    UserCancel,
    TimeLimit,
    Shutdown,
}
