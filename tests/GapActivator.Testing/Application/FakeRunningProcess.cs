using GapActivator.Application.Ports;

namespace GapActivator.Testing;

/// <summary>A controllable child process. Killing it completes <see cref="Exited"/> with -1.</summary>
public sealed class FakeRunningProcess(int pid, DateTimeOffset startTime) : IRunningProcess
{
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _signalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _killAttempted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _signalCancelCount;
    private int _killAttempts;
    private int _disposeCount;

    public int Pid { get; } = pid;

    public DateTimeOffset StartTime { get; } = startTime;

    public Task<int> Exited => _exited.Task;

    public int SignalCancelCount => Volatile.Read(ref _signalCancelCount);

    /// <summary>Completes on the first call of <see cref="SignalCancel"/>.</summary>
    public Task Signalled => _signalled.Task;

    public int KillAttempts => Volatile.Read(ref _killAttempts);

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    public bool Killed { get; private set; }

    /// <summary>Completes on the first call of <see cref="KillTreeAsync"/>, even when <see cref="KillFailure"/> makes it fail.</summary>
    public Task KillAttempted => _killAttempted.Task;

    /// <summary>When set, <see cref="SignalCancel"/> counts the call and then throws this.</summary>
    public Exception? SignalFailure { get; set; }

    /// <summary>When set, <see cref="KillTreeAsync"/> throws this and the process keeps running.</summary>
    public Exception? KillFailure { get; set; }

    /// <summary>When set, a kill failing with <see cref="KillFailure"/> happens as the child exits with this code.</summary>
    public int? ExitCodeOnFailedKill { get; set; }

    /// <summary>When set, <see cref="DisposeAsync"/> counts the call and then throws this.</summary>
    public Exception? DisposeFailure { get; set; }

    /// <summary>Makes the process exit with the given code (no effect if it already exited).</summary>
    public void Exit(int code) => _exited.TrySetResult(code);

    /// <summary>Faults <see cref="Exited"/>, which simulates a bug in a real process handle.</summary>
    public void FailExited(Exception error) => _exited.TrySetException(error);

    public void SignalCancel()
    {
        Interlocked.Increment(ref _signalCancelCount);
        _signalled.TrySetResult();
        if (SignalFailure is not null)
        {
            throw SignalFailure;
        }
    }

    public Task KillTreeAsync()
    {
        Interlocked.Increment(ref _killAttempts);
        _killAttempted.TrySetResult();
        if (KillFailure is not null)
        {
            if (ExitCodeOnFailedKill is { } code)
            {
                Exit(code);
            }

            return Task.FromException(KillFailure);
        }

        Killed = true;
        _exited.TrySetResult(-1);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposeCount);
        return DisposeFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(DisposeFailure);
    }
}
