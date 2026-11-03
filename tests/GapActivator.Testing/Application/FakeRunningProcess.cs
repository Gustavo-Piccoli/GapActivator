using GapActivator.Application.Ports;

namespace GapActivator.Testing;

/// <summary>A controllable child process. Killing it completes <see cref="Exited"/> with -1.</summary>
public sealed class FakeRunningProcess(int pid, DateTimeOffset startTime) : IRunningProcess
{
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _signalCancelCount;

    public int Pid { get; } = pid;

    public DateTimeOffset StartTime { get; } = startTime;

    public Task<int> Exited => _exited.Task;

    public int SignalCancelCount => Volatile.Read(ref _signalCancelCount);

    public bool Killed { get; private set; }

    /// <summary>Makes the process exit with the given code (no effect if it already exited).</summary>
    public void Exit(int code) => _exited.TrySetResult(code);

    public void SignalCancel() => Interlocked.Increment(ref _signalCancelCount);

    public Task KillTreeAsync()
    {
        Killed = true;
        _exited.TrySetResult(-1);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
