using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace GapActivator.App.Tests.Fakes;

/// <summary>Builds the dashboard view model with fakes, and exposes the pieces a test drives.</summary>
internal sealed class DashboardHarness
{
    public DashboardHarness(AppLanguage language = AppLanguage.English, FakeServiceGateway? gateway = null, Localizer? localizer = null, Navigator? navigator = null, TimeZoneInfo? zone = null)
    {
        L = localizer ?? new Localizer(language);
        Gateway = gateway ?? new FakeServiceGateway();
        Navigator = navigator ?? new Navigator();
        Permissions = new Permissions(L, Gateway);
        Times = new TimeDisplay(L, TimeZoneInfo.Utc);
        Live = new LiveState(Gateway, new ListLogger<LiveState>());
        Seen = new FailureAcknowledgements(Time);
        Runs = new RunActions(L, Gateway, Dialogs);
        Dashboard = new DashboardViewModel(L, Gateway, Live, Permissions, Runs, Seen, Navigator, Times);
        Actions = new ProcessActions(L, Gateway, Dialogs, Runs, Time);
        Processes = new ProcessesViewModel(L, Gateway, Live, Permissions, Actions, Runs, Navigator, Times, NewEditor);
        History = new HistoryViewModel(L, Gateway, Live, Seen, Navigator, Times, zone ?? TimeZoneInfo.Utc, NewDetail);
        Service = new ServiceActions(L, Gateway, Dialogs);
        Settings = new SettingsViewModel(L, Gateway, Permissions, AppSettings, Service);
    }

    private ProcessEditorViewModel NewEditor() =>
        new(
            L,
            Gateway,
            Permissions,
            Dialogs,
            Seen,
            Times,
            TimeZoneInfo.Utc,
            () => new ScheduleEditorViewModel(L, Gateway, Time, TimeZoneInfo.Utc, Times, new ImmediateUiDispatcher()),
            Files);

    private RunDetailViewModel NewDetail() => new(L, Gateway, Time, Times, Clipboard, new ImmediateUiDispatcher());

    public FakeClipboardService Clipboard { get; } = new();

    public FakeFilePicker Files { get; } = new();

    public Localizer L { get; }

    public FakeServiceGateway Gateway { get; }

    public ScriptedDialogService Dialogs { get; } = new();

    public Navigator Navigator { get; }

    public FakeTimeProvider Time { get; } = new(Dtos.Epoch);

    public Permissions Permissions { get; }

    public TimeDisplay Times { get; }

    public LiveState Live { get; }

    public FailureAcknowledgements Seen { get; }

    public RunActions Runs { get; }

    public DashboardViewModel Dashboard { get; }

    public ProcessActions Actions { get; }

    public ProcessesViewModel Processes { get; }

    public HistoryViewModel History { get; }

    public FakeAppSettings AppSettings { get; } = new();

    public ServiceActions Service { get; }

    public SettingsViewModel Settings { get; }
}
