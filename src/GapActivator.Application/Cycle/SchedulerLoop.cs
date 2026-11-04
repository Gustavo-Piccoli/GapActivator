using GapActivator.Application.Events;
using GapActivator.Application.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GapActivator.Application.Cycle;

/// <summary>
/// Singleton: runs a cycle, waits for the rest of the interval, and repeats until the stopping token is cancelled.
/// Runs never block it, because launches go to the supervisor (BI-038). The interval is read again every cycle (BI-037);
/// a cycle that fails before reading it waits for the last known interval, 60 seconds at first.
/// </summary>
public sealed partial class SchedulerLoop(
    CycleRunner cycle,
    ICommandDispatcher commands,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<SchedulerLoop> logger)
{
    private TimeSpan _lastInterval = TimeSpan.FromSeconds(60);

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
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

    [LoggerMessage(Level = LogLevel.Error, Message = "The scheduling cycle failed.")]
    private partial void LogCycleFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recording the cycle failure failed; publishing it directly.")]
    private partial void LogRecordFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Publishing the cycle failure failed.")]
    private partial void LogPublishFailed(Exception exception);
}
