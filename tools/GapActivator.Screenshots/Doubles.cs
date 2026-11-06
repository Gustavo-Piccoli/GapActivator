using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Settings;
using GapActivator.App.Updates;
using GapActivator.Ipc.Contracts;

namespace GapActivator.Screenshots;

// The App test fakes live in an xUnit test project, which a tool must not reference, so the few doubles the
// screenshots need are written out here. They serve synthetic data and never reach a service.

/// <summary>A gateway that is connected as an operator and serves the data the scene hands it.</summary>
internal sealed class ScreenshotGateway : IServiceGateway
{
    public event EventHandler<GatewayStatus>? ConnectionChanged
    {
        add { }
        remove { }
    }

    public event EventHandler<NotificationDto>? NotificationReceived
    {
        add { }
        remove { }
    }

    public event EventHandler<StateChangedDto>? StateChanged
    {
        add { }
        remove { }
    }

    public GatewayStatus Status { get; } =
        new(ConnectionState.Connected, true, new CallerDto("EXAMPLE\\operator", true), OperatorsGroupMode.Enabled, "0.1.0", null);

    public DashboardDto Dashboard { get; set; } = new(false, [], [], []);

    public List<ProcessSummaryDto> Processes { get; } = [];

    public Dictionary<Guid, ProcessDetailsDto> Details { get; } = [];

    public List<RunRowDto> Runs { get; } = [];

    public Dictionary<Guid, RunDetailsDto> RunDetails { get; } = [];

    public required SettingsViewDto Settings { get; set; }

    public Func<DateTimeOffset, int, IReadOnlyList<DateTimeOffset>> Preview { get; set; } = (_, _) => [];

    public void Start()
    {
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default) => Task.FromResult(Dashboard);

    public Task<IReadOnlyList<ProcessSummaryDto>> ListProcessesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ProcessSummaryDto>>([.. Processes]);

    public Task<ProcessDetailsDto> GetProcessAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Details[id]);

    public Task<RunRowPageDto> ListRunsAsync(RunFilterDto filter, CancellationToken ct = default) =>
        Task.FromResult(new RunRowPageDto([.. Runs], Runs.Count));

    public Task<RunDetailsDto> GetRunAsync(Guid id, CancellationToken ct = default) => Task.FromResult(RunDetails[id]);

    public Task<SettingsViewDto> GetSettingsAsync(CancellationToken ct = default) => Task.FromResult(Settings);

    public Task<IReadOnlyList<DateTimeOffset>> PreviewScheduleAsync(string rule, DateTimeOffset anchor, int count, CancellationToken ct = default) =>
        Task.FromResult(Preview(anchor, count));

    public Task<ProcessSavedDto> RegisterProcessAsync(ProcessDefinitionDto definition, CancellationToken ct = default) => Unused<ProcessSavedDto>();

    public Task<ProcessSavedDto> UpdateProcessAsync(Guid id, ProcessDefinitionDto definition, CancellationToken ct = default) => Unused<ProcessSavedDto>();

    public Task DeleteProcessAsync(Guid id, CancellationToken ct = default) => Unused<int>();

    public Task EnableProcessAsync(Guid id, CancellationToken ct = default) => Unused<int>();

    public Task DisableProcessAsync(Guid id, bool cancelActiveRun, CancellationToken ct = default) => Unused<int>();

    public Task UnblockProcessAsync(Guid id, bool runNow, CancellationToken ct = default) => Unused<int>();

    public Task<Guid> RunNowAsync(Guid processId, CancellationToken ct = default) => Unused<Guid>();

    public Task CancelRunAsync(Guid runId, CancellationToken ct = default) => Unused<int>();

    public Task RestartRunAsync(Guid processId, CancellationToken ct = default) => Unused<int>();

    public Task PauseInstallationAsync(CancellationToken ct = default) => Unused<int>();

    public Task ResumeInstallationAsync(CancellationToken ct = default) => Unused<int>();

    public Task UpdateSettingsAsync(SettingsInputDto settings, CancellationToken ct = default) => Unused<int>();

    public Task SendTestEmailAsync(EmailInputDto email, CancellationToken ct = default) => Unused<int>();

    public Task RestartServiceAsync(CancellationToken ct = default) => Unused<int>();

    private static Task<T> Unused<T>() => throw new NotSupportedException("The screenshot tool only reads.");
}

internal sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}

internal sealed class InMemoryAppSettings : IAppSettingsService
{
    public event EventHandler<AppSettings>? Changed;

    public AppSettings Current { get; private set; } = new(AppTheme.System, AppLanguage.English, true, true, null, null);

    public void Update(Func<AppSettings, AppSettings> change)
    {
        Current = change(Current);
        Changed?.Invoke(this, Current);
    }
}

internal sealed class NoDialogs : IDialogService
{
    public Task<DialogChoice> AskAsync(string title, string message, string primary, string? secondary, string cancel) =>
        Task.FromResult(DialogChoice.Cancel);

    public Task<bool> ConfirmAsync(string title, string message, string confirm) => Task.FromResult(false);
}

internal sealed class NoFilePicker : IFilePicker
{
    public Task<string?> PickExecutableAsync() => Task.FromResult<string?>(null);
}

internal sealed class NoClipboard : IClipboardService
{
    public Task SetTextAsync(string text) => Task.CompletedTask;
}

internal sealed class NoShell : IShellLauncher
{
    public void OpenFolder(string path)
    {
    }

    public void OpenUrl(Uri url)
    {
    }
}

internal sealed class QuietUpdateChecker : IUpdateChecker
{
    public UpdateCheckResult? Last => null;

    public event EventHandler<UpdateCheckResult>? UpdateFound
    {
        add { }
        remove { }
    }

    public event EventHandler<UpdateCheckResult>? Checked
    {
        add { }
        remove { }
    }

    public Task<UpdateCheckResult> CheckNowAsync(CancellationToken ct) =>
        Task.FromResult(new UpdateCheckResult(UpdateCheckOutcome.UpToDate, new Version(0, 1, 0), null));
}

internal sealed class NoServiceStarter : IServiceStarter
{
    public Task<ServiceStartOutcome> StartAsync(CancellationToken ct) =>
        Task.FromResult(new ServiceStartOutcome(ServiceStartResult.Started, 0));
}
