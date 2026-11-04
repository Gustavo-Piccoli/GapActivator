using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GapActivator.Application.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace GapActivator.Infrastructure.Processes;

/// <summary>
/// A child started (or re-adopted) by <see cref="WindowsProcessLauncher"/>. It holds the process handle, so the PID
/// cannot be reused while this object lives. Disposing it releases every handle and never kills the child.
/// </summary>
internal sealed partial class WindowsRunningProcess : IRunningProcess
{
    /// <summary>After the child exits, how long <see cref="Exited"/> waits for the rest of its output (ruling C9).</summary>
    internal static readonly TimeSpan OutputDrainAfterExit = TimeSpan.FromSeconds(1);

    /// <summary>How long disposal waits for the output pipe to reach end of file before it stops reading.</summary>
    internal static readonly TimeSpan OutputDrainOnDispose = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan UnregisterWait = TimeSpan.FromSeconds(5);

    private readonly SafeProcessHandle _process;
    private readonly SafeJobHandle? _job;
    private readonly EventWaitHandle _cancelEvent;
    private readonly RunOutputPump? _pump;
    private readonly TimeSpan _killWaitTimeout;
    private readonly ILogger _logger;
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ProcessWaitHandle _exitWaitHandle;
    private readonly RegisteredWaitHandle _exitWait;
    private readonly Lock _gate = new();
    private bool _disposed;

    /// <param name="pid">The child's process id.</param>
    /// <param name="startTime">The child's creation time.</param>
    /// <param name="process">Owned; opened with at least SYNCHRONIZE and PROCESS_QUERY_LIMITED_INFORMATION.</param>
    /// <param name="job">Owned; null when the child is in no GapActivator job, and the kill then walks the process tree.</param>
    /// <param name="cancelEvent">Owned.</param>
    /// <param name="pump">Owned; null when the output is not captured.</param>
    /// <param name="killWaitTimeout">How long a tree kill waits for the exit.</param>
    /// <param name="logger">Diagnostics.</param>
    public WindowsRunningProcess(
        int pid,
        DateTimeOffset startTime,
        SafeProcessHandle process,
        SafeJobHandle? job,
        EventWaitHandle cancelEvent,
        RunOutputPump? pump,
        TimeSpan killWaitTimeout,
        ILogger logger)
    {
        Pid = pid;
        StartTime = startTime;
        _process = process;
        _job = job;
        _cancelEvent = cancelEvent;
        _pump = pump;
        _killWaitTimeout = killWaitTimeout;
        _logger = logger;
        _exitWaitHandle = new ProcessWaitHandle(process);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(
            _exitWaitHandle, static (state, _) => ((WindowsRunningProcess)state!).OnExited(), this, Timeout.Infinite,
            executeOnlyOnce: true);
    }

    public int Pid { get; }

    public DateTimeOffset StartTime { get; }

    /// <summary>The exit code; set once the child exited and its output drained, at most one second after the exit.</summary>
    public Task<int> Exited => _exited.Task;

    /// <summary>Completes when the output pump stopped reading; already complete when the output is not captured.</summary>
    internal Task OutputDrained => _pump?.Drained ?? Task.CompletedTask;

    /// <summary>Whether the tree kill goes through a job object rather than the process tree walk.</summary>
    internal bool HasJob => _job is not null;

    public void SignalCancel()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _cancelEvent.Set();
            }
        }
    }

    /// <exception cref="TimeoutException">The child did not exit within the kill wait timeout.</exception>
    public async Task KillTreeAsync()
    {
        if (_exited.Task.IsCompleted)
        {
            return;
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_job is null)
            {
                KillTreeWithoutJob();
            }
            else if (!NativeMethods.TerminateJobObject(_job, uint.MaxValue))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }

        await _exited.Task.WaitAsync(_killWaitTimeout).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        bool unregistered = Unregister();
        if (_pump is not null)
        {
            await _pump.DisposeAsync(OutputDrainOnDispose).ConfigureAwait(false);
        }

        _cancelEvent.Dispose();
        _job?.Dispose();
        _exitWaitHandle.Dispose();
        if (unregistered)
        {
            _process.Dispose();
        }
    }

    /// <summary>
    /// Removes the exit wait and waits for a callback in progress, so the process handle is never closed under the wait.
    /// Returns false when that did not finish in time; the process handle is then left open rather than risked.
    /// </summary>
    private bool Unregister()
    {
        using var done = new ManualResetEvent(false);
        if (_exitWait.Unregister(done) && !done.WaitOne(UnregisterWait))
        {
            LogUnregisterTimedOut(Pid);
            return false;
        }

        return true;
    }

    private void KillTreeWithoutJob()
    {
        try
        {
            using Process process = Process.GetProcessById(Pid);
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // It exited in the meantime; Exited completes from the process handle.
        }
    }

    private void OnExited()
    {
        int exitCode;
        try
        {
            if (!NativeMethods.GetExitCodeProcess(_process, out uint code))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            exitCode = unchecked((int)code);
        }
        catch (Exception ex) when (ex is Win32Exception or ObjectDisposedException)
        {
            _exited.TrySetException(ex);
            return;
        }

        _ = CompleteAfterOutputAsync(exitCode);
    }

    private async Task CompleteAfterOutputAsync(int exitCode)
    {
        if (_pump is not null)
        {
            try
            {
                await _pump.Drained.WaitAsync(OutputDrainAfterExit).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // A descendant still holds the output pipe: the run ends when the child exits, not when the pipe closes.
            }
        }

        _exited.TrySetResult(exitCode);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The exit wait of process {Pid} did not unregister in time; its handle is left open.")]
    private partial void LogUnregisterTimedOut(int pid);

    /// <summary>Lets the thread pool wait on the process handle without owning it.</summary>
    private sealed class ProcessWaitHandle : WaitHandle
    {
        public ProcessWaitHandle(SafeProcessHandle process) =>
            SafeWaitHandle = new SafeWaitHandle(process.DangerousGetHandle(), ownsHandle: false);
    }
}
