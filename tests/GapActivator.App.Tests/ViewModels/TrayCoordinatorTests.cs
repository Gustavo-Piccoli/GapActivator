using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Shell;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.ViewModels;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.Tests.ViewModels;

public class TrayCoordinatorTests : IDisposable
{
    private readonly DashboardHarness _h = new();
    private readonly FakeTrayIcon _tray = new();
    private readonly FakeMainWindowService _window = new();
    private readonly FakeAppLifetime _lifetime = new();
    private readonly TrayCoordinator _coordinator;

    public TrayCoordinatorTests()
    {
        _coordinator = new TrayCoordinator(
            _h.L,
            _h.Gateway,
            _h.Live,
            _h.Seen,
            _tray,
            _window,
            _lifetime,
            _h.Service,
            _h.Navigator,
            new ImmediateUiDispatcher());
        _coordinator.Start();
    }

    public void Dispose()
    {
        _coordinator.Dispose();
        GC.SuppressFinalize(this);
    }

    private static RunRowDto Failure(Guid? processId = null, DateTimeOffset? endedAt = null) =>
        Dtos.RunRow(
            processId: processId,
            state: RunState.Completed,
            outcome: RunOutcome.Failed,
            endedAt: endedAt ?? Dtos.Epoch.AddMinutes(1));

    private static RunRowDto Running() => Dtos.RunRow(state: RunState.Running, outcome: null);

    [Fact]
    public void Compute_Disconnected_IsUnreachable()
    {
        var state = TrayCoordinator.Compute(FakeServiceGateway.Stopped, Dtos.Dashboard(activeRuns: [Running()]), [], _h.Seen);

        state.ShouldBe(TrayIconState.Unreachable);
    }

    [Fact]
    public void Compute_BlockedProcess_IsAttention()
    {
        var blocked = Dtos.Summary(lifecycle: ProcessLifecycle.Blocked);

        var state = TrayCoordinator.Compute(FakeServiceGateway.Operator(), Dtos.Dashboard(), [blocked], _h.Seen);

        state.ShouldBe(TrayIconState.Attention);
    }

    [Fact]
    public void Compute_UnseenFailure_IsAttentionUntilHistoryIsOpened()
    {
        var dashboard = Dtos.Dashboard(recentFailures: [Failure()]);

        TrayCoordinator.Compute(FakeServiceGateway.Operator(), dashboard, [], _h.Seen).ShouldBe(TrayIconState.Attention);

        _h.Time.Advance(TimeSpan.FromDays(1));
        _h.Seen.MarkHistorySeen();

        TrayCoordinator.Compute(FakeServiceGateway.Operator(), dashboard, [], _h.Seen).ShouldBe(TrayIconState.Healthy);
    }

    [Fact]
    public void Compute_UnseenFailure_IsAttentionUntilThatProcessIsOpened()
    {
        var processId = Guid.NewGuid();
        var dashboard = Dtos.Dashboard(recentFailures: [Failure(processId)]);

        _h.Time.Advance(TimeSpan.FromDays(1));
        _h.Seen.MarkProcessSeen(Guid.NewGuid());
        TrayCoordinator.Compute(FakeServiceGateway.Operator(), dashboard, [], _h.Seen).ShouldBe(TrayIconState.Attention);

        _h.Seen.MarkProcessSeen(processId);
        TrayCoordinator.Compute(FakeServiceGateway.Operator(), dashboard, [], _h.Seen).ShouldBe(TrayIconState.Healthy);
    }

    [Fact]
    public void Compute_ActiveRuns_IsBusy()
    {
        var dashboard = Dtos.Dashboard(activeRuns: [Running()]);

        TrayCoordinator.Compute(FakeServiceGateway.Operator(), dashboard, [], _h.Seen).ShouldBe(TrayIconState.Busy);
    }

    [Fact]
    public void Compute_AttentionBeatsBusy()
    {
        var dashboard = Dtos.Dashboard(activeRuns: [Running()], recentFailures: [Failure()]);

        TrayCoordinator.Compute(FakeServiceGateway.Operator(), dashboard, [], _h.Seen).ShouldBe(TrayIconState.Attention);
    }

    [Fact]
    public void Compute_Idle_IsHealthy()
    {
        TrayCoordinator.Compute(FakeServiceGateway.Operator(), Dtos.Dashboard(), [Dtos.Summary()], _h.Seen).ShouldBe(TrayIconState.Healthy);
        TrayCoordinator.Compute(FakeServiceGateway.Operator(), null, [], _h.Seen).ShouldBe(TrayIconState.Healthy);
    }

    [Fact]
    public void Start_WhenTheServiceIsDown_ShowsTheUnreachableIconWithItsWord()
    {
        _h.Gateway.SetStatus(FakeServiceGateway.Stopped);

        _tray.LastState.ShouldBe(TrayIconState.Unreachable);
        _tray.LastTooltip.ShouldBe("GapActivator — service unreachable");
    }

    [Fact]
    public async Task Tooltip_FollowsTheStateAndTheLanguageLive()
    {
        _h.Gateway.Dashboard = Dtos.Dashboard(activeRuns: [Running(), Running()]);

        await _h.Live.RefreshAsync();

        _tray.LastState.ShouldBe(TrayIconState.Busy);
        _tray.LastTooltip.ShouldBe("GapActivator — 2 running");

        _h.L.SetLanguage(AppLanguage.Portuguese);

        _tray.LastTooltip.ShouldBe("GapActivator — 2 em execução");
    }

