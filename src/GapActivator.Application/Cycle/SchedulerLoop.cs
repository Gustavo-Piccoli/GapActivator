using GapActivator.Application.Events;
using GapActivator.Application.Messaging;
using GapActivator.Application.Runs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GapActivator.Application.Cycle;

/// <summary>
/// Singleton: reconciles the runs left by the previous instance once (BI-073), then runs a cycle, waits for the rest of
/// the interval, and repeats until the stopping token is cancelled. Runs never block it, because launches go to the
/// supervisor (BI-038). The interval is read again every cycle (BI-037); a cycle that fails before reading it waits for
/// the last known interval, 60 seconds at first. A failed reconciliation is logged at Critical and rethrown, so the
/// host stops and the service's recovery actions restart it. However the loop ends, the tracked runs are stopped for
/// the shutdown first, for at most the largest grace period plus 15 seconds, and then the supervisor's lifetime ends.
/// </summary>
public sealed partial class SchedulerLoop(
    CycleRunner cycle,
    ICommandDispatcher commands,
    IServiceScopeFactory scopes,
    RunSupervisor supervisor,
    TimeProvider time,
    ILogger<SchedulerLoop> logger)
{
    private static readonly TimeSpan ShutdownMargin = TimeSpan.FromSeconds(15);

    private TimeSpan _lastInterval = TimeSpan.FromSeconds(60);

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ReconcileAsync(stoppingToken);
            while (true)
            {
                stoppingToken.ThrowIfCancellationRequested();

                // The monotonic clock, so a wall-clock jump cannot stretch or shrink the wait.
                long start = time.GetTimestamp();
                try
                {
                    _lastInterval = await cycle.RunOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogCycleFailed(ex);
                    await RecordCycleFailureAsync(ex.Message, stoppingToken);
                }

                TimeSpan wait = _lastInterval - time.GetElapsedTime(start);
                await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown: end quietly, with no CycleFailed (BI-040).
        }
        finally
        {
            await StopRunsAsync();
        }
    }

    private async Task ReconcileAsync(CancellationToken stoppingToken)
    {
        try
        {
            await commands.SendAsync<ReconcileOnStartup, Unit>(new ReconcileOnStartup(), Caller.System, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            LogReconcileFailed(ex);
            throw;
        }
    }

    /// <summary>Never throws, so it cannot hide the exception that ends the loop. Children still running are not touched.</summary>
    private async Task StopRunsAsync()
    {
        try
        {
            try
            {
                using var deadline = new CancellationTokenSource(supervisor.LargestGracePeriod + ShutdownMargin, time);
                await supervisor.StopAllAsync(deadline.Token);
            }
            finally
            {
                // Ends the workers still retrying a record; their runs stay active for the next start.
                supervisor.EndLifetime();
            }
        }
        catch (Exception ex)
        {
            LogShutdownFailed(ex);
        }
    }

    /// <summary>Nothing here may end the loop, except the shutdown cancellation.</summary>
    private async Task RecordCycleFailureAsync(string error, CancellationToken ct)
    {
        try
        {
            await commands.SendAsync<RecordCycleFailure, Unit>(new RecordCycleFailure(error), Caller.System, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogRecordFailed(ex);
            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                var publisher = scope.ServiceProvider.GetRequiredService<EventPublisher>();
                await publisher.PublishAsync([new CycleFailed(error, time.GetUtcNow())], ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception publishEx)
            {
                LogPublishFailed(publishEx);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Reconciling the runs of the previous instance failed; GapActivator stops.")]
    private partial void LogReconcileFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Stopping the runs for the shutdown failed.")]
    private partial void LogShutdownFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The scheduling cycle failed.")]
    private partial void LogCycleFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recording the cycle failure failed; publishing it directly.")]
    private partial void LogRecordFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Publishing the cycle failure failed.")]
    private partial void LogPublishFailed(Exception exception);
}
