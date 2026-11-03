using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;

namespace GapActivator.Testing;

/// <summary>Creates a <see cref="Run"/> already in a given state, with fixed synthetic values.</summary>
public sealed class RunBuilder
{
    public static readonly DateTimeOffset BaseTime = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private ProcessKind _kind = ProcessKind.RunToCompletion;
    private RunTrigger _trigger = RunTrigger.Manual;
    private RunState _state = RunState.Launching;

    public RunBuilder OfKind(ProcessKind kind)
    {
        _kind = kind;
        return this;
    }

    public RunBuilder TriggeredBy(RunTrigger trigger)
    {
        _trigger = trigger;
        return this;
    }

    public RunBuilder InState(RunState state)
    {
        _state = state;
        return this;
    }

    public Run Build()
    {
        var run = Run.Begin(
            ProcessId.New(),
            ProcessName.Create("Nightly backup"),
            _kind,
            _trigger,
            scheduledFor: null,
            isLate: false,
            BaseTime);

        switch (_state)
        {
            case RunState.Launching:
                break;
            case RunState.Running:
                run.MarkRunning(4242, BaseTime.AddSeconds(1), BaseTime.AddSeconds(1));
                break;
            case RunState.Stopping:
                run.MarkRunning(4242, BaseTime.AddSeconds(1), BaseTime.AddSeconds(1));
                run.RequestStop(StopReason.UserCancel, BaseTime.AddSeconds(2));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(_state), _state, "Build supports Launching, Running or Stopping.");
        }

        run.ClearDomainEvents();
        return run;
    }
}