    [Fact]
    public async Task Tooltip_HealthyWhilePaused_SaysPausedAndOtherwiseIdle()
    {
        await _h.Live.RefreshAsync();
        _tray.LastTooltip.ShouldBe("GapActivator — idle");

        _h.Gateway.Dashboard = Dtos.Dashboard(isPaused: true);
        await _h.Live.RefreshAsync();

        _tray.LastState.ShouldBe(TrayIconState.Healthy);
        _tray.LastTooltip.ShouldBe("GapActivator — paused");
    }

    [Fact]
    public async Task Icon_FollowsTheFailureAcknowledgements()
    {
        _h.Gateway.Dashboard = Dtos.Dashboard(recentFailures: [Failure()]);
        await _h.Live.RefreshAsync();
        _tray.LastState.ShouldBe(TrayIconState.Attention);
        _tray.LastTooltip.ShouldBe("GapActivator — needs attention");

        _h.Time.Advance(TimeSpan.FromDays(1));
        _h.Seen.MarkHistorySeen();

        _tray.LastState.ShouldBe(TrayIconState.Healthy);
    }

    [Fact]
    public void Menu_Operator_HasOpenRestartAndQuit()
    {
        _tray.RaiseMenu();

        var items = _tray.Menus.ShouldHaveSingleItem();
        items.Select(i => i.Id).ShouldBe(["open", "restart", "-", "quit"]);
        items[0].Text.ShouldBe("Open GapActivator");
        items[1].Text.ShouldBe("Restart service");
        items[3].Text.ShouldBe("Quit");
    }

    [Fact]
    public void Menu_Viewer_HasNoRestart()
    {
        _h.Gateway.SetStatus(FakeServiceGateway.Viewer());

        _tray.RaiseMenu();

        _tray.Menus.ShouldHaveSingleItem().Select(i => i.Id).ShouldBe(["open", "-", "quit"]);
    }

    [Fact]
    public void Menu_FollowsTheLanguage()
    {
        _h.L.SetLanguage(AppLanguage.Portuguese);

        _tray.RaiseMenu();

        _tray.Menus.ShouldHaveSingleItem().Where(i => i.Id != "-").Select(i => i.Text)
            .ShouldBe(["Abrir o GapActivator", "Reiniciar serviço", "Sair"]);
    }

    [Fact]
    public void Menu_Restart_ConfirmsAndSendsRestartService()
    {
        _h.Dialogs.Enqueue(DialogChoice.Primary);
        _tray.MenuChoice = "restart";

        _tray.RaiseMenu();

        _h.Dialogs.Asked.ShouldHaveSingleItem().Title.ShouldBe(_h.L.RestartServiceTitle);
        _h.Gateway.Calls.Select(c => c.Method).ShouldBe([nameof(IServiceGateway.RestartServiceAsync)]);
    }

    [Fact]
    public void Menu_Restart_Declined_SendsNothing()
    {
        _tray.MenuChoice = "restart";

        _tray.RaiseMenu();

        _h.Gateway.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void Menu_Restart_WhenTheCallFails_DoesNotThrow()
    {
        _h.Dialogs.Enqueue(DialogChoice.Primary);
        _h.Gateway.FailNext(nameof(IServiceGateway.RestartServiceAsync), new InvalidOperationException("down"));
        _tray.MenuChoice = "restart";

        Should.NotThrow(() => _tray.RaiseMenu());
    }

    [Fact]
    public void Menu_Quit_ExitsTheAppOnly()
    {
        _tray.MenuChoice = "quit";

        _tray.RaiseMenu();

        _lifetime.Exits.ShouldBe(1);
        _h.Gateway.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void Menu_Dismissed_DoesNothing()
    {
        _tray.MenuChoice = null;

        _tray.RaiseMenu();

        _window.Shown.ShouldBe(0);
        _lifetime.Exits.ShouldBe(0);
    }

    [Fact]
    public void Menu_OpenAndDoubleClick_ShowTheWindow()
    {
        _tray.MenuChoice = "open";
        _tray.RaiseMenu();
        _window.Shown.ShouldBe(1);

        _tray.RaiseOpen();
        _window.Shown.ShouldBe(2);
    }

    [Fact]
    public void BalloonClick_WithARunTag_ShowsTheWindowAndOpensTheRun()
    {
        var runId = Guid.NewGuid();
        NavigationRequest? request = null;
        _h.Navigator.Requested += (_, r) => request = r;

        _tray.RaiseBalloonClicked($"run:{runId:D}");

        _window.Shown.ShouldBe(1);
        request.ShouldBe(new NavigationRequest(AppPage.History, runId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("run:not-a-guid")]
    [InlineData("update:x")]
    public void BalloonClick_WithoutAUsableRunTag_OnlyShowsTheWindow(string? tag)
    {
        var navigated = false;
        _h.Navigator.Requested += (_, _) => navigated = true;

        _tray.RaiseBalloonClicked(tag);

        _window.Shown.ShouldBe(1);
        navigated.ShouldBeFalse();
    }

    [Fact]
    public void Dispose_StopsFollowingEverything()
    {
        _coordinator.Dispose();
        var before = _tray.States.Count;

        _h.Gateway.SetStatus(FakeServiceGateway.Stopped);
        _h.L.SetLanguage(AppLanguage.Portuguese);
        _tray.RaiseOpen();
        _tray.RaiseBalloonClicked($"run:{Guid.NewGuid():D}");

        _tray.States.Count.ShouldBe(before);
        _window.Shown.ShouldBe(0);
    }
}
