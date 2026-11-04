using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using GapActivator.Application.Queries;
using GapActivator.Domain.Processes;
using Microsoft.Extensions.Logging;

namespace GapActivator.Application.Cycle;

/// <summary>
/// Singleton: one scheduling cycle. It runs retention once per local calendar day, then, unless the installation is
/// paused, evaluates each process in its own command so that a failure in one never affects another (BI-039).
/// The single scheduler loop is the only caller, so the last retention day needs no guard.
/// </summary>
public sealed partial class CycleRunner(
    ICommandDispatcher commands,
    IQueryDispatcher queries,
    ILogRetention logRetention,
    TimeProvider time,
    ILogger<CycleRunner> logger)
{
    private DateOnly? _lastRetentionDay;

    /// <summary>Runs one cycle and returns the interval to wait before the next one.</summary>
    public async Task<TimeSpan> RunOnceAsync(CancellationToken ct)
    {
        CycleSettings settings = await queries.QueryAsync<GetCycleSettings, CycleSettings>(new GetCycleSettings(), Caller.System, ct);

        await RunRetentionAsync(settings, ct);

        // Q-1: a paused installation dispatches nothing.
        if (settings.IsPaused)
        {
            return settings.CycleInterval;
        }

        IReadOnlyList<ProcessId> ids = await queries.QueryAsync<ListProcessIds, IReadOnlyList<ProcessId>>(new ListProcessIds(), Caller.System, ct);
        foreach (ProcessId id in ids)
        {
            try
            {
                await commands.SendAsync<EvaluateProcess, Unit>(new EvaluateProcess(id), Caller.System, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogEvaluateFailed(ex, id);
            }
        }

        return settings.CycleInterval;
    }

    /// <summary>Retention failures are logged and never fail the cycle. The day is recorded only if both parts succeeded (BI-086, BI-089).</summary>
    private async Task RunRetentionAsync(CycleSettings settings, CancellationToken ct)
    {
        DateTimeOffset now = time.GetUtcNow();
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, time.LocalTimeZone).Date);
        if (_lastRetentionDay == today)
        {
            return;
        }

        bool logsPurged = false;
        try
        {
            await logRetention.PurgeOlderThanAsync(settings.LogRetentionDays, ct);
            logsPurged = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogLogRetentionFailed(ex);
        }

        bool historyPurged = false;
        try
        {
            await commands.SendAsync<PurgeHistory, int>(new PurgeHistory(settings.HistoryRetentionDays), Caller.System, ct);
            historyPurged = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogHistoryRetentionFailed(ex);
        }

        if (logsPurged && historyPurged)
        {
            _lastRetentionDay = today;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Evaluating process {ProcessId} failed; the cycle continues with the next process.")]
    private partial void LogEvaluateFailed(Exception exception, ProcessId processId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Log retention failed; it will be tried again in the next cycle.")]
    private partial void LogLogRetentionFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "History retention failed; it will be tried again in the next cycle.")]
    private partial void LogHistoryRetentionFailed(Exception exception);
}
