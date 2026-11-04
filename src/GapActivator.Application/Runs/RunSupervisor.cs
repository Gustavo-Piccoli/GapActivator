using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using Microsoft.Extensions.Logging;

namespace GapActivator.Application.Runs;

/// <summary>
/// Singleton: owns the launched children. Each tracked run has one background task, the only code that touches its
/// process handle: launch, record the launch, wait for exit, stop or time limit, signal, wait for the grace period,
/// kill, record the end, remove the entry. That task never throws: what it cannot handle is logged at Error, and
/// the entry is always removed, so a run whose end could not be recorded stays active for startup reconciliation.
/// Every command is sent as <see cref="Caller.System"/> with no cancellation, so a shutdown cannot lose a completion,
/// and is retried on failure (see <see cref="SendAsync"/>). Nothing here holds a lock across an await.
/// </summary>
public sealed partial class RunSupervisor(
    ICommandDispatcher dispatcher,
    IProcessLauncher launcher,
    TimeProvider time,
    ILogger<RunSupervisor> logger)
{
    private const int MaxRetries = 3;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>Timers cannot wait much longer than 49 days, so a long time limit is waited for in pieces.</summary>
    private static readonly TimeSpan MaxTimerDelay = TimeSpan.FromDays(1);

    private readonly Lock _sync = new();
    private readonly Dictionary<RunId, TrackedRun> _byRun = [];
    private readonly Dictionary<ProcessId, TrackedRun> _byProcess = [];

    public IReadOnlyCollection<RunId> TrackedRuns
    {
        get
        {
            lock (_sync)
            {
                return [.. _byRun.Keys];
            }
        }
    }

    public bool IsTracking(ProcessId processId)
    {
        lock (_sync)
        {
            return _byProcess.ContainsKey(processId);
        }
    }

    /// <summary>
    /// Launches and supervises a committed run. A ticket for a process that already has an entry is ignored and logged
    /// (BI-041), unless that entry's child is gone and its end is being recorded: the end may already be committed, which
    /// is what allowed this run to begin, so this ticket waits for the entry to go away instead of being lost.
    /// </summary>
    public void Start(RunStartTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        TrackedRun? run = null;
        TrackedRun? ending = null;
        lock (_sync)
        {
            if (!_byProcess.TryGetValue(ticket.ProcessId, out TrackedRun? current))
            {
                run = Track(ticket.RunId, ticket.ProcessId, ticket.GracePeriod);
            }
            else if (current.IsEnding)
            {
                ending = current;
            }
        }

        if (run is not null)
        {
            _ = Task.Run(() => LaunchAndSuperviseAsync(run, ticket));
        }
        else if (ending is not null)
        {
            _ = StartAfterAsync(ending, ticket);
        }
        else
        {
            LogAlreadyTracked(ticket.ProcessId, ticket.RunId);
        }
    }

    /// <summary>
    /// Supervises a child that outlived the previous instance. The supervisor takes ownership of the handle.
    /// Zero or less <paramref name="remainingTimeLimit"/> stops the run at once; <paramref name="stopping"/> signals the
    /// child once on adoption and kills it after the grace period.
    /// </summary>
    public void Adopt(
        RunId runId,
        ProcessId processId,
        IRunningProcess process,
        TimeSpan? remainingTimeLimit,
        TimeSpan gracePeriod,
        bool stopping)
    {
        ArgumentNullException.ThrowIfNull(process);

        TrackedRun? run = null;
        lock (_sync)
        {
            if (!_byProcess.ContainsKey(processId))
            {
                run = Track(runId, processId, gracePeriod);
            }
        }

        if (run is null)
        {
            LogAlreadyTracked(processId, runId);
            _ = DisposeIgnoredAsync(process, runId);
            return;
        }

        if (stopping)
        {
            run.RequestStop();
        }

        _ = Task.Run(() => SuperviseAdoptedAsync(run, process, remainingTimeLimit));
    }

    /// <summary>
    /// Aborts the launch or signals the child, then kills it after the ticket's grace period. A no-op for an untracked
    /// run, and the first call is the only one that counts: another does not signal again or restart the grace period.
    /// </summary>
    public void Stop(RunId runId)
    {
        TrackedRun? run;
        lock (_sync)
        {
            _byRun.TryGetValue(runId, out run);
        }

        if (run is not null && run.RequestStop())
        {
            _ = CancelLaunchAsync(run);
        }
    }

    /// <summary>
    /// Sends <see cref="StartPendingRun"/> once the end of the run was recorded, or at once when the run is no longer
    /// tracked, because the exit can win the race. The entry's removal and this check share the lock, so exactly one of
    /// the two paths sends it.
    /// </summary>
    public void StartAfterEnd(RunId runId, ProcessId processId)
    {
        lock (_sync)
        {
            if (_byRun.TryGetValue(runId, out TrackedRun? run))
            {
                run.RestartAfterEnd = true;
                return;
            }
        }

        _ = Task.Run(() => SendAsync(new StartPendingRun(processId), processId.ToString()));
    }

    /// <summary>
    /// Completes after the end was recorded (or could not be) and the entry was removed, or at once if the run is not
    /// tracked. A pending restart is sent after this completes, so <see cref="IsTracking"/> is false at that moment.
    /// </summary>
    public Task WaitForEndAsync(RunId runId, CancellationToken ct)
    {
        TrackedRun? run;
        lock (_sync)
        {
            _byRun.TryGetValue(runId, out run);
        }

        return run is null ? Task.CompletedTask : run.Ended.WaitAsync(ct);
    }

    /// <summary>Must be called holding <see cref="_sync"/>.</summary>
    private TrackedRun Track(RunId runId, ProcessId processId, TimeSpan gracePeriod)
    {
        var run = new TrackedRun(runId, processId, gracePeriod);
        _byRun.Add(runId, run);
        _byProcess.Add(processId, run);
        return run;
    }

    private async Task StartAfterAsync(TrackedRun previous, RunStartTicket ticket)
    {
        await previous.Ended;
        Start(ticket);
    }

    private async Task LaunchAndSuperviseAsync(TrackedRun run, RunStartTicket ticket)
    {
        try
        {
            IRunningProcess? process = await LaunchAsync(run, ticket.Request);
            if (process is not null)
            {
                await using (process)
                {
                    bool launchRecorded = await SendAsync(
                        new RecordRunLaunched(run.RunId, process.Pid, process.StartTime), run.RunId.ToString());
                    await SuperviseAsync(run, process, ticket.TimeLimit, launchRecorded);
                }
            }
        }
        catch (Exception ex)
        {
            LogRunTaskFailed(ex, run.RunId);
        }
        finally
        {
            await FinishAsync(run);
        }
    }

    private async Task SuperviseAdoptedAsync(TrackedRun run, IRunningProcess process, TimeSpan? remainingTimeLimit)
    {
        try
        {
            await using (process)
            {
                await SuperviseAsync(run, process, remainingTimeLimit, launchRecorded: true);
            }
        }
        catch (Exception ex)
        {
            LogRunTaskFailed(ex, run.RunId);
        }
        finally
        {
            await FinishAsync(run);
        }
    }

    /// <summary>Returns the process, or null after recording why there is none.</summary>
    private async Task<IRunningProcess?> LaunchAsync(TrackedRun run, LaunchRequest request)
    {
        try
        {
            return await launcher.LaunchAsync(request, run.LaunchToken);
        }
        catch (LaunchFailedException ex)
        {
            MarkEnding(run);
            await SendAsync(new RecordRunLaunchFailed(run.RunId, ex.Message), run.RunId.ToString());
        }
        catch (OperationCanceledException) when (run.StopRequested.IsCompleted)
        {
            MarkEnding(run);
            await SendAsync(new RecordLaunchAborted(run.RunId), run.RunId.ToString());
        }
        catch (Exception ex)
        {
            LogLaunchThrew(ex, run.RunId);
            MarkEnding(run);
            await SendAsync(new RecordRunLaunchFailed(run.RunId, ex.Message), run.RunId.ToString());
        }

        return null;
    }

    private async Task SuperviseAsync(TrackedRun run, IRunningProcess process, TimeSpan? timeLimit, bool launchRecorded)
    {
        Task<int> exited = process.Exited;

        await WaitForExitStopOrTimeLimitAsync(run, exited, timeLimit);

        bool killed = !exited.IsCompleted && await StopChildAsync(run, process, exited);
        int exitCode = await exited;

        MarkEnding(run);
        if (!launchRecorded)
        {
            // An exit cannot be recorded on a run still Launching, and the command carries no PID. A last attempt here
            // lets a run outlive a short outage; if it fails too, the exit is rejected and logged, and the run stays
            // active for startup reconciliation.
            await SendAsync(new RecordRunLaunched(run.RunId, process.Pid, process.StartTime), run.RunId.ToString());
        }

        await SendAsync(new RecordRunExited(run.RunId, exitCode, killed), run.RunId.ToString());
    }

    private async Task WaitForExitStopOrTimeLimitAsync(TrackedRun run, Task<int> exited, TimeSpan? timeLimit)
    {
        if (timeLimit is not { } limit)
        {
            await Task.WhenAny(exited, run.StopRequested);
            return;
        }

        // The deadline is fixed once: every wait below is measured against it, so one large clock move still fires.
        DateTimeOffset now = time.GetUtcNow();
        DateTimeOffset deadline = limit < DateTimeOffset.MaxValue - now ? now + limit : DateTimeOffset.MaxValue;

        using var cancellation = new CancellationTokenSource();
        await Task.WhenAny(exited, run.StopRequested, WaitUntilAsync(deadline, cancellation.Token));
        await cancellation.CancelAsync();

        if (!exited.IsCompleted && !run.StopRequested.IsCompleted)
        {
            // Even if this cannot be recorded the limit still holds: the caller stops any child that has not exited.
            await SendAsync(new StopRun(run.RunId, StopReason.TimeLimit), run.RunId.ToString());
        }
    }

    private async Task WaitUntilAsync(DateTimeOffset deadline, CancellationToken ct)
    {
        try
        {
            for (TimeSpan left = deadline - time.GetUtcNow(); left > TimeSpan.Zero; left = deadline - time.GetUtcNow())
            {
                // Rounded up: a delay under one millisecond would create no timer and this would spin.
                TimeSpan delay = TimeSpan.FromMilliseconds(Math.Ceiling(left.TotalMilliseconds));
                await Task.Delay(delay < MaxTimerDelay ? delay : MaxTimerDelay, time, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // The child exited or the run was stopped first.
        }
    }

    /// <summary>Signals the child and kills it if the grace period ends first. Returns whether it was killed.</summary>
    private async Task<bool> StopChildAsync(TrackedRun run, IRunningProcess process, Task<int> exited)
    {
        try
        {
            process.SignalCancel();
        }
        catch (Exception ex)
        {
            LogSignalFailed(ex, run.RunId);
        }

        using var cancellation = new CancellationTokenSource();
        await Task.WhenAny(exited, Task.Delay(run.GracePeriod, time, cancellation.Token));
        await cancellation.CancelAsync();
        if (exited.IsCompleted)
        {
            return false;
        }

        try
        {
            await process.KillTreeAsync();
            return true;
        }
        catch (Exception ex)
        {
            // The child may still be alive, so the entry stays and keeps a new run from overlapping it.
            LogKillFailed(ex, run.RunId);
            return false;
        }
    }

    private void MarkEnding(TrackedRun run)
    {
        lock (_sync)
        {
            run.IsEnding = true;
        }
    }

    private async Task FinishAsync(TrackedRun run)
    {
        bool restart;
        lock (_sync)
        {
            _byRun.Remove(run.RunId);
            _byProcess.Remove(run.ProcessId);
            restart = run.RestartAfterEnd;
        }

        run.Dispose();
        run.MarkEnded();

        if (restart)
        {
            await SendAsync(new StartPendingRun(run.ProcessId), run.ProcessId.ToString());
        }
    }

    private async Task CancelLaunchAsync(TrackedRun run)
    {
        try
        {
            await run.CancelLaunchAsync();
        }
        catch (ObjectDisposedException)
        {
            // The run ended at the same moment, so there is no launch left to abort.
        }
        catch (Exception ex)
        {
            LogCancelFailed(ex, run.RunId);
        }
    }

    private async Task DisposeIgnoredAsync(IRunningProcess process, RunId runId)
    {
        try
        {
            await process.DisposeAsync();
        }
        catch (Exception ex)
        {
            LogRunTaskFailed(ex, runId);
        }
    }

    /// <summary>
    /// Sends a command, retrying a failure 3 times, 1 second apart, then logs an error and returns false. A rule
    /// violation will not heal, so it is logged without a retry. Never throws.
    /// </summary>
    private async Task<bool> SendAsync<TCommand>(TCommand command, string subject)
        where TCommand : ICommand<Unit>
    {
        string name = typeof(TCommand).Name;
        for (int retriesLeft = MaxRetries; ; retriesLeft--)
        {
            try
            {
                await dispatcher.SendAsync<TCommand, Unit>(command, Caller.System, CancellationToken.None);
                return true;
            }
            catch (Exception ex) when (ex is CommandRejectedException or DomainException)
            {
                LogRuleViolation(ex, name, subject);
                return false;
            }
            catch (Exception ex) when (retriesLeft > 0)
            {
                LogRetrying(ex, name, subject, retriesLeft);
            }
            catch (Exception ex)
            {
                LogGaveUp(ex, name, subject);
                return false;
            }

            await Task.Delay(RetryDelay, time);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Process {ProcessId} is already tracked; run {RunId} was ignored.")]
    private partial void LogAlreadyTracked(ProcessId processId, RunId runId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The launcher failed unexpectedly for run {RunId}.")]
    private partial void LogLaunchThrew(Exception exception, RunId runId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The supervision of run {RunId} failed.")]
    private partial void LogRunTaskFailed(Exception exception, RunId runId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Signalling the child of run {RunId} failed; it will be killed after the grace period.")]
    private partial void LogSignalFailed(Exception exception, RunId runId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Killing the child of run {RunId} failed; waiting for it to exit.")]
    private partial void LogKillFailed(Exception exception, RunId runId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Aborting the launch of run {RunId} failed.")]
    private partial void LogCancelFailed(Exception exception, RunId runId);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Command} for {Subject} was refused and will not be retried.")]
    private partial void LogRuleViolation(Exception exception, string command, string subject);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Command} for {Subject} failed; {RetriesLeft} retries left.")]
    private partial void LogRetrying(Exception exception, string command, string subject, int retriesLeft);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Command} for {Subject} failed after every retry and was given up.")]
    private partial void LogGaveUp(Exception exception, string command, string subject);

    /// <summary>The state of one run. <see cref="RestartAfterEnd"/> and <see cref="IsEnding"/> are guarded by the supervisor's lock.</summary>
    private sealed class TrackedRun(RunId runId, ProcessId processId, TimeSpan gracePeriod) : IDisposable
    {
        private readonly CancellationTokenSource _launch = new();
        private readonly TaskCompletionSource _stop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _end = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RunId RunId { get; } = runId;

        public ProcessId ProcessId { get; } = processId;

        public TimeSpan GracePeriod { get; } = gracePeriod;

        public bool RestartAfterEnd { get; set; }

        /// <summary>The child is gone (or never existed) and the end is being recorded.</summary>
        public bool IsEnding { get; set; }

        public CancellationToken LaunchToken => _launch.Token;

        public Task StopRequested => _stop.Task;

        public Task Ended => _end.Task;

        /// <summary>Returns false when a stop was already requested.</summary>
        public bool RequestStop() => _stop.TrySetResult();

        /// <summary>Runs the launcher's cancellation callbacks away from the caller, who may be another command.</summary>
        public Task CancelLaunchAsync() => _launch.CancelAsync();

        public void MarkEnded() => _end.TrySetResult();

        public void Dispose() => _launch.Dispose();
    }
}
