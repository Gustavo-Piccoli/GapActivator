using GapActivator.App.Settings;
using Microsoft.Extensions.Logging;

namespace GapActivator.App.Updates;

/// <summary>
/// Checks for newer releases on demand, and once started every hour looks whether a daily check is due (when the app setting is on).
/// Every check, successful or not, records its time, so a failing network is retried at most once a day.
/// </summary>
public sealed partial class UpdateChecker(IReleaseSource source, Version current, IAppSettingsService settings, TimeProvider time, ILogger<UpdateChecker> logger)
    : IUpdateChecker, IDisposable
{
    private readonly Version _current = ReleaseVersion.Normalize(current);
    private readonly CancellationTokenSource _stopping = new();
    private ITimer? _timer;
    private UpdateCheckResult? _last;
    private int _running;
    private int _disposed;

    public static TimeSpan CheckInterval { get; } = TimeSpan.FromHours(24);

    public static TimeSpan TickInterval { get; } = TimeSpan.FromHours(1);

    public static TimeSpan Timeout { get; } = TimeSpan.FromSeconds(10);

    public event EventHandler<UpdateCheckResult>? UpdateFound;

    public event EventHandler<UpdateCheckResult>? Checked;

    public UpdateCheckResult? Last => Volatile.Read(ref _last);

    /// <summary>True while a scheduled (timer-started) check is in progress; tests wait for it to end before asserting that nothing happened.</summary>
    internal bool ScheduledCheckRunning => Volatile.Read(ref _running) == 1;

    /// <summary>Starts the hourly tick, the first at once; a second call does nothing.</summary>
    public void StartBackground()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        if (_timer is not null)
        {
            return;
        }

        // The callback runs on a pool thread; the result reaches the UI only through UpdateFound handlers that marshal.
        _timer = time.CreateTimer(OnTick, null, TimeSpan.Zero, TickInterval);
    }

    public async Task<UpdateCheckResult> CheckNowAsync(CancellationToken ct)
    {
        var result = await CheckCoreAsync(ct).ConfigureAwait(false);

        // A check that was still running when the app shut down leaves no trace: no write, no event.
        if (Volatile.Read(ref _disposed) == 1)
        {
            return result;
        }

        Volatile.Write(ref _last, result);
        settings.Update(s => s with { LastUpdateCheck = time.GetUtcNow() });
        Checked?.Invoke(this, result);
        if (result.Outcome == UpdateCheckOutcome.UpdateAvailable)
        {
            UpdateFound?.Invoke(this, result);
        }

        return result;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _timer?.Dispose();
            _stopping.Cancel();
            _stopping.Dispose();
        }
    }

    private static UpdateCheckResult Failed() => new(UpdateCheckOutcome.Failed, null, null);

    private async Task<UpdateCheckResult> CheckCoreAsync(CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(Timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            var release = await source.GetLatestAsync(linked.Token).ConfigureAwait(false);
            if (release is null)
            {
                return new UpdateCheckResult(UpdateCheckOutcome.NoReleases, null, null);
            }

            if (!ReleaseVersion.TryParse(release.TagName, out var latest))
            {
                LogUnreadableTag();
                return Failed();
            }

            var outcome = latest > _current ? UpdateCheckOutcome.UpdateAvailable : UpdateCheckOutcome.UpToDate;
            return new UpdateCheckResult(outcome, latest, release.PageUrl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogCheckFailed(ex);
            return Failed();
        }
    }

    private void OnTick(object? state)
    {
        try
        {
            if (Volatile.Read(ref _disposed) == 1 || !IsDue())
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            {
                return;
            }

            _ = RunScheduledAsync();
        }
        catch (Exception ex)
        {
            LogTickFailed(ex);
        }
    }

    private bool IsDue()
    {
        var current = settings.Current;
        return current.CheckForUpdates && (current.LastUpdateCheck is not { } last || time.GetUtcNow() - last >= CheckInterval);
    }

    private async Task RunScheduledAsync()
    {
        try
        {
            await CheckNowAsync(_stopping.Token).ConfigureAwait(false);
        }
        catch (Exception) when (Volatile.Read(ref _disposed) == 1)
        {
            // Disposed while the check ran: nothing to report.
        }
        catch (Exception ex)
        {
            LogTickFailed(ex);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Update check failed.")]
    private partial void LogCheckFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Update check failed.")]
    private partial void LogUnreadableTag();

    [LoggerMessage(Level = LogLevel.Information, Message = "The scheduled update check threw.")]
    private partial void LogTickFailed(Exception ex);
}
