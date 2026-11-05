using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.ViewModels;
using GapActivator.Ipc;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.Tests.ViewModels;

public class HistoryViewModelTests
{
    private static List<RunFilterDto> Filters(DashboardHarness h) =>
        [.. h.Gateway.Calls.Where(c => c.Method == nameof(FakeServiceGateway.ListRunsAsync)).Select(c => (RunFilterDto)c.Args[0]!)];

    [Fact]
    public async Task Load_DefaultFilter_RequestsPage1OfSize50()
    {
        var h = new DashboardHarness();

        await h.History.ApplyCommand.ExecuteAsync(null);

        Filters(h).ShouldBe([new RunFilterDto(null, null, null, null, 1, 50)]);
    }

    [Fact]
    public async Task Filter_ProcessOutcomeAndDates_MapToTheRunFilter()
    {
        var h = new DashboardHarness(zone: Zones.Fixed(TimeSpan.FromHours(-3)));
        var process = Dtos.Summary("Alpha");
        h.Gateway.Processes = [process];
        await h.Live.RefreshAsync();
        h.History.SelectedProcess = h.History.ProcessOptions.Single(o => o.Id == process.Id);
        h.History.SelectedOutcome = h.History.OutcomeOptions.Single(o => o.Outcomes is [RunOutcome.Cancelled]);
        h.History.FromDate = new DateTime(2030, 1, 1);
        h.History.ToDate = new DateTime(2030, 1, 2);

        await h.History.ApplyCommand.ExecuteAsync(null);

        RunFilterDto filter = Filters(h).Single();
        filter.ProcessId.ShouldBe(process.Id);
        filter.Outcomes.ShouldBe([RunOutcome.Cancelled]);
        filter.From.ShouldBe(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.FromHours(-3)));
        filter.To.ShouldBe(new DateTimeOffset(2030, 1, 3, 0, 0, 0, TimeSpan.FromHours(-3)));
    }

    [Fact]
    public async Task Filter_Failures_SendsTheFiveFailureOutcomes()
    {
        var h = new DashboardHarness();
        h.History.SelectedOutcome = h.History.OutcomeOptions[1];

        await h.History.ApplyCommand.ExecuteAsync(null);

        h.History.OutcomeOptions[0].Outcomes.ShouldBeNull();
        h.History.OutcomeOptions[1].Name.ShouldBe("Failures");
        Filters(h).Single().Outcomes.ShouldBe(
            [RunOutcome.Failed, RunOutcome.FailedToStart, RunOutcome.TimedOut, RunOutcome.Abandoned, RunOutcome.Crashed]);
    }

    [Fact]
    public async Task Filter_FromAfterTo_ShowsTheErrorAndSendsNothing()
    {
        var h = new DashboardHarness();
        h.History.FromDate = new DateTime(2030, 1, 5);
        h.History.ToDate = new DateTime(2030, 1, 2);

        await h.History.ApplyCommand.ExecuteAsync(null);

        h.History.ErrorText.ShouldBe("The start date is after the end date.");
        Filters(h).ShouldBeEmpty();
    }

    [Fact]
    public async Task Paging_TotalsAndButtons()
    {
        var h = new DashboardHarness();
        h.Gateway.RunPages = _ => new RunRowPageDto([Dtos.RunRow()], 120);

        await h.History.ApplyCommand.ExecuteAsync(null);
        h.History.PageCount.ShouldBe(3);
        h.History.PreviousPageCommand.CanExecute(null).ShouldBeFalse();
        h.History.NextPageCommand.CanExecute(null).ShouldBeTrue();

        await h.History.NextPageCommand.ExecuteAsync(null);

        h.History.Page.ShouldBe(2);
        h.History.PageText.ShouldBe("Page 2 of 3");
        Filters(h).Last().Page.ShouldBe(2);
        h.History.PreviousPageCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task Paging_UsesTheAppliedFilterNotTheEditedOne()
    {
        var h = new DashboardHarness();
        h.Gateway.RunPages = _ => new RunRowPageDto([Dtos.RunRow()], 120);
        await h.History.ApplyCommand.ExecuteAsync(null);
        h.History.FromDate = new DateTime(2030, 1, 1);

        await h.History.NextPageCommand.ExecuteAsync(null);

        Filters(h).Last().From.ShouldBeNull();
    }

    [Fact]
    public async Task Rows_MapStatusTriggerAndTimes()
    {
        var h = new DashboardHarness();
        var run = Dtos.RunRow("Alpha", outcome: RunOutcome.Failed, exitCode: 2, trigger: RunTrigger.Schedule);
        h.Gateway.RunPages = _ => new RunRowPageDto([run, Dtos.RunRow("Beta", state: RunState.Running, outcome: null, exitCode: null)], 2);

        await h.History.ApplyCommand.ExecuteAsync(null);

        h.History.Items[0].StatusText.ShouldBe("Failed");
        h.History.Items[0].TriggerText.ShouldBe("Scheduled");
        h.History.Items[0].ExitCodeText.ShouldBe("2");
        h.History.Items[0].DurationText.ShouldBe("5 s");
        h.History.Items[1].StatusText.ShouldBe("Running");
        h.History.Items[1].ExitCodeText.ShouldBeNull();
    }

    [Fact]
    public async Task StateChanged_OnPageOne_Refreshes()
    {
        var h = new DashboardHarness();
        await h.History.ApplyCommand.ExecuteAsync(null);
        var before = Filters(h).Count;

        h.Gateway.RaiseStateChanged(new StateChangedDto(null, null));
        await h.History.Refreshing;

        Filters(h).Count.ShouldBe(before + 1);
    }

    [Fact]
    public async Task StateChanged_OnPageTwo_DoesNotRefresh()
    {
        var h = new DashboardHarness();
        h.Gateway.RunPages = _ => new RunRowPageDto([Dtos.RunRow()], 120);
        await h.History.ApplyCommand.ExecuteAsync(null);
        await h.History.NextPageCommand.ExecuteAsync(null);
        var before = Filters(h).Count;

        h.Gateway.RaiseStateChanged(new StateChangedDto(null, null));
        await h.History.Refreshing;

        Filters(h).Count.ShouldBe(before);
    }

    [Fact]
    public async Task Shown_MarksAllFailuresSeen()
    {
        var h = new DashboardHarness();
        var failure = Dtos.RunRow(outcome: RunOutcome.Failed, endedAt: Dtos.Epoch.AddSeconds(-10));
        h.Seen.IsUnseen(failure).ShouldBeTrue();
        h.Time.Advance(TimeSpan.FromMinutes(1));

        h.Navigator.Navigate(AppPage.History);
        await h.History.Refreshing;

        h.Seen.IsUnseen(failure).ShouldBeFalse();
        Filters(h).Count.ShouldBe(1);
    }

    [Fact]
    public async Task NavigatorOpenRun_OpensTheDetail()
    {
        var h = new DashboardHarness();
        var run = Dtos.RunRow("Alpha");
        h.Gateway.Runs[run.Id] = Dtos.RunDetails(run, output: "hello");

        h.Navigator.OpenRun(run.Id);
        await h.History.OpeningDetail;

        h.History.Detail.ShouldNotBeNull();
        h.History.Detail.ProcessName.ShouldBe("Alpha");
        h.History.Detail.Output.ShouldBe("hello");
    }

    [Fact]
    public async Task OpenRunCommand_ThenClose_ClosesTheDetail()
    {
        var h = new DashboardHarness();
        var run = Dtos.RunRow("Alpha", state: RunState.Running, outcome: null);
        h.Gateway.Runs[run.Id] = Dtos.RunDetails(run);
        h.Gateway.RunPages = _ => new RunRowPageDto([run], 1);
        await h.History.ApplyCommand.ExecuteAsync(null);

        await h.History.OpenRunCommand.ExecuteAsync(h.History.Items[0]);
        h.History.Detail.ShouldNotBeNull();
        h.History.CloseDetailCommand.Execute(null);

        h.History.Detail.ShouldBeNull();
    }

    [Fact]
    public async Task Language_Switched_RebuildsTheFilterOptionNames()
    {
        var h = new DashboardHarness();
        var process = Dtos.Summary("Alpha");
        h.Gateway.Processes = [process];
        h.Gateway.RunPages = _ => new RunRowPageDto([Dtos.RunRow(outcome: RunOutcome.Failed)], 1);
        await h.Live.RefreshAsync();
        await h.History.ApplyCommand.ExecuteAsync(null);
        h.History.SelectedProcess = h.History.ProcessOptions.Single(o => o.Id == process.Id);
        h.History.SelectedOutcome = h.History.OutcomeOptions[1];

        h.L.SetLanguage(AppLanguage.Portuguese);

        h.History.ProcessOptions[0].Name.ShouldBe("Todos os processos");
        h.History.OutcomeOptions[0].Name.ShouldBe("Todos os resultados");
        h.History.OutcomeOptions[1].Name.ShouldBe("Falhas");
        h.History.SelectedProcess.Id.ShouldBe(process.Id);
        h.History.SelectedOutcome.Name.ShouldBe("Falhas");
        h.History.Items[0].StatusText.ShouldBe("Falhou");
        h.History.PageText.ShouldBe("Página 1 de 1");
    }

    [Fact]
    public async Task Apply_ServiceFails_ShowsTheErrorAndKeepsTheRows()
    {
        var h = new DashboardHarness();
        h.Gateway.RunPages = _ => new RunRowPageDto([Dtos.RunRow()], 1);
        await h.History.ApplyCommand.ExecuteAsync(null);
        h.Gateway.FailNext(nameof(FakeServiceGateway.ListRunsAsync), new GapActivatorRequestException(IpcErrorCodes.Disconnected, "x", retryable: true));

        await h.History.ApplyCommand.ExecuteAsync(null);

        h.History.ErrorText.ShouldBe("Not connected to the service.");
        h.History.Items.Count.ShouldBe(1);
    }
}
