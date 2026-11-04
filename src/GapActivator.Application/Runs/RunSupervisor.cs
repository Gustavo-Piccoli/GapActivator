using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using Microsoft.Extensions.Logging;

namespace GapActivator.Application.Runs;

/// <summary>
/// Singleton: owns the launched children. Each tracked run has one background task, the only code that touches its
/// process handle: wait for the previous run if needed, launch, wait for exit, stop or time limit, signal, wait for the
/// grace period, kill, record the end, remove the entry. Recording the launch or a time-limit stop runs alongside that
/// work and never holds it up; both are recorded before the exit. Records are sent as <see cref="Caller.System"/> with no
/// cancellation, so a shutdown cannot lose one that has started, and do not give up (see <see cref="SendAsync{TCommand}(TCommand, string, CancellationToken)"/>).
/// The task never throws: what it cannot handle is logged at Error and the entry is always removed. When the
/// supervisor's lifetime ends, every task finishes and a run not yet recorded as ended stays active for startup
/// reconciliation. Nothing here holds a lock across an await.
/// </summary>
public sealed partial class RunSupervisor(
    ICommandDispatcher dispatcher,
    IProcessLauncher launcher,
    ISecretProtector secrets,
    TimeProvider time,
    ILogger<RunSupervisor> logger) : IDisposable
{
    private const int FastRetries = 3;

    private static readonly TimeSpan FastRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan KillRetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>Timers cannot wait much longer than 49 days, so a long time limit is waited for in pieces.</summary>
    private static readonly TimeSpan MaxTimerDelay = TimeSpan.FromDays(1);

    private readonly Lifetime _lifetime = new();
    private readonly Lock _sync = new();
    private readonly Dictionary<RunId, TrackedRun> _byRun = [];

    /// <summary>The newest run of each process. At most one run of a process can have a live child: see <see cref="Start"/>.</summary>
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
    /// is what allowed this run to begin. That run is tracked at once, so it can be stopped and awaited, and it launches
    /// only after the previous entry has gone, so a process never has two live children.
    /// </summary>
    public void Start(RunStartTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        TrackedRun? run = null;
        TrackedRun? previous = null;
        lock (_sync)
        {
            _byProcess.TryGetValue(ticket.ProcessId, out TrackedRun? current);
            if ((current is null || current.IsEnding) && !_byRun.ContainsKey(ticket.RunId))
            {
                previous = current;
                run = Track(ticket.RunId, ticket.ProcessId, ticket.GracePeriod);
            }
        }

        if (run is null)
        {
            LogAlreadyTracked(ticket.ProcessId, ticket.RunId);
            return;
        }

        _ = Task.Run(() => LaunchAndSuperviseAsync(run, ticket, previous));
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
            if (!_byProcess.ContainsKey(processId) && !_byRun.ContainsKey(runId))
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
    /// Completes after the end was recorded, or refused, or the supervisor's lifetime ended, and the entry was removed;
    /// or at once if the run is not tracked. A pending restart is sent after this completes.
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

    /// <summary>Ends the supervisor's lifetime: every background task finishes and no child is touched.</summary>
    public void Dispose() => _lifetime.Dispose();

    /// <summary>Must be called holding <see cref="_sync"/>.</summary>
    private TrackedRun Track(RunId runId, ProcessId processId, TimeSpan gracePeriod)
    {
        var run = new TrackedRun(runId, processId, gracePeriod, _lifetime.Token);
        _byRun.Add(runId, run);
        _byProcess[processId] = run;
        return run;
    }

    private async Task LaunchAndSuperviseAsync(TrackedRun run, RunStartTicket ticket, TrackedRun? previous)
    {
        try
        {
            IRunningProcess? process = await LaunchAsync(run, ticket, previous);
            if (process is not null)
            {
                await using (process)
                {
                    var launched = new RecordRunLaunched(run.RunId, process.Pid, process.StartTime);
                    await SuperviseAsync(run, process, ticket.TimeLimit, launched);
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
                await SuperviseAsync(run, process, remainingTimeLimit, launched: null);
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

    /// <summary>
    /// Returns the process, or null after recording why there is none. The plaintext request exists only inside this
    /// method, so it does not stay alive for the child's life.
    /// </summary>
    private async Task<IRunningProcess?> LaunchAsync(TrackedRun run, RunStartTicket ticket, TrackedRun? previous)
    {
        if (previous is not null)
        {
            await Task.WhenAny(previous.Ended, run.StopRequested, _lifetime.Ended);
        }

        if (_lifetime.HasEnded)
        {
            LogLifetimeEnded(run.RunId);
            return null;
        }

        if (run.StopRequested.IsCompleted)
        {
            MarkEnding(run);
            await SendAsync(new RecordLaunchAborted(run.RunId), run.RunId.ToString());
            return null;
        }

        try
        {
            return await launcher.LaunchAsync(CreateRequest(ticket), run.LaunchToken);
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
        catch (OperationCanceledException) when (_lifetime.HasEnded)
        {
            LogLifetimeEnded(run.RunId);
        }
        catch (Exception ex)
        {
            LogLaunchThrew(ex, run.RunId);
            MarkEnding(run);
            await SendAsync(new RecordRunLaunchFailed(run.RunId, ex.Message), run.RunId.ToString());
        }

        return null;
    }

    private LaunchRequest CreateRequest(RunStartTicket ticket) =>
        new(
            ticket.RunId,
            ticket.Executable,
            ticket.Arguments,
            ticket.WorkingDirectory,
            [.. ticket.Environment.Items.Select(variable => ToKeyValue(ticket.RunId, variable))]);

    private KeyValuePair<string, string> ToKeyValue(RunId runId, EnvironmentVariable variable)
    {
        if (variable.SecretValue is not { } secret)
        {
            return new(variable.Name, variable.PlainValue!);
        }

        try
        {
            return new(variable.Name, secrets.Unprotect(secret));
        }
        catch (Exception ex)
        {
            LogSecretUnreadable(ex, runId, variable.Name);
            throw new LaunchFailedException($"The secret of environment variable '{variable.Name}' could not be read.", ex);
        }
    }

    /// <param name="launched">The launch to record, or null for an adopted run, whose launch is already recorded.</param>
    private async Task SuperviseAsync(
        TrackedRun run, IRunningProcess process, TimeSpan? timeLimit, RecordRunLaunched? launched)
    {
        string subject = run.RunId.ToString();
        Task<int> exited = process.Exited;
        var timeLimitStop = new StopRun(run.RunId, StopReason.TimeLimit);
        using var retries = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        try
        {
            Task<bool> recordingLaunch = launched is null
                ? Task.FromResult(true)
                : SendAsync(launched, subject, retries.Token);

            Task<bool> recordingStop = await WaitForExitStopOrDeadlineAsync(run, exited, timeLimit)
                ? SendAsync(timeLimitStop, subject, retries.Token)
                : Task.FromResult(true);

            bool killed = !exited.IsCompleted && !_lifetime.HasEnded && await StopChildAsync(run, process, exited);

            await Task.WhenAny(exited, _lifetime.Ended);
            if (!exited.IsCompleted)
            {
                LogLifetimeEnded(run.RunId);
                await recordingLaunch;
                await recordingStop;
                return;
            }

            // The exit needs the launch recorded, and the stop decides the outcome. A record that was still waiting for
            // its next retry tries again now instead of delaying the exit for up to a full backoff.
            await retries.CancelAsync();
            int exitCode = await exited;
            MarkEnding(run);

            bool recorded = (await recordingLaunch || await SendAsync(launched!, subject))
                && (await recordingStop || await SendAsync(timeLimitStop, subject));
            if (recorded)
            {
                await SendAsync(new RecordRunExited(run.RunId, exitCode, killed), subject);
            }
        }
        finally
        {
            await retries.CancelAsync();
        }
    }

    /// <summary>Returns true only when the time limit expired while the child was still running and nothing else had stopped it.</summary>
    private async Task<bool> WaitForExitStopOrDeadlineAsync(TrackedRun run, Task<int> exited, TimeSpan? timeLimit)
    {
        if (timeLimit is not { } limit)
        {
            await Task.WhenAny(exited, run.StopRequested, _lifetime.Ended);
            return false;
        }

        // The deadline is fixed once: every wait below is measured against it, so one large clock move still fires.
        DateTimeOffset now = time.GetUtcNow();
        DateTimeOffset deadline = limit < DateTimeOffset.MaxValue - now ? now + limit : DateTimeOffset.MaxValue;

        using var cancellation = new CancellationTokenSource();
        await Task.WhenAny(exited, run.StopRequested, _lifetime.Ended, WaitUntilAsync(deadline, cancellation.Token));
        await cancellation.CancelAsync();

        return !exited.IsCompleted && !run.StopRequested.IsCompleted && !_lifetime.HasEnded;
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

    /// <summary>
    /// Signals the child and kills it if the grace period ends first. A failed kill is tried again every 30 seconds:
    /// the child may still be alive, so the entry stays and keeps a new run from overlapping it. Returns whether it
    /// was killed.
    /// </summary>
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

        await WaitForExitOrDelayAsync(exited, run.GracePeriod);
        while (!exited.IsCompleted && !_lifetime.HasEnded)
        {
            try
            {
                // A child that exits by itself just before this call is reported as killed; the port cannot tell them apart.
                await process.KillTreeAsync();
                return true;
            }
            catch (Exception ex)
            {
                if (!exited.IsCompleted)
                {
                    LogKillFailed(ex, run.RunId);
                }
            }

            await WaitForExitOrDelayAsync(exited, KillRetryDelay);
        }

        return false;
    }

    private async Task WaitForExitOrDelayAsync(Task exited, TimeSpan delay)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        await Task.WhenAny(exited, Task.Delay(delay, time, cancellation.Token));
        await cancellation.CancelAsync();
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

            // A deferred run may already have taken the process's place.
            if (_byProcess.TryGetValue(run.ProcessId, out TrackedRun? current) && ReferenceEquals(current, run))
            {
                _byProcess.Remove(run.ProcessId);
            }

            restart = run.RestartAfterEnd;
        }

        run.Dispose();
        run.MarkEnded();

        if (restart && !_lifetime.HasEnded)
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

    private Task<bool> SendAsync<TCommand>(TCommand command, string subject)
        where TCommand : ICommand<Unit> =>
        SendAsync(command, subject, _lifetime.Token);

    /// <summary>
    /// Sends a command until it succeeds: 3 retries, 1 second apart, then one Error and retries with a backoff doubling
    /// from 2 up to 60 seconds. A rule violation will not heal, so it is logged at Error without a retry. Never throws.
    /// Returns false only when <paramref name="stopRetrying"/> fired before the command got an answer.
    /// </summary>
    private async Task<bool> SendAsync<TCommand>(TCommand command, string subject, CancellationToken stopRetrying)
        where TCommand : ICommand<Unit>
    {
        string name = typeof(TCommand).Name;
        TimeSpan backoff = FirstBackoff;
        for (int failures = 1; ; failures++)
        {
            try
            {
                await dispatcher.SendAsync<TCommand, Unit>(command, Caller.System, CancellationToken.None);
                if (failures > FastRetries + 1)
                {
                    LogRecovered(name, subject);
                }

                return true;
            }
            catch (Exception ex) when (ex is CommandRejectedException or DomainException)
            {
                LogRuleViolation(ex, name, subject);
                return true;
            }
            catch (Exception ex)
            {
                if (failures <= FastRetries)
                {
                    LogRetrying(ex, name, subject, failures);
                }
                else if (failures == FastRetries + 1)
                {
                    LogKeepsFailing(ex, name, subject);
                }
                else
                {
                    LogStillFailing(ex, name, subject);
                }
            }

            TimeSpan delay = FastRetryDelay;
            if (failures > FastRetries)
            {
                delay = backoff;
                backoff = backoff * 2 < MaxBackoff ? backoff * 2 : MaxBackoff;
            }

            try
            {
                await Task.Delay(delay, time, stopRetrying);
            }
            catch (OperationCanceledException)
            {
                if (_lifetime.HasEnded)
                {
                    LogAbandoned(name, subject);
                }

                return false;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Process {ProcessId} is already tracked; run {RunId} was ignored.")]
    private partial void LogAlreadyTracked(ProcessId processId, RunId runId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The launcher failed unexpectedly for run {RunId}.")]
    private partial void LogLaunchThrew(Exception exception, RunId runId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The secret of environment variable {Variable} could not be read for run {RunId}.")]
    private partial void LogSecretUnreadable(Exception exception, RunId runId, string variable);

    [LoggerMessage(Level = LogLevel.Error, Message = "The supervision of run {RunId} failed.")]
    private partial void LogRunTaskFailed(Exception exception, RunId runId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The supervisor is stopping; run {RunId} stays active for startup reconciliation.")]
    private partial void LogLifetimeEnded(RunId runId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Signalling the child of run {RunId} failed; it will be killed after the grace period.")]
    private partial void LogSignalFailed(Exception exception, RunId runId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Killing the child of run {RunId} failed; trying again until it exits.")]
    private partial void LogKillFailed(Exception exception, RunId runId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Aborting the launch of run {RunId} failed.")]
    private partial void LogCancelFailed(Exception exception, RunId runId);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Command} for {Subject} was refused and will not be retried.")]
    private partial void LogRuleViolation(Exception exception, string command, string subject);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Command} for {Subject} failed (failure {Failures}); retrying.")]
    private partial void LogRetrying(Exception exception, string command, string subject, int failures);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Command} for {Subject} keeps failing; retrying with a backoff until it succeeds or the supervisor stops.")]
    private partial void LogKeepsFailing(Exception exception, string command, string subject);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Command} for {Subject} is still failing.")]
    private partial void LogStillFailing(Exception exception, string command, string subject);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Command} for {Subject} succeeded after failing repeatedly.")]
    private partial void LogRecovered(string command, string subject);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The supervisor is stopping; {Command} for {Subject} was not recorded.")]
    private partial void LogAbandoned(string command, string subject);

    /// <summary>The state of one run. <see cref="RestartAfterEnd"/> and <see cref="IsEnding"/> are guarded by the supervisor's lock.</summary>
    private sealed class TrackedRun(
        RunId runId, ProcessId processId, TimeSpan gracePeriod, CancellationToken lifetime) : IDisposable
    {
        private readonly CancellationTokenSource _launch = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
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

    /// <summary>The token and the task stay usable after disposal, when background tasks may still look at them.</summary>
    private sealed class Lifetime : IDisposable
    {
        private readonly CancellationTokenSource _source = new();
        private int _disposed;

        public Lifetime()
        {
            Token = _source.Token;
            Ended = Task.Delay(Timeout.Infinite, Token);
        }

        public CancellationToken Token { get; }

        /// <summary>Ends as cancelled; only ever passed to Task.WhenAny.</summary>
        public Task Ended { get; }

        public bool HasEnded => Token.IsCancellationRequested;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _source.Cancel();
                _source.Dispose();
            }
        }
    }
}
