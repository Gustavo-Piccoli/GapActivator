using System.ComponentModel;
using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Shell;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.ViewModels;

/// <summary>Keeps the tray icon, its tooltip and its menu in step with the connection, the live state and the language.</summary>
/// <remarks>
/// The icon state and the tooltip are recomputed from their sources on every change, never cached as text, so a language
/// switch is followed live. The state is also in the tooltip, so colour is never the only signal.
/// </remarks>
public sealed class TrayCoordinator(
    Localizer l,
    IServiceGateway gateway,
    LiveState live,
    FailureAcknowledgements seen,
    ITrayIcon tray,
    IMainWindowService window,
    IAppLifetime lifetime,
    ServiceActions service,
    INavigator navigator,
    IUiDispatcher ui) : IDisposable
{
    private const string RunTagPrefix = "run:";

    private bool _started;
    private bool _disposed;

    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        gateway.ConnectionChanged += OnConnectionChanged;
        live.Changed += OnSourceChanged;
        seen.Changed += OnSourceChanged;
        l.PropertyChanged += OnLocalizerChanged;
        tray.MenuRequested += OnMenuRequested;
        tray.OpenRequested += OnOpenRequested;
        tray.BalloonClicked += OnBalloonClicked;
        RefreshTray();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_started)
        {
            return;
        }

        gateway.ConnectionChanged -= OnConnectionChanged;
        live.Changed -= OnSourceChanged;
        seen.Changed -= OnSourceChanged;
        l.PropertyChanged -= OnLocalizerChanged;
        tray.MenuRequested -= OnMenuRequested;
        tray.OpenRequested -= OnOpenRequested;
        tray.BalloonClicked -= OnBalloonClicked;
    }

    /// <summary>First match wins: unreachable, attention (a blocked process or an unseen failure), busy, healthy.</summary>
    internal static TrayIconState Compute(
        GatewayStatus status,
        DashboardDto? dashboard,
        IReadOnlyList<ProcessSummaryDto> processes,
        FailureAcknowledgements seen)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(seen);

        if (!status.IsConnected)
        {
            return TrayIconState.Unreachable;
        }

        if (processes.Any(p => p.Lifecycle == ProcessLifecycle.Blocked)
            || (dashboard?.RecentFailures.Any(seen.IsUnseen) ?? false))
        {
            return TrayIconState.Attention;
        }

        return dashboard?.ActiveRuns.Count > 0 ? TrayIconState.Busy : TrayIconState.Healthy;
    }

    private void RefreshTray()
    {
        if (_disposed)
        {
            return;
        }

        var dashboard = live.Dashboard;
        var state = Compute(gateway.Status, dashboard, live.Processes, seen);
        var stateText = state switch
        {
            TrayIconState.Unreachable => l.TrayUnreachable,
            TrayIconState.Attention => l.TrayAttention,
            TrayIconState.Busy => l.TrayBusy(dashboard?.ActiveRuns.Count ?? 0),
            _ => dashboard?.IsPaused == true ? l.TrayPaused : l.TrayIdle,
        };
        tray.SetState(state, l.TrayTooltip(stateText));
    }

    private void OnConnectionChanged(object? sender, GatewayStatus status) => ui.Post(RefreshTray);

    private void OnSourceChanged(object? sender, EventArgs e) => ui.Post(RefreshTray);

    private void OnLocalizerChanged(object? sender, PropertyChangedEventArgs e) => ui.Post(RefreshTray);

    private void OnOpenRequested(object? sender, EventArgs e) => Show();

    private void Show()
    {
        if (!_disposed)
        {
            window.Show();
        }
    }

    private void OnMenuRequested(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        List<TrayMenuItem> items = [new("open", l.MenuOpen)];
        if (gateway.Status.CanChange)
        {
            items.Add(new TrayMenuItem("restart", l.MenuRestartService));
        }

        items.Add(TrayMenuItem.Separator);
        items.Add(new TrayMenuItem("quit", l.MenuQuit));

        Execute(tray.ShowMenu(items));
    }

    private void Execute(string? id)
    {
        switch (id)
        {
            case "open":
                Show();
                break;
            case "restart":
                _ = RestartAsync();
                break;
            case "quit":
                // Only the app exits; the service keeps running.
                lifetime.Exit();
                break;
        }
    }

    private async Task RestartAsync()
    {
        try
        {
            // Asks first, then restarts; "no" sends nothing.
            await service.RestartServiceAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The coordinator has no logger and a menu click has no place to show an error.
        }
    }

    // The balloon tag is a single, last-wins field, so with overlapping balloons the tag may belong to another run.
    // A click therefore only shows the window and then asks the history for the run, which tolerates one it cannot find.
    private void OnBalloonClicked(object? sender, string? tag)
    {
        if (_disposed)
        {
            return;
        }

        window.Show();
        if (tag is not null
            && tag.StartsWith(RunTagPrefix, StringComparison.Ordinal)
            && Guid.TryParse(tag[RunTagPrefix.Length..], out var runId))
        {
            navigator.OpenRun(runId);
        }
    }
}
