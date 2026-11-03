using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Testing;

namespace GapActivator.Domain.Tests.Runs;

public class RunStateMachineTests
{
    private static readonly DateTimeOffset T0 = RunBuilder.BaseTime;
    private static readonly DateTimeOffset T1 = T0.AddMinutes(5);
    private static readonly ProcessName Name = ProcessName.Create("Nightly backup");

    private static Run Build(RunState state, ProcessKind kind = ProcessKind.RunToCompletion) =>
        new RunBuilder().OfKind(kind).InState(state).Build();

    [Fact]
    public void Begin_NotLate_IsLaunchingWithoutEvents()
    {
        var run = Run.Begin(ProcessId.New(), Name, ProcessKind.RunToCompletion, RunTrigger.Manual, null, false, T0);

        run.State.ShouldBe(RunState.Launching);
        run.CreatedAt.ShouldBe(T0);
        run.Outcome.ShouldBeNull();
        run.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Begin_Late_RaisesRunLate()
    {
        var scheduled = T0.AddMinutes(-10);
        var run = Run.Begin(ProcessId.New(), Name, ProcessKind.RunToCompletion, RunTrigger.Schedule, scheduled, true, T0);

        run.IsLate.ShouldBeTrue();
        var e = run.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<RunLate>();
        e.ScheduledFor.ShouldBe(scheduled);
        e.OccurredAt.ShouldBe(T0);
    }

    [Fact]
    public void Begin_LateWithoutScheduledTime_Throws() =>
        Should.Throw<DomainException>(() =>
            Run.Begin(ProcessId.New(), Name, ProcessKind.RunToCompletion, RunTrigger.Schedule, null, true, T0));

    [Fact]
    public void Skip_IsCompletedImmediately_WithSkippedOutcome()
    {
        var run = Run.Skip(ProcessId.New(), Name, ProcessKind.RunToCompletion, T0, T1);

        run.State.ShouldBe(RunState.Completed);
        run.Outcome.ShouldBe(RunOutcome.Skipped);
        run.EndedAt.ShouldBe(T1);
        run.DomainEvents.OfType<RunSkipped>().ShouldHaveSingleItem();
        run.DomainEvents.OfType<RunCompleted>().ShouldHaveSingleItem().Outcome.ShouldBe(RunOutcome.Skipped);
    }

    [Fact]
    public void MarkRunning_FromLaunching_GoesToRunning()
    {
        var run = Build(RunState.Launching);

        run.MarkRunning(77, T0, T1);

        run.State.ShouldBe(RunState.Running);
        run.Pid.ShouldBe(77);
        run.ProcessStartTime.ShouldBe(T0);
        run.StartedAt.ShouldBe(T1);
        run.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<RunStarted>().Pid.ShouldBe(77);
    }

    [Fact]
    public void MarkRunning_Twice_Throws()
    {
        var run = Build(RunState.Running);

        Should.Throw<DomainException>(() => run.MarkRunning(1, T0, T1));
    }

    [Fact]
    public void RequestStop_DuringLaunching_ThenMarkRunning_GoesToStopping()
    {
        var run = Build(RunState.Launching);

        run.RequestStop(StopReason.UserCancel, T0);
        run.State.ShouldBe(RunState.Launching);
        run.StopReason.ShouldBe(StopReason.UserCancel);

        run.MarkRunning(5, T0, T1);
        run.State.ShouldBe(RunState.Stopping);
    }

    [Fact]
    public void RequestStop_WhileRunning_GoesToStopping()
    {
        var run = Build(RunState.Running);

        run.RequestStop(StopReason.TimeLimit, T1);

        run.State.ShouldBe(RunState.Stopping);
        run.StopReason.ShouldBe(StopReason.TimeLimit);
    }

    [Fact]
    public void RequestStop_Twice_KeepsFirstReason()
    {
        var run = Build(RunState.Running);
        run.RequestStop(StopReason.TimeLimit, T0);

        run.RequestStop(StopReason.UserCancel, T1);
        run.Exit(1, true, T1);

        run.StopReason.ShouldBe(StopReason.TimeLimit);
        run.Outcome.ShouldBe(RunOutcome.TimedOut);
    }

    [Fact]
    public void RequestStop_WhenCompleted_Throws()
    {
        var run = Build(RunState.Running);
        run.Exit(0, false, T1);

        Should.Throw<DomainException>(() => run.RequestStop(StopReason.UserCancel, T1));
    }

    [Fact]
    public void AbortLaunch_WithStopRequested_CompletesWithMappedOutcome()
    {
        var run = Build(RunState.Launching);
        run.RequestStop(StopReason.UserCancel, T0);

        run.AbortLaunch(T1);

        run.State.ShouldBe(RunState.Completed);
        run.Outcome.ShouldBe(RunOutcome.Cancelled);
        run.EndedAt.ShouldBe(T1);
        run.ExitCode.ShouldBeNull();
    }

    [Fact]
    public void AbortLaunch_WithoutStopRequest_Throws()
    {
        var run = Build(RunState.Launching);

        Should.Throw<DomainException>(() => run.AbortLaunch(T1));
    }

    [Fact]
    public void AbortLaunch_WhenRunning_Throws()
    {
        var run = Build(RunState.Running);

        Should.Throw<DomainException>(() => run.AbortLaunch(T1));
    }

    [Fact]
    public void FailToStart_FromLaunching_CompletesWithError()
    {
        var run = Build(RunState.Launching);

        run.FailToStart("File not found", T1);

        run.State.ShouldBe(RunState.Completed);
        run.Outcome.ShouldBe(RunOutcome.FailedToStart);
        run.Error.ShouldBe("File not found");
        run.EndedAt.ShouldBe(T1);
        run.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<RunCompleted>();
    }

    [Fact]
    public void FailToStart_WhenRunning_Throws()
    {
        var run = Build(RunState.Running);

        Should.Throw<DomainException>(() => run.FailToStart("x", T1));
    }

    [Fact]
    public void Exit_WhenLaunching_Throws()
    {
        var run = Build(RunState.Launching);

        Should.Throw<DomainException>(() => run.Exit(0, false, T1));
    }

    [Fact]
    public void Exit_WhenCompleted_Throws()
    {
        var run = Build(RunState.Running);
        run.Exit(0, false, T1);

        Should.Throw<DomainException>(() => run.Exit(0, false, T1));
    }

    [Fact]
    public void Exit_RunToCompletionExitZero_IsSucceeded()
    {
        var run = Build(RunState.Running);

        run.Exit(0, false, T1);

        run.State.ShouldBe(RunState.Completed);
        run.Outcome.ShouldBe(RunOutcome.Succeeded);
        run.ExitCode.ShouldBe(0);
        run.Killed.ShouldBeFalse();
        run.EndedAt.ShouldBe(T1);
        run.StartedAt.ShouldBe(T0.AddSeconds(1));
        var e = run.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<RunCompleted>();
        e.Outcome.ShouldBe(RunOutcome.Succeeded);
        e.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Exit_RunToCompletionNonZero_IsFailed()
    {
        var run = Build(RunState.Running);

        run.Exit(3, false, T1);

        run.Outcome.ShouldBe(RunOutcome.Failed);
        run.ExitCode.ShouldBe(3);
    }

    [Theory]
    [InlineData(StopReason.UserCancel, ProcessKind.RunToCompletion, RunOutcome.Cancelled)]
    [InlineData(StopReason.UserCancel, ProcessKind.Service, RunOutcome.Stopped)]
    [InlineData(StopReason.TimeLimit, ProcessKind.RunToCompletion, RunOutcome.TimedOut)]
    [InlineData(StopReason.TimeLimit, ProcessKind.Service, RunOutcome.TimedOut)]
    [InlineData(StopReason.Shutdown, ProcessKind.RunToCompletion, RunOutcome.Interrupted)]
    [InlineData(StopReason.Shutdown, ProcessKind.Service, RunOutcome.Interrupted)]
    public void Exit_AfterStopRequest_MapsOutcomeFromReason(StopReason reason, ProcessKind kind, RunOutcome expected)
    {
        var run = Build(RunState.Running, kind);
        run.RequestStop(reason, T0);

        run.Exit(0, true, T1);

        run.Outcome.ShouldBe(expected);
        run.Killed.ShouldBeTrue();
    }

    [Fact]
    public void Exit_ServiceKindExitCodeZero_IsCrashed()
    {
        var run = Build(RunState.Running, ProcessKind.Service);

        run.Exit(0, false, T1);

        run.Outcome.ShouldBe(RunOutcome.Crashed);
        run.DomainEvents.OfType<RunCompleted>().ShouldHaveSingleItem().Outcome.ShouldBe(RunOutcome.Crashed);
        run.DomainEvents.OfType<ServiceProcessCrashed>().ShouldHaveSingleItem().ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Exit_ServiceKindNonZero_IsCrashed()
    {
        var run = Build(RunState.Running, ProcessKind.Service);

        run.Exit(9, false, T1);

        run.Outcome.ShouldBe(RunOutcome.Crashed);
    }

    [Fact]
    public void Abandon_ServiceKind_IsInterrupted()
    {
        var run = Build(RunState.Running, ProcessKind.Service);

        run.Abandon(T1);

        run.State.ShouldBe(RunState.Completed);
        run.Outcome.ShouldBe(RunOutcome.Interrupted);
        run.EndedAt.ShouldBe(T1);
    }

    [Fact]
    public void Abandon_RunToCompletion_IsAbandoned()
    {
        var run = Build(RunState.Stopping);

        run.Abandon(T1);

        run.Outcome.ShouldBe(RunOutcome.Abandoned);
        run.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<RunCompleted>();
    }

    [Fact]
    public void Abandon_WhenLaunching_Completes()
    {
        var run = Build(RunState.Launching);

        run.Abandon(T1);

        run.State.ShouldBe(RunState.Completed);
    }

    [Fact]
    public void Abandon_WhenCompleted_Throws()
    {
        var run = Build(RunState.Running);
        run.Exit(0, false, T1);

        Should.Throw<DomainException>(() => run.Abandon(T1));
    }

    [Fact]
    public void Begin_RecordsProcessNameSnapshotKindAndTrigger()
    {
        var processId = ProcessId.New();

        var run = Run.Begin(processId, Name, ProcessKind.Service, RunTrigger.ServiceKeepAlive, null, false, T0);

        run.ProcessId.ShouldBe(processId);
        run.ProcessNameSnapshot.ShouldBe(Name);
        run.Kind.ShouldBe(ProcessKind.Service);
        run.Trigger.ShouldBe(RunTrigger.ServiceKeepAlive);
        run.StartedAt.ShouldBeNull();
        run.EndedAt.ShouldBeNull();
    }
}
