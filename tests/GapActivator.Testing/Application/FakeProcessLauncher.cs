using GapActivator.Application.Ports;
using GapActivator.Domain.Runs;

namespace GapActivator.Testing;

/// <summary>
/// Launch behaviours are queued per executable path (case-insensitive) and consumed in order.
/// When nothing is queued for a path, the launch succeeds with a generated PID.
/// </summary>
public sealed class FakeProcessLauncher(TimeProvider time) : IProcessLauncher
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Queue<Func<CancellationToken, Task<IRunningProcess>>>> _behaviours =
        new(StringComparer.OrdinalIgnoreCase);
    private int _nextPid = 1000;

    public List<LaunchRequest> LaunchRequests { get; } = [];

    /// <summary>Every process handed out by <see cref="LaunchAsync"/>, in launch order.</summary>
    public List<FakeRunningProcess> Launched { get; } = [];

    public List<(RunId RunId, int Pid, DateTimeOffset StartTime)> AttachCalls { get; } = [];

    /// <summary>What <see cref="TryAttach"/> returns; null means no matching live process.</summary>
    public IRunningProcess? AttachResult { get; set; }

    /// <summary>Queues a successful launch and returns the process it will hand out.</summary>
    public FakeRunningProcess QueueSucceed(string executable, int? pid = null, DateTimeOffset? startTime = null)
    {
        var process = new FakeRunningProcess(pid ?? NextPid(), startTime ?? time.GetUtcNow());
        Enqueue(executable, _ =>
        {
            lock (_gate)
            {
                Launched.Add(process);
            }

            return Task.FromResult<IRunningProcess>(process);
        });
        return process;
    }

    /// <summary>Queues a launch that fails with <see cref="LaunchFailedException"/>.</summary>
    public void QueueFail(string executable, string message = "Simulated launch failure.") =>
        Enqueue(executable, _ => throw new LaunchFailedException(message));

    /// <summary>Queues a launch that blocks until its token is cancelled, then throws <see cref="OperationCanceledException"/>.</summary>
    public void QueueBlockUntilCancelled(string executable) =>
        Enqueue(executable, async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            throw new InvalidOperationException("Unreachable: an infinite delay only ends by cancellation.");
        });

    public Task<IRunningProcess> LaunchAsync(LaunchRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        Func<CancellationToken, Task<IRunningProcess>> behaviour;
        lock (_gate)
        {
            LaunchRequests.Add(request);
            if (_behaviours.TryGetValue(request.Executable, out var queue) && queue.Count > 0)
            {
                behaviour = queue.Dequeue();
            }
            else
            {
                var process = new FakeRunningProcess(NextPidLocked(), time.GetUtcNow());
                Launched.Add(process);
                behaviour = _ => Task.FromResult<IRunningProcess>(process);
            }
        }

        ct.ThrowIfCancellationRequested();
        return behaviour(ct);
    }

    public IRunningProcess? TryAttach(RunId runId, int pid, DateTimeOffset processStartTime)
    {
        lock (_gate)
        {
            AttachCalls.Add((runId, pid, processStartTime));
        }

        return AttachResult;
    }

    private int NextPid()
    {
        lock (_gate)
        {
            return NextPidLocked();
        }
    }

    private int NextPidLocked() => _nextPid++;

    private void Enqueue(string executable, Func<CancellationToken, Task<IRunningProcess>> behaviour)
    {
        lock (_gate)
        {
            if (!_behaviours.TryGetValue(executable, out var queue))
            {
                queue = new Queue<Func<CancellationToken, Task<IRunningProcess>>>();
                _behaviours[executable] = queue;
            }

            queue.Enqueue(behaviour);
        }
    }
}
