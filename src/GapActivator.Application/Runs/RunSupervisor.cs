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

    /// <summary>Set under <see cref="_sync"/> when <see cref="StopAllAsync"/> begins: nothing is launched or restarted after it.</summary>
    private bool _stoppingAll;

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

    /// <summary>The largest grace period of the tracked runs; zero when nothing is tracked.</summary>
    public TimeSpan LargestGracePeriod
    {
        get
        {
            lock (_sync)
            {
                return _byRun.Values.Select(run => run.GracePeriod).DefaultIfEmpty(TimeSpan.Zero).Max();
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
        bool shuttingDown = false;
        lock (_sync)
        {
            _byProcess.TryGetValue(ticket.ProcessId, out TrackedRun? current);
            if ((current is null || current.IsEnding) && !_byRun.ContainsKey(ticket.RunId))
            {
                previous = current;
                shuttingDown = _stoppingAll;
                run = Track(ticket.RunId, ticket.ProcessId, ticket.GracePeriod);
            }
        }

        if (run is null)
        {
            LogAlreadyTracked(ticket.ProcessId, ticket.RunId);
            return;
        }

        _ = Task.Run(() => LaunchAndSuperviseAsync(run, ticket, previous, shuttingDown));
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
    /// the two paths sends it. Once <see cref="StopAllAsync"/> has begun the restart is dropped and logged: it is not
    /// persisted, so the next start does not run it either.
    /// </summary>
    public void StartAfterEnd(RunId runId, ProcessId processId)
    {
        bool dropped;
        lock (_sync)
        {
            if (_byRun.TryGetValue(runId, out TrackedRun? run))
            {
                run.RestartAfterEnd = true;
                return;
            }

            dropped = _stoppingAll;
        }

        if (dropped)
        {
            LogRestartDroppedForShutdown(processId);
            return;
        }

        _ = Task.Run(() => SendRestartAsync(processId));
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

    /// <summary>
    /// Stops every tracked run for a shutdown and waits for their ends, bounded by <paramref name="ct"/>. Each stop goes
    /// through <see cref="StopRun"/> so the reason is recorded and the run ends Interrupted; a send that fails is logged
    /// and its run left alone. From here on nothing is launched or restarted: a run tracked later is stopped without
    /// launching and is waited for too. When <paramref name="ct"/> fires this returns normally, and the runs still
    /// tracked stay active for startup reconciliation. Never throws.
    /// </summary>
    public async Task StopAllAsync(CancellationToken ct)
    {
        RunId[] tracked;
        lock (_sync)
        {
            _stoppingAll = true;
            tracked = [.. _byRun.Keys];
        }

        await Task.WhenAll(tracked.Select(runId => SendShutdownStopAsync(runId, ct)));

        try
        {
            // Runs tracked after this began are not launched, but they are waited for as well.
            while (true)
            {
                Task[] ends;
                lock (_sync)
                {
                    ends = [.. _byRun.Values.Select(run => run.Ended)];
                }

                if (ends.Length == 0)
                {
                    break;
                }

                await Task.WhenAll(ends).WaitAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            LogShutdownDeadline(TrackedRuns.Count);
        }
    }

    /// <summary>
    /// Ends the supervisor's lifetime: every background task finishes and no child is touched. The lifetime has ended when
    /// this returns; the cancellation callbacks run away from the caller.
    /// </summary>
    public void Dispose() => _ = EndLifetimeAsync();

    /// <summary>The same as <see cref="Dispose"/>, for the shutdown; either may be called first, and more than once.</summary>
    public void EndLifetime() => _ = EndLifetimeAsync();

    private async Task SendShutdownStopAsync(RunId runId, CancellationToken ct)
    {
        try
        {
            await dispatcher.SendAsync<StopRun, Unit>(new StopRun(runId, StopReason.Shutdown), Caller.System, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            LogShutdownStopCancelled(runId);
        }
        catch (Exception ex)
        {
            // Stop is not called directly: a child signalled with no recorded reason would end as a failure. The run
            // stays active for startup reconciliation, like one still active at the deadline.
            LogShutdownStopFailed(ex, runId);
        }
    }

    /// <summary>Must be called holding <see cref="_sync"/>.</summary>
    private TrackedRun Track(RunId runId, ProcessId processId, TimeSpan gracePeriod)
    {
        var run = new TrackedRun(runId, processId, gracePeriod, _lifetime.Token);
        _byRun.Add(runId, run);
        _byProcess[processId] = run;
        return run;
    }

    private async Task LaunchAndSuperviseAsync(
        TrackedRun run, RunStartTicket ticket, TrackedRun? previous, bool shuttingDown)
    {
        try
        {
            IRunningProcess? process = await LaunchAsync(run, ticket, previous, shuttingDown);
            if (process is not null)
            {
                await using (process)
                {
                    // One instant: the recorded StartedAt and the start of the time limit.
                    DateTimeOffset launchedAt = time.GetUtcNow();
                    var launched = new RecordRunLaunched(run.RunId, process.Pid, process.StartTime, launchedAt);
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
    /// method, so it does not stay alive for the child's life. A run tracked after <see cref="StopAllAsync"/> began is
    /// never launched: it is stopped for the shutdown, so it ends Interrupted through the abort below.
    /// </summary>
    private async Task<IRunningProcess?> LaunchAsync(
        TrackedRun run, RunStartTicket ticket, TrackedRun? previous, bool shuttingDown)
    {
        if (shuttingDown)
        {
            // The stop's after-commit action requests the stop before this returns, unless the run is no longer active.
            await SendAsync(new StopRun(run.RunId, StopReason.Shutdown), run.RunId.ToString());
        }

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

        if (shuttingDown)
        {
            LogNotLaunchedForShutdown(run.RunId);
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

    /// <param name="run">The tracked run.</param>
    /// <param name="process">The child.</param>
    /// <param name="timeLimit">
    /// The time limit, measured from <see cref="RecordRunLaunched.LaunchedAt"/> for a launch; for an adopted run it is the
    /// remainder reconciliation computed from the persisted StartedAt, measured from now.
    /// </param>
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

            DateTimeOffset limitStart = launched?.LaunchedAt ?? time.GetUtcNow();
            Task<bool> recordingStop = await WaitForExitStopOrDeadlineAsync(run, exited, timeLimit, limitStart)
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
    private async Task<bool> WaitForExitStopOrDeadlineAsync(
        TrackedRun run, Task<int> exited, TimeSpan? timeLimit, DateTimeOffset limitStart)
    {
        if (timeLimit is not { } limit)
        {
            await Task.WhenAny(exited, run.StopRequested, _lifetime.Ended);
            return false;
        }

        // The deadline is fixed once: every wait below is measured against it, so one large clock move still fires.
        DateTimeOffset deadline = limit < DateTimeOffset.MaxValue - limitStart ? limitStart + limit : DateTimeOffset.MaxValue;

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
        bool killFailureLogged = false;
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
                if (!exited.IsCompleted && killFailureLogged)
                {
                    LogKillStillFailing(ex, run.RunId);
                }
                else if (!exited.IsCompleted)
                {
                    LogKillFailed(ex, run.RunId);
                    killFailureLogged = true;
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
        bool dropped;
        lock (_sync)
        {
            _byRun.Remove(run.RunId);

            // A deferred run may already have taken the process's place.
            if (_byProcess.TryGetValue(run.ProcessId, out TrackedRun? current) && ReferenceEquals(current, run))
            {
                _byProcess.Remove(run.ProcessId);
            }

            restart = run.RestartAfterEnd && !_stoppingAll;
            dropped = run.RestartAfterEnd && _stoppingAll;
        }

        // Logged before the end is signalled, so whoever awaits the end sees the decision.
        if (dropped)
        {
            LogRestartDroppedForShutdown(run.ProcessId);
        }

        run.Dispose();
        run.MarkEnded();

        if (restart && !_lifetime.HasEnded)
        {
            await SendRestartAsync(run.ProcessId);
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

    private async Task EndLifetimeAsync()
    {
        try
        {
            await _lifetime.DisposeAsync();
        }
        catch (Exception ex)
        {
            LogLifetimeCallbackFailed(ex);
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
    /// Sends <see cref="StartPendingRun"/> with 3 retries, 1 second apart, then logs Error and drops it. A restart is a
    /// momentary intent: retrying for longer could start an extra run after a later cycle's run ended, and the operator
    /// can ask again. Never throws.
    /// </summary>
    private async Task SendRestartAsync(ProcessId processId)
    {
        var command = new StartPendingRun(processId);
        string name = nameof(StartPendingRun);
        string subject = processId.ToString();
        for (int failures = 1; ; failures++)
        {
            try
            {
                await dispatcher.SendAsync<StartPendingRun, Unit>(command, Caller.System, CancellationToken.None);
                return;
            }
            catch (Exception ex) when (ex is CommandRejectedException or DomainException)
            {
                LogRuleViolation(ex, name, subject);
                return;
            }
            catch (Exception ex)
            {
                if (failures > FastRetries)
                {
                    LogRestartDropped(ex, processId);
                    return;
                }

                LogRetrying(ex, name, subject, failures);
            }

            try
            {
                await Task.Delay(FastRetryDelay, time, _lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                LogAbandoned(name, subject);
                return;
            }
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "The shutdown deadline passed with {Count} run(s) still tracked; they stay active for startup reconciliation.")]
    private partial void LogShutdownDeadline(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "GapActivator is stopping; run {RunId} was not launched and stays active for startup reconciliation.")]
    private partial void LogNotLaunchedForShutdown(RunId runId);

    [LoggerMessage(Level = LogLevel.Information, Message = "GapActivator is stopping; the pending restart of process {ProcessId} was dropped.")]
    private partial void LogRestartDroppedForShutdown(ProcessId processId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Stopping run {RunId} for the shutdown failed; it stays active for startup reconciliation.")]
    private partial void LogShutdownStopFailed(Exception exception, RunId runId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The shutdown deadline passed before run {RunId} could be stopped; it stays active for startup reconciliation.")]
    private partial void LogShutdownStopCancelled(RunId runId);

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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Killing the child of run {RunId} is still failing.")]
    private partial void LogKillStillFailing(Exception exception, RunId runId);

    [LoggerMessage(Level = LogLevel.Error, Message = "The restart of process {ProcessId} was dropped after repeated failures; it can be requested again.")]
    private partial void LogRestartDropped(Exception exception, ProcessId processId);

    [LoggerMessage(Level = LogLevel.Error, Message = "A cancellation callback failed when the supervisor's lifetime ended.")]
    private partial void LogLifetimeCallbackFailed(Exception exception);

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "The supervisor is stopping; {Command} for {Subject} was abandoned.")]
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
    private sealed class Lifetime : IAsyncDisposable
    {
        private readonly CancellationTokenSource _source = new();
        private int _ending;

        public Lifetime()
        {
            Token = _source.Token;
            Ended = Task.Delay(Timeout.Infinite, Token);
        }

        public CancellationToken Token { get; }

        /// <summary>Ends as cancelled; only ever passed to Task.WhenAny.</summary>
        public Task Ended { get; }

        public bool HasEnded => Token.IsCancellationRequested;

        /// <summary>
        /// Idempotent. <see cref="HasEnded"/> is true before this first yields, and the source is disposed after the
        /// callbacks ran, even when one of them throws: this task then rethrows it.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _ending, 1) != 0)
            {
                return;
            }

            try
            {
                await _source.CancelAsync();
            }
            finally
            {
                _source.Dispose();
            }
        }
    }
}
