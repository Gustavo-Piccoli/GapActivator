using CommunityToolkit.Mvvm.Input;
using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.ViewModels;
using GapActivator.Ipc;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.Tests.ViewModels;

public class DashboardViewModelTests
{
    private readonly DashboardHarness _h = new();

    private DashboardViewModel Vm => _h.Dashboard;

    private static GatewayStatus Caller(string name, bool canChange, OperatorsGroupMode mode) =>
        new(ConnectionState.Connected, true, new CallerDto(name, canChange), mode, "0.0.0", null);

    private async Task LoadAsync(DashboardDto dashboard)
    {
        _h.Gateway.Dashboard = dashboard;
        await _h.Live.RefreshAsync();
    }

    private static Task Run(IRelayCommand command) =>
        command is IAsyncRelayCommand async ? async.ExecuteAsync(null) : Task.Run(() => command.Execute(null));

    [Fact]
    public void Dashboard_ShowsCallerRightsAndOperatorsMode()
    {
        _h.Gateway.SetStatus(Caller("TEST\\viewer", false, OperatorsGroupMode.Disabled));

        Vm.CallerText.ShouldBe(_h.L.CallerViewOnly("TEST\\viewer"));
        Vm.OperatorsModeText.ShouldBe(_h.L.OperatorsModeText(OperatorsGroupMode.Disabled));
        Vm.OperatorsModeText.ShouldStartWith("Operators mode is off");
        Vm.ConnectionText.ShouldBe("Connected to the service");
    }

    [Fact]
    public void Dashboard_Operator_SaysTheCallerCanChange()
    {
        _h.Gateway.SetStatus(Caller("TEST\\operator", true, OperatorsGroupMode.Enabled));

        Vm.CallerText.ShouldBe("TEST\\operator: you can change processes and settings.");
    }

    [Fact]
    public void Dashboard_Disconnected_ShowsTheBannerTextAndNoCaller()
    {
        _h.Gateway.SetStatus(FakeServiceGateway.Stopped);

        Vm.ConnectionText.ShouldBe("The GapActivator service is stopped.");
        Vm.IsConnected.ShouldBeFalse();
        Vm.CallerText.ShouldBeNull();
        Vm.OperatorsModeText.ShouldBeNull();
    }

    [Fact]
    public async Task ActiveRuns_MapNameStateAndStart()
    {
        RunRowDto run = Dtos.RunRow("Nightly", state: RunState.Running, outcome: null, startedAt: Dtos.Epoch);

        await LoadAsync(Dtos.Dashboard(activeRuns: [run]));

        ActiveRunItemViewModel item = Vm.ActiveRuns.ShouldHaveSingleItem();
        item.ProcessName.ShouldBe("Nightly");
        item.StateText.ShouldBe("Running");
        item.StartedText.ShouldBe(_h.Times.DateTime(Dtos.Epoch));
        item.ActionText.ShouldBe("Cancel");
        Vm.HasActiveRuns.ShouldBeTrue();
    }

    [Fact]
    public async Task ActiveRuns_ServiceKind_OffersStop()
    {
        await LoadAsync(Dtos.Dashboard(activeRuns: [Dtos.RunRow("Svc", kind: ProcessKind.Service, state: RunState.Running, outcome: null)]));

        Vm.ActiveRuns.ShouldHaveSingleItem().ActionText.ShouldBe("Stop");
    }

    [Fact]
    public async Task ActiveRuns_RefreshWithTheSameRun_KeepsTheItem()
    {
        RunRowDto run = Dtos.RunRow("Nightly", state: RunState.Launching, outcome: null);
        await LoadAsync(Dtos.Dashboard(activeRuns: [run]));
        ActiveRunItemViewModel first = Vm.ActiveRuns[0];

        await LoadAsync(Dtos.Dashboard(activeRuns: [run with { State = RunState.Running }]));

        Vm.ActiveRuns.ShouldHaveSingleItem().ShouldBeSameAs(first);
        first.StateText.ShouldBe("Running");
    }

    [Fact]
    public async Task Upcoming_MapsNameAndTime()
    {
        await LoadAsync(Dtos.Dashboard(upcoming: [new UpcomingRunDto(Guid.NewGuid(), "Backup", Dtos.Epoch)]));

        Vm.Upcoming.ShouldHaveSingleItem().ShouldBe(new UpcomingItem("Backup", _h.Times.WithWeekday(Dtos.Epoch)));
        Vm.HasUpcoming.ShouldBeTrue();
    }

