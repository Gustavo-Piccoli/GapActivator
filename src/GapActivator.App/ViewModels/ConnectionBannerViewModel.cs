using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GapActivator.App.Localization;
using GapActivator.App.Services;

namespace GapActivator.App.ViewModels;

/// <summary>The strip above the page that explains why the service is not available, and the persistent development banner.</summary>
public sealed partial class ConnectionBannerViewModel : ViewModelBase
{
    private static readonly TimeSpan StartingLimit = TimeSpan.FromSeconds(30);

    private readonly IServiceGateway _gateway;
    private readonly IServiceStarter _starter;
    private readonly TimeProvider _time;
    private CancellationTokenSource? _startWait;
    private int? _failedCode;

    public ConnectionBannerViewModel(Localizer l, IServiceGateway gateway, IServiceStarter starter, TimeProvider time)
        : base(l)
    {
        _gateway = gateway;
        _starter = starter;
        _time = time;
        Refresh();
        gateway.ConnectionChanged += OnConnectionChanged;
        l.PropertyChanged += (_, _) => Refresh();
    }

    [ObservableProperty]
    public partial bool IsVisible { get; private set; }

    /// <summary>The explanation, or null while connected.</summary>
    [ObservableProperty]
    public partial string? Text { get; private set; }

    [ObservableProperty]
    public partial bool ShowStartService { get; private set; }

    /// <summary>True while connected to a service that is not the installed one (a development service).</summary>
    [ObservableProperty]
    public partial bool IsDevelopmentMode { get; private set; }

    [ObservableProperty]
    public partial bool IsConnected { get; private set; }

    [ObservableProperty]
    public partial string? ErrorText { get; private set; }

    public string DevelopmentText => L.BannerDevelopment;

    private void OnConnectionChanged(object? sender, GatewayStatus status)
    {
        if (status.IsConnected || status.State == ConnectionState.Refused)
        {
            _failedCode = null;
            _startWait?.Cancel();
        }

        Refresh();
    }

    [RelayCommand]
    private async Task StartServiceAsync()
    {
        _failedCode = null;
        Refresh();
        ServiceStartOutcome outcome = await _starter.StartAsync(CancellationToken.None).ConfigureAwait(true);

        switch (outcome.Result)
        {
            case ServiceStartResult.Declined:
                return;
            case ServiceStartResult.Failed:
                _failedCode = outcome.ErrorCode;
                Refresh();
                return;
        }

        if (_gateway.Status.IsConnected)
        {
            return;
        }

        // The gateway reconnects on its own; "Starting" lasts until it does, or at most 30 s.
        using var wait = new CancellationTokenSource();
        _startWait = wait;
        Refresh();
        try
        {
            await Task.Delay(StartingLimit, _time, wait.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Connected, or refused: nothing left to wait for.
        }
        finally
        {
            _startWait = null;
            Refresh();
        }
    }

    private void Refresh()
    {
        GatewayStatus status = _gateway.Status;
        var starting = _startWait is not null
            && status.State is ConnectionState.ServiceStopped or ConnectionState.Connecting or ConnectionState.ServiceNotInstalled;
        var text = starting ? L.BannerStarting : ConnectionTexts.Banner(status, L);

        Text = text;
        IsVisible = text is not null;
        ShowStartService = status.State == ConnectionState.ServiceStopped && !starting;
        IsDevelopmentMode = status.IsConnected && !status.ServerVerified;
        IsConnected = status.IsConnected;
        ErrorText = _failedCode is { } code ? L.StartServiceFailed(code) : null;
        OnPropertyChanged(nameof(DevelopmentText));
    }
}
