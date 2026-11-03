using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;

namespace GapActivator.Domain.Runs;

/// <summary>One execution (or skipped execution) of a process, driven by an explicit state machine.</summary>
public sealed class Run : AggregateRoot<RunId>
{
    private Run(
        RunId id,
        ProcessId processId,
        ProcessName processNameSnapshot,
        ProcessKind kind,
        RunTrigger trigger,
        DateTimeOffset? scheduledFor,
        bool isLate,
        DateTimeOffset createdAt)
        : base(id)
    {
        ProcessId = processId;
        ProcessNameSnapshot = processNameSnapshot;
        Kind = kind;
        Trigger = trigger;
        ScheduledFor = scheduledFor;
        IsLate = isLate;
        CreatedAt = createdAt;
        State = RunState.Launching;
    }

    public ProcessId ProcessId { get; }

    public ProcessName ProcessNameSnapshot { get; }

    public ProcessKind Kind { get; }

    public RunTrigger Trigger { get; }

    public DateTimeOffset? ScheduledFor { get; }

    public bool IsLate { get; }

    public RunState State { get; private set; }

    public StopReason? StopReason { get; private set; }

    public int? Pid { get; private set; }

    public DateTimeOffset? ProcessStartTime { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public RunOutcome? Outcome { get; private set; }

    public int? ExitCode { get; private set; }

    public bool Killed { get; private set; }

    public string? Error { get; private set; }

    public static Run Begin(
        ProcessId processId,
        ProcessName processName,
        ProcessKind kind,
        RunTrigger trigger,
        DateTimeOffset? scheduledFor,
        bool isLate,
        DateTimeOffset now)
    {
        if (isLate && scheduledFor is null)
        {
            throw new DomainException("A late run must have a scheduled time.");
        }

        var run = new Run(RunId.New(), processId, processName, kind, trigger, scheduledFor, isLate, now);
        if (isLate)
        {
            run.Raise(new RunLate(run.Id, processId, scheduledFor!.Value, now));
        }

        return run;
    }

    public static Run Skip(
        ProcessId processId,
        ProcessName processName,
        ProcessKind kind,
        DateTimeOffset scheduledFor,
        DateTimeOffset now)
    {
        var run = new Run(RunId.New(), processId, processName, kind, RunTrigger.Schedule, scheduledFor, false, now);
        run.Raise(new RunSkipped(run.Id, processId, scheduledFor, now));
        run.Complete(RunOutcome.Skipped, null, false, now);
        return run;
    }

    public void MarkRunning(int pid, DateTimeOffset processStartTime, DateTimeOffset now)
    {
        RequireState("mark running", RunState.Launching);
        Pid = pid;
        ProcessStartTime = processStartTime;
        StartedAt = now;
        State = StopReason is null ? RunState.Running : RunState.Stopping;
        Raise(new RunStarted(Id, ProcessId, pid, now));
    }

    public void RequestStop(StopReason reason, DateTimeOffset now)
    {
        RequireState("request stop", RunState.Launching, RunState.Running, RunState.Stopping);
        if (reason == Runs.StopReason.TimeLimit && Kind == ProcessKind.Service)
        {
            throw new DomainException("A Service-kind run has no time limit.");
        }

        if (StopReason is null)
        {
            StopReason = reason;
            Raise(new RunStopRequested(Id, ProcessId, reason, now));
        }

        if (State == RunState.Running)
        {
            State = RunState.Stopping;
        }
    }

    public void AbortLaunch(DateTimeOffset now)
    {
        RequireState("abort launch", RunState.Launching);
        if (StopReason is not { } reason)
        {
            throw new DomainException("Cannot abort a launch that has no stop request.");
        }

        Complete(OutcomeForStop(reason), null, false, now);
    }

    public void FailToStart(string error, DateTimeOffset now)
    {
        RequireState("fail to start", RunState.Launching);
        Error = error;
        Complete(RunOutcome.FailedToStart, null, false, now);
    }

    public void Exit(int exitCode, bool killed, DateTimeOffset now)
    {
        RequireState("exit", RunState.Running, RunState.Stopping);
        Complete(ResolveOutcome(exitCode), exitCode, killed, now);
    }

    public void Abandon(DateTimeOffset now)
    {
        RequireState("abandon", RunState.Launching, RunState.Running, RunState.Stopping);
        Complete(Kind == ProcessKind.Service ? RunOutcome.Interrupted : RunOutcome.Abandoned, null, false, now);
    }

    private RunOutcome ResolveOutcome(int exitCode)
    {
        if (StopReason is { } reason)
        {
            return OutcomeForStop(reason);
        }

        return Kind == ProcessKind.Service
            ? RunOutcome.Crashed
            : exitCode == 0 ? RunOutcome.Succeeded : RunOutcome.Failed;
    }

    private RunOutcome OutcomeForStop(StopReason reason) => reason switch
    {
        Runs.StopReason.UserCancel => Kind == ProcessKind.Service ? RunOutcome.Stopped : RunOutcome.Cancelled,
        Runs.StopReason.TimeLimit => RunOutcome.TimedOut,
        _ => RunOutcome.Interrupted,
    };

    private void Complete(RunOutcome outcome, int? exitCode, bool killed, DateTimeOffset now)
    {
        State = RunState.Completed;
        Outcome = outcome;
        ExitCode = exitCode;
        Killed = killed;
        EndedAt = now;
        Raise(new RunCompleted(Id, ProcessId, outcome, exitCode, killed, now));
        if (outcome == RunOutcome.Crashed)
        {
            Raise(new ServiceProcessCrashed(Id, ProcessId, exitCode, now));
        }
    }

    private void RequireState(string action, params RunState[] allowed)
    {
        if (!allowed.Contains(State))
        {
            throw new DomainException($"Cannot {action} a run that is {State}.");
        }
    }
}
