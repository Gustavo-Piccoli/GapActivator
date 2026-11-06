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
    private readonly IAppSettingsService _settings;
    private readonly IShellLauncher _launcher;
    private readonly IUiDispatcher _ui;
    private readonly AppPaths _app;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;
    private Status _status = Status.None;
    private Version? _availableVersion;
    private bool _folderMissing;
    private bool _folderFailed;
    private bool _releaseFailed;
    private bool _repositoryFailed;

    public AboutViewModel(Localizer l, IServiceGateway gateway, IUpdateChecker updates, IAppSettingsService settings, IShellLauncher launcher, AppPaths app, ServicePaths service, IUiDispatcher ui, string version)
        : base(l)
    {
        _gateway = gateway;
        _updates = updates;
        _settings = settings;
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
        updates.Checked += OnChecked;
        settings.Changed += OnSettingsChanged;
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
        _ => L.AboutNotChecked,
    };

    /// <summary>Says the automatic check is off (the app setting); null while it is on.</summary>
    public string? AutomaticChecksNote => _settings.Current.CheckForUpdates ? null : L.AboutAutomaticChecksOff;

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

    /// <summary>Why the last folder open failed, or null. Kept until a folder opens; never cleared before the next attempt.</summary>
    public string? FolderErrorText => _folderMissing ? L.AboutFolderMissing : _folderFailed ? L.AboutCouldNotOpen : null;

    /// <summary>Shown beside the release button when the release page could not be opened; cleared only when it opens.</summary>
    public string? ReleaseErrorText => _releaseFailed ? L.AboutCouldNotOpenPage : null;

    /// <summary>Shown beside the repository button when the page could not be opened; cleared only when it opens.</summary>
    public string? RepositoryErrorText => _repositoryFailed ? L.AboutCouldNotOpenPage : null;

    private string AvailableVersionText => _availableVersion?.ToString() ?? string.Empty;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _updates.UpdateFound -= OnUpdateFound;
        _updates.Checked -= OnChecked;
        _settings.Changed -= OnSettingsChanged;
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
            var opened = TryLaunch(() => _launcher.OpenUrl(url));
            SetLinkState(ref _releaseFailed, !opened, nameof(ReleaseErrorText));
        }
    }

    [RelayCommand]
    private void OpenRepository()
    {
        var opened = TryLaunch(() => _launcher.OpenUrl(new Uri(Repository)));
        SetLinkState(ref _repositoryFailed, !opened, nameof(RepositoryErrorText));
    }

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

        var opened = TryLaunch(() => _launcher.OpenFolder(path));
        SetFolderState(missing: false, failed: !opened);
    }

    private bool CanOpenRelease() => AvailableReleaseUrl is not null;

    // No handler or a shell failure is told briefly by the caller, never thrown into the UI.
    private static bool TryLaunch(Action launch)
    {
        try
        {
            launch();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void SetLinkState(ref bool field, bool failed, string property)
    {
        if (field == failed)
        {
            return;
        }

        field = failed;
        OnPropertyChanged(property);
    }

    private void SetFolderState(bool missing, bool failed)
    {
        if (_folderMissing == missing && _folderFailed == failed)
        {
            return;
        }

        _folderMissing = missing;
        _folderFailed = failed;
        OnPropertyChanged(nameof(FolderErrorText));
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

    // The background check finishes on a pool thread, so its result also reaches this page only through the dispatcher.
    private void OnChecked(object? sender, UpdateCheckResult result) => _ui.Post(() => Apply(result));

    private void OnSettingsChanged(object? sender, AppSettings settings) => _ui.Post(() => OnPropertyChanged(nameof(AutomaticChecksNote)));

    private void OnConnectionChanged(object? sender, GatewayStatus status)
    {
        OnPropertyChanged(nameof(ServiceVersionText));
        OnPropertyChanged(nameof(AuthorizationText));
        OnPropertyChanged(nameof(IsDevelopmentMode));
    }

    // An empty name refreshes every text, each rebuilt from the stored outcome.
    private void OnLocalizerChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(string.Empty);
}
