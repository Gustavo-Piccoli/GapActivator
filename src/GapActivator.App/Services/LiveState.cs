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

    /// <summary>Raised after a refresh stored a new snapshot, on the thread that finished it (the UI thread in the app).</summary>
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
        catch
        {
            // A subscriber threw: do not leave the refresh marked as running for ever.
            lock (_gate)
            {
                _running = false;
            }

            throw;
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
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.RefreshingLiveStateFailed(logger, ex);
            return;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
