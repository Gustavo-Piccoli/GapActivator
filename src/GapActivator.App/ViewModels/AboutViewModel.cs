using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Settings;
using GapActivator.App.Updates;

namespace GapActivator.App.ViewModels;

/// <summary>
/// The About page: version, the update check, the project page, the folders and who may change things. Every text is derived from a
/// stored outcome (status, connection), so a language switch rebuilds it without losing the outcome.
/// </summary>
public sealed partial class AboutViewModel : ViewModelBase, IDisposable
{
    private enum Status
    {
        None,
        Checking,
        UpToDate,
        NoReleases,
        Failed,
        Available,
    }

    private const string Repository = "https://github.com/Gustavo-Piccoli/GapActivator";

    private readonly IServiceGateway _gateway;
    private readonly IUpdateChecker _updates;
    private readonly IShellLauncher _launcher;
    private readonly IUiDispatcher _ui;
    private readonly AppPaths _app;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;
    private Status _status = Status.None;
    private Version? _availableVersion;
    private bool _folderMissing;
    private bool _openFailed;

    public AboutViewModel(Localizer l, IServiceGateway gateway, IUpdateChecker updates, IShellLauncher launcher, AppPaths app, ServicePaths service, IUiDispatcher ui, string version)
        : base(l)
    {
        _gateway = gateway;
        _updates = updates;
        _launcher = launcher;
        _app = app;
        _ui = ui;
        VersionText = version;
        ServiceDataRoot = service.DataRoot;
        ServiceLogs = service.LogsDirectory;
        AppSettingsFile = app.SettingsFile;
        AppLogs = app.LogsDirectory;
        if (updates.Last is { } last)
        {
            Apply(last);
        }

        L.PropertyChanged += OnLocalizerChanged;
        gateway.ConnectionChanged += OnConnectionChanged;
        updates.UpdateFound += OnUpdateFound;
    }

    public string VersionText { get; }

#pragma warning disable CA1822 // An instance member so compiled bindings can reach it.
    public string RepositoryUrl => Repository;
#pragma warning restore CA1822

    public string ServiceDataRoot { get; }

    public string ServiceLogs { get; }

    public string AppSettingsFile { get; }

    public string AppLogs { get; }

    /// <summary>The folder that holds the app settings file; the settings row opens it.</summary>
    public string AppSettingsFolder => _app.Root;

    public string VersionLabel => L.AboutVersion(VersionText);

    public string? ServiceVersionText =>
        _gateway.Status is { IsConnected: true, ServiceVersion: { Length: > 0 } version } ? L.AboutServiceVersion(version) : null;

    public string? AuthorizationText =>
        _gateway.Status is { IsConnected: true, OperatorsMode: { } mode } ? L.OperatorsModeText(mode) : null;

    public bool IsDevelopmentMode => _gateway.Status is { IsConnected: true, ServerVerified: false };

    public string DevelopmentNote => L.AboutDevelopmentNote;

    public string UpdateStatus => _status switch
    {
        Status.Checking => L.AboutChecking,
        Status.UpToDate => L.AboutUpToDate,
        Status.NoReleases => L.AboutNoReleases,
        Status.Failed => L.AboutCheckFailed,
        Status.Available => L.AboutUpdateAvailable(AvailableVersionText),
        _ => string.Empty,
    };

    /// <summary>The release page of an available update, or null.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenReleaseCommand))]
    [NotifyPropertyChangedFor(nameof(OpenReleaseName))]
    public partial Uri? AvailableReleaseUrl { get; private set; }

    public string OpenReleaseName => L.AboutOpenReleaseName(AvailableVersionText);

    public string OpenRepositoryName => L.AboutOpenRepositoryName;

    public string ServiceDataName => L.AboutOpenFolderName(L.AboutServiceDataLabel);

    public string ServiceLogsName => L.AboutOpenFolderName(L.AboutServiceLogsLabel);

    public string AppSettingsName => L.AboutOpenFolderName(L.AboutAppSettingsLabel);

    public string AppLogsName => L.AboutOpenFolderName(L.AboutAppLogsLabel);

    /// <summary>Why the last open failed, or null. Kept until an open works; never cleared before the next attempt.</summary>
    public string? ErrorText => _folderMissing ? L.AboutFolderMissing : _openFailed ? L.AboutCouldNotOpen : null;

    private string AvailableVersionText => _availableVersion?.ToString() ?? string.Empty;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _updates.UpdateFound -= OnUpdateFound;
        _gateway.ConnectionChanged -= OnConnectionChanged;
        L.PropertyChanged -= OnLocalizerChanged;
        _cts.Cancel();
        _cts.Dispose();
    }

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        SetStatus(Status.Checking);
        AvailableReleaseUrl = null;
        UpdateCheckResult result;
        try
        {
            result = await _updates.CheckNowAsync(_cts.Token).ConfigureAwait(true);
        }
        catch (Exception)
        {
            SetStatus(Status.Failed);
            return;
        }

        Apply(result);
    }

    [RelayCommand(CanExecute = nameof(CanOpenRelease))]
    private void OpenRelease()
    {
        if (AvailableReleaseUrl is { } url)
        {
            TryLaunch(() => _launcher.OpenUrl(url));
        }
    }

    [RelayCommand]
    private void OpenRepository() => TryLaunch(() => _launcher.OpenUrl(new Uri(Repository)));

    [RelayCommand]
    private void OpenFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        // A folder the service has not created yet (it has not run) is an expected state, not a failure of the shell.
        if (!Directory.Exists(path))
        {
            SetFolderState(missing: true, failed: false);
            return;
        }

        TryLaunch(() => _launcher.OpenFolder(path));
    }

    private bool CanOpenRelease() => AvailableReleaseUrl is not null;

    private void TryLaunch(Action launch)
    {
        try
        {
            launch();
        }
        catch (Exception)
        {
            // No handler or a shell failure: say so briefly, never crash the UI.
            SetFolderState(missing: false, failed: true);
            return;
        }

        SetFolderState(missing: false, failed: false);
    }

    private void SetFolderState(bool missing, bool failed)
    {
        if (_folderMissing == missing && _openFailed == failed)
        {
            return;
        }

        _folderMissing = missing;
        _openFailed = failed;
        OnPropertyChanged(nameof(ErrorText));
    }

    private void Apply(UpdateCheckResult result)
    {
        switch (result.Outcome)
        {
            case UpdateCheckOutcome.UpdateAvailable when result.LatestVersion is not null:
                _availableVersion = result.LatestVersion;
                AvailableReleaseUrl = result.ReleaseUrl;
                SetStatus(Status.Available);
                break;
            case UpdateCheckOutcome.UpToDate:
                SetStatus(Status.UpToDate);
                break;
            case UpdateCheckOutcome.NoReleases:
                SetStatus(Status.NoReleases);
                break;
            default:
                SetStatus(Status.Failed);
                break;
        }
    }

    private void SetStatus(Status status)
    {
        _status = status;
        OnPropertyChanged(nameof(UpdateStatus));
    }

    // The checker's timer runs on a pool thread, so its result reaches view-model state only through the dispatcher.
    private void OnUpdateFound(object? sender, UpdateCheckResult result) => _ui.Post(() => Apply(result));

    private void OnConnectionChanged(object? sender, GatewayStatus status)
    {
        OnPropertyChanged(nameof(ServiceVersionText));
        OnPropertyChanged(nameof(AuthorizationText));
        OnPropertyChanged(nameof(IsDevelopmentMode));
    }

    // An empty name refreshes every text, each rebuilt from the stored outcome.
    private void OnLocalizerChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(string.Empty);
}
