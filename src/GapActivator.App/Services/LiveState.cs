using GapActivator.App.Composition;
using GapActivator.Ipc.Contracts;
using Microsoft.Extensions.Logging;

namespace GapActivator.App.Services;

/// <summary>The outcomes the app calls failures.</summary>
public static class RunOutcomes
{
    public static IReadOnlyList<RunOutcome> Failures { get; } =
        [RunOutcome.Failed, RunOutcome.FailedToStart, RunOutcome.TimedOut, RunOutcome.Abandoned, RunOutcome.Crashed];
}

/// <summary>
/// The snapshot of the dashboard and the process list that every page reads. It refreshes right after each connect, because the
/// service drops pushes sent before the handshake, and again on every state-changed hint.
/// </summary>
public sealed class LiveState(IServiceGateway gateway, ILogger<LiveState> logger)
{
    private readonly object _gate = new();
    private bool _started;
    private bool _running;
    private bool _pending;
    private TaskCompletionSource? _done;

    public DashboardDto? Dashboard { get; private set; }

    public IReadOnlyList<ProcessSummaryDto> Processes { get; private set; } = [];

    /// <summary>True when the last refresh failed; the snapshot is then the one from before. Cleared by the next refresh that works.</summary>
    public bool RefreshFailed { get; private set; }

    /// <summary>Raised after every refresh, whether it stored a new snapshot or failed (see <see cref="RefreshFailed"/>), on the thread that finished it (the UI thread in the app).</summary>
    public event EventHandler? Changed;

    /// <summary>Starts following the gateway; a second call does nothing.</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        gateway.ConnectionChanged += (_, status) =>
        {
            if (status.IsConnected)
            {
                _ = RefreshAsync();
            }
        };
        gateway.StateChanged += (_, _) => _ = RefreshAsync();

        // The gateway may have connected before this object started listening.
        if (gateway.Status.IsConnected)
        {
            _ = RefreshAsync();
        }
    }

    /// <summary>
    /// Fetches the dashboard and the process list. One refresh runs at a time; a request made during one makes exactly one more run
    /// after it, and every caller waits for that last run.
    /// </summary>
    public Task RefreshAsync()
    {
        TaskCompletionSource done;
        lock (_gate)
        {
            if (_running)
            {
                _pending = true;
                return _done!.Task;
            }

            _running = true;
            done = _done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _ = LoopAsync(done);
        return done.Task;
    }

    // FetchAsync never throws, so the loop cannot end early: a queued refresh is never dropped and "running" is never left set.
    // Callers of RefreshAsync wait for the refresh to finish, not for its subscribers to be healthy, so the task completes
    // successfully after a failure; the failure is logged, and RefreshFailed tells the pages.
    private async Task LoopAsync(TaskCompletionSource done)
    {
        try
        {
            while (true)
            {
                lock (_gate)
                {
                    _pending = false;
                }

                await FetchAsync().ConfigureAwait(true);

                lock (_gate)
                {
                    if (!_pending)
                    {
                        _running = false;
                        break;
                    }
                }
            }
        }
        finally
        {
            done.TrySetResult();
        }
    }

    private async Task FetchAsync()
    {
        try
        {
            DashboardDto dashboard = await gateway.GetDashboardAsync().ConfigureAwait(true);
            IReadOnlyList<ProcessSummaryDto> processes = await gateway.ListProcessesAsync().ConfigureAwait(true);
            Dashboard = dashboard;
            Processes = processes;
            RefreshFailed = false;
        }
        catch (Exception ex)
        {
            // The last snapshot stays. Cancellation is a failure too: nothing here passes a token, so it is unexpected.
            AppLog.RefreshingLiveStateFailed(logger, ex);
            RefreshFailed = true;
        }

        // Also raised after a failure, so the pages learn about RefreshFailed. One throwing subscriber must not stop the others.
        foreach (Delegate subscriber in Changed?.GetInvocationList() ?? [])
        {
            try
            {
                ((EventHandler)subscriber)(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                AppLog.LiveStateSubscriberFailed(logger, ex);
            }
        }
    }
}