    [Fact]
    public async Task Cancel_RunToCompletion_SendsCancelRun()
    {
        var runId = Guid.NewGuid();
        await LoadAsync(Dtos.Dashboard(activeRuns: [Dtos.RunRow(id: runId, state: RunState.Running, outcome: null)]));

        await Run(Vm.ActiveRuns[0].CancelCommand);

        _h.Gateway.Calls.ShouldContain(c => c.Method == nameof(IServiceGateway.CancelRunAsync) && (Guid)c.Args[0]! == runId);
        _h.Dialogs.Asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task Stop_ServiceKind_ConfirmsThenDisablesWithCancel()
    {
        var processId = Guid.NewGuid();
        await LoadAsync(Dtos.Dashboard(activeRuns: [Dtos.RunRow("Svc", processId: processId, kind: ProcessKind.Service, state: RunState.Running, outcome: null)]));
        _h.Dialogs.Enqueue(DialogChoice.Primary);

        await Run(Vm.ActiveRuns[0].CancelCommand);

        var asked = _h.Dialogs.Asked.ShouldHaveSingleItem();
        asked.Title.ShouldBe("Stop the service-kind process");
        asked.Message.ShouldBe("Stopping \"Svc\" disables it. It stays stopped until you enable it again.");
        asked.Primary.ShouldBe("Stop");
        _h.Gateway.Calls.ShouldContain(c => c.Method == nameof(IServiceGateway.DisableProcessAsync) && (Guid)c.Args[0]! == processId && (bool)c.Args[1]!);
    }

    [Fact]
    public async Task Stop_ServiceKind_Declined_SendsNothing()
    {
        await LoadAsync(Dtos.Dashboard(activeRuns: [Dtos.RunRow("Svc", kind: ProcessKind.Service, state: RunState.Running, outcome: null)]));
        _h.Dialogs.Enqueue(DialogChoice.Cancel);

        await Run(Vm.ActiveRuns[0].CancelCommand);

        _h.Gateway.Calls.ShouldNotContain(c => c.Method == nameof(IServiceGateway.DisableProcessAsync) || c.Method == nameof(IServiceGateway.CancelRunAsync));
    }

    [Fact]
    public async Task StopAsync_ServiceKind_DisablesWithoutAsking()
    {
        var processId = Guid.NewGuid();

        await _h.Runs.StopAsync(Guid.NewGuid(), processId, ProcessKind.Service);

        _h.Dialogs.Asked.ShouldBeEmpty();
        _h.Gateway.Calls.ShouldContain(c => c.Method == nameof(IServiceGateway.DisableProcessAsync) && (Guid)c.Args[0]! == processId && (bool)c.Args[1]!);
    }

    [Fact]
    public async Task Cancel_Viewer_CannotExecute()
    {
        await LoadAsync(Dtos.Dashboard(activeRuns: [Dtos.RunRow(state: RunState.Running, outcome: null)]));
        _h.Gateway.SetStatus(FakeServiceGateway.Viewer());

        Vm.ActiveRuns[0].CancelCommand.CanExecute(null).ShouldBeFalse();
        Vm.ActiveRuns[0].DeniedReason.ShouldBe("Requires Administrators or GapActivator Operators");
        Vm.PauseCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task Cancel_Operator_CanExecuteAfterReconnecting()
    {
        _h.Gateway.SetStatus(FakeServiceGateway.Viewer());
        await LoadAsync(Dtos.Dashboard(activeRuns: [Dtos.RunRow(state: RunState.Running, outcome: null)]));
        Vm.ActiveRuns[0].CancelCommand.CanExecute(null).ShouldBeFalse();

        _h.Gateway.SetStatus(FakeServiceGateway.Operator());

        Vm.ActiveRuns[0].CancelCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task PauseAndResume_SendTheCommandsAndFollowIsPaused()
    {
        await LoadAsync(Dtos.Dashboard(isPaused: false));
        Vm.IsPaused.ShouldBeFalse();
        Vm.PauseCommand.CanExecute(null).ShouldBeTrue();
        Vm.ResumeCommand.CanExecute(null).ShouldBeFalse();

        await Run(Vm.PauseCommand);
        await LoadAsync(Dtos.Dashboard(isPaused: true));

        _h.Gateway.Calls.ShouldContain(c => c.Method == nameof(IServiceGateway.PauseInstallationAsync));
        Vm.IsPaused.ShouldBeTrue();
        Vm.PausedText.ShouldBe("GapActivator is paused: no new run starts.");
        Vm.PauseCommand.CanExecute(null).ShouldBeFalse();
        Vm.ResumeCommand.CanExecute(null).ShouldBeTrue();

        await Run(Vm.ResumeCommand);

        _h.Gateway.Calls.ShouldContain(c => c.Method == nameof(IServiceGateway.ResumeInstallationAsync));
    }

    [Fact]
    public async Task RecentFailure_Open_NavigatesToTheRun()
    {
        var runId = Guid.NewGuid();
        await LoadAsync(Dtos.Dashboard(recentFailures: [Dtos.RunRow("Broken", id: runId, outcome: RunOutcome.Failed)]));
        NavigationRequest? request = null;
        _h.Navigator.Requested += (_, r) => request = r;

        FailureItemViewModel item = Vm.RecentFailures.ShouldHaveSingleItem();
        item.OpenCommand.Execute(null);

        item.ProcessName.ShouldBe("Broken");
        item.OutcomeText.ShouldBe("Failed");
        item.EndedText.ShouldBe(_h.Times.DateTime(Dtos.Epoch.AddSeconds(5)));
        request.ShouldBe(new NavigationRequest(AppPage.History, runId));
    }

    [Fact]
    public async Task RecentFailure_IsUnseen_FollowsTheAcknowledgements()
    {
        await LoadAsync(Dtos.Dashboard(recentFailures: [Dtos.RunRow("Broken", outcome: RunOutcome.Failed)]));
        FailureItemViewModel item = Vm.RecentFailures[0];
        item.IsUnseen.ShouldBeTrue();

        _h.Time.Advance(TimeSpan.FromHours(1));
        _h.Seen.MarkHistorySeen();

        item.IsUnseen.ShouldBeFalse();
    }

    [Fact]
    public async Task Cancel_Rejected_ShowsTheLocalizedError()
    {
        await LoadAsync(Dtos.Dashboard(activeRuns: [Dtos.RunRow(state: RunState.Running, outcome: null)]));
        _h.Gateway.FailNext(nameof(IServiceGateway.CancelRunAsync), new GapActivatorRequestException(IpcErrorCodes.RunNotActive, "x", retryable: false));

        await Run(Vm.ActiveRuns[0].CancelCommand);

        Vm.ErrorText.ShouldBe("There is no active run to cancel.");
    }

    [Fact]
    public async Task Language_Switched_RebuildsTheCachedTexts()
    {
        var upcoming = new UpcomingRunDto(Guid.NewGuid(), "Backup", Dtos.Epoch);
        await LoadAsync(Dtos.Dashboard(
            activeRuns: [Dtos.RunRow(kind: ProcessKind.Service, state: RunState.Running, outcome: null)],
            upcoming: [upcoming],
            recentFailures: [Dtos.RunRow(outcome: RunOutcome.TimedOut)]));
        _h.Gateway.SetStatus(Caller("TEST\\viewer", false, OperatorsGroupMode.Enabled));

        _h.L.SetLanguage(AppLanguage.Portuguese);

        Vm.ActiveRuns[0].StateText.ShouldBe("Em execução");
        Vm.ActiveRuns[0].ActionText.ShouldBe("Parar");
        Vm.ActiveRuns[0].StartedText.ShouldBe("01/01/2030 12:00");
        Vm.Upcoming[0].AtText.ShouldBe("ter. 01/01/2030 12:00");
        Vm.RecentFailures[0].OutcomeText.ShouldBe("Tempo esgotado");
        Vm.RecentFailures[0].EndedText.ShouldBe("01/01/2030 12:00");
        Vm.ConnectionText.ShouldBe("Conectado ao serviço");
        Vm.CallerText.ShouldBe("TEST\\viewer: você pode apenas visualizar.");
        Vm.OperatorsModeText.ShouldStartWith("Modo Operators ativado");
        Vm.PausedText.ShouldBe("O GapActivator está pausado: nenhuma nova execução é iniciada.");
    }
}
