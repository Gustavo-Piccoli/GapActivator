using CommunityToolkit.Mvvm.Input;
using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.ViewModels;
using GapActivator.Ipc;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.Tests.ViewModels;

public class ProcessesViewModelTests
{
    private readonly DashboardHarness _h = new();

    private ProcessesViewModel Vm => _h.Processes;

    private async Task LoadAsync(params ProcessSummaryDto[] processes)
    {
        _h.Gateway.Processes = [.. processes];
        await _h.Live.RefreshAsync();
    }

    private static ActiveRunViewDto Active() => new(Guid.NewGuid(), RunState.Running, RunTrigger.Manual, Dtos.Epoch, 1);

    // The live-state refresh also reads from the gateway; these are the calls that change something.
    private List<(string Method, object?[] Args)> Sent() =>
        [.. _h.Gateway.Calls.Where(c => c.Method is not ("GetDashboardAsync" or "ListProcessesAsync" or "GetProcessAsync"))];

    private ProcessRowViewModel Row(string name) => Vm.Items.Single(i => i.Name == name);

    [Fact]
    public async Task Items_FollowLiveStateOrderedByName()
    {
        await LoadAsync(Dtos.Summary("beta"), Dtos.Summary("Alpha"), Dtos.Summary("charlie"));

        Vm.Items.Select(i => i.Name).ShouldBe(["Alpha", "beta", "charlie"]);

        await LoadAsync(Dtos.Summary("zeta"), Dtos.Summary("Beta"));

        Vm.Items.Select(i => i.Name).ShouldBe(["Beta", "zeta"]);
    }

    [Fact]
    public async Task Items_KeepTheInstanceOfAProcessThatStays()
    {
        var keep = Dtos.Summary("Keep");
        await LoadAsync(keep);
        ProcessRowViewModel first = Vm.Items.Single();

        await LoadAsync(Dtos.Summary("Aaa"), keep with { Lifecycle = ProcessLifecycle.Disabled });

        Vm.Items[1].ShouldBeSameAs(first);
        first.Lifecycle.ShouldBe(ProcessLifecycle.Disabled);
    }

    [Theory]
    [InlineData(AppLanguage.English, ProcessLifecycle.Enabled, "Enabled", null)]
    [InlineData(AppLanguage.English, ProcessLifecycle.Disabled, "Disabled", "Paused manually")]
    [InlineData(AppLanguage.English, ProcessLifecycle.Blocked, "Blocked", "The failure limit was reached")]
    [InlineData(AppLanguage.Portuguese, ProcessLifecycle.Enabled, "Ativado", null)]
    [InlineData(AppLanguage.Portuguese, ProcessLifecycle.Disabled, "Desativado", "Pausado manualmente")]
    [InlineData(AppLanguage.Portuguese, ProcessLifecycle.Blocked, "Bloqueado", "O limite de falhas foi atingido")]
    public async Task Badge_EachLifecycle_HasTextAndTheFixedTooltip(AppLanguage language, ProcessLifecycle lifecycle, string text, string? tooltip)
    {
        var h = new DashboardHarness(language);
        h.Gateway.Processes = [Dtos.Summary(lifecycle: lifecycle)];
        await h.Live.RefreshAsync();

        ProcessRowViewModel row = h.Processes.Items.Single();

        row.Lifecycle.ShouldBe(lifecycle);
        row.LifecycleText.ShouldBe(text);
        row.LifecycleTooltip.ShouldBe(tooltip);
    }

    [Fact]
    public async Task Row_ShowsKindNextRunAndLastOutcome()
    {
        var next = Dtos.Epoch.AddHours(2);
        await LoadAsync(
            Dtos.Summary("A", nextScheduledAt: next, lastOutcome: RunOutcome.Failed),
            Dtos.Summary("B", kind: ProcessKind.Service));

        ProcessRowViewModel a = Vm.Items[0];
        a.KindText.ShouldBe("Runs to completion");
        a.NextText.ShouldBe($"Next: {_h.Times.DateTime(next)}");
        a.LastOutcomeText.ShouldBe("Failed");
        ProcessRowViewModel b = Vm.Items[1];
        b.KindText.ShouldBe("Service (stays running)");
        b.NextText.ShouldBe("No next run");
        b.LastOutcomeText.ShouldBeNull();
    }

    [Theory]
    [InlineData(ProcessLifecycle.Enabled, false, false, true, false, false, false)]
    [InlineData(ProcessLifecycle.Disabled, false, true, false, false, false, false)]
    [InlineData(ProcessLifecycle.Blocked, false, false, false, true, true, false)]
    [InlineData(ProcessLifecycle.Blocked, true, false, false, true, false, true)]
    [InlineData(ProcessLifecycle.Enabled, true, false, true, false, false, true)]
    public async Task Row_ShowFlagsFollowLifecycleAndActiveRun(
        ProcessLifecycle lifecycle, bool running, bool enable, bool disable, bool unblock, bool unblockAndRun, bool cancel)
    {
        await LoadAsync(Dtos.Summary(lifecycle: lifecycle, activeRun: running ? Active() : null));

        ProcessRowViewModel row = Vm.Items.Single();

        row.IsRunning.ShouldBe(running);
        row.ShowEnable.ShouldBe(enable);
        row.ShowDisable.ShouldBe(disable);
        row.ShowUnblock.ShouldBe(unblock);
        row.ShowUnblockAndRun.ShouldBe(unblockAndRun);
        row.ShowCancel.ShouldBe(cancel);
    }

    [Fact]
    public async Task RunNow_DisabledService_ShowsTheServiceReason()
    {
        await LoadAsync(Dtos.Summary(kind: ProcessKind.Service, lifecycle: ProcessLifecycle.Disabled));
        _h.Gateway.FailNext("RunNowAsync", new GapActivatorRequestException(IpcErrorCodes.ProcessDisabled, "x", retryable: false));

        await Vm.Items.Single().RunNowCommand.ExecuteAsync(null);

        Vm.ErrorText.ShouldBe("The service is disabled. Enable it to start it.");
    }

    [Fact]
    public async Task RunNow_Blocked_ShowsTheBlockedTextAndOffersUnblockAndRun()
    {
        await LoadAsync(Dtos.Summary(lifecycle: ProcessLifecycle.Blocked));
        _h.Gateway.FailNext("RunNowAsync", new GapActivatorRequestException(IpcErrorCodes.ProcessBlocked, "x", retryable: false));
        ProcessRowViewModel row = Vm.Items.Single();

        row.RunNowCommand.CanExecute(null).ShouldBeTrue();
        await row.RunNowCommand.ExecuteAsync(null);

        Vm.ErrorText.ShouldBe("The process is blocked. Unblock it first.");
        row.ShowUnblockAndRun.ShouldBeTrue();
    }

    [Fact]
    public async Task Commands_SendWhatTheRowSays()
    {
        await LoadAsync(
            Dtos.Summary("Blocked", lifecycle: ProcessLifecycle.Blocked),
            Dtos.Summary("Disabled", lifecycle: ProcessLifecycle.Disabled),
            Dtos.Summary("Enabled"));

        await Row("Blocked").UnblockCommand.ExecuteAsync(null);
        await Row("Blocked").UnblockAndRunCommand.ExecuteAsync(null);
        await Row("Disabled").EnableCommand.ExecuteAsync(null);
        await Row("Enabled").DisableCommand.ExecuteAsync(null);

        Sent().Select(c => c.Method).ShouldBe(["UnblockProcessAsync", "UnblockProcessAsync", "EnableProcessAsync", "DisableProcessAsync"]);
        Sent()[0].Args[1].ShouldBe(false);
        Sent()[1].Args[1].ShouldBe(true);
    }

    [Fact]
    public async Task Cancel_RunToCompletion_CancelsTheRunWithoutAsking()
    {
        var run = Active();
        await LoadAsync(Dtos.Summary(activeRun: run));

        await Vm.Items.Single().CancelCommand.ExecuteAsync(null);

        _h.Dialogs.Asked.ShouldBeEmpty();
        Sent().ShouldHaveSingleItem().Args[0].ShouldBe(run.RunId);
    }

    [Fact]
    public async Task Delete_RunStillActive_ShowsTheNotStoppedText()
    {
        var process = Dtos.Summary(activeRun: Active());
        _h.Gateway.Details[process.Id] = Dtos.Details(process);
        await LoadAsync(process);
        _h.Dialogs.Enqueue(DialogChoice.Primary);

        Task delete = Vm.Items.Single().DeleteCommand.ExecuteAsync(null);
        for (var i = 0; i < 400 && !delete.IsCompleted; i++)
        {
            await Task.Delay(1, TestContext.Current.CancellationToken);
            _h.Time.Advance(ProcessActions.DeletePollInterval);
        }

        await delete.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Vm.ErrorText.ShouldBe("The run has not stopped yet. Try deleting again in a moment.");
    }

    [Fact]
    public async Task Actions_Viewer_EveryCommandDisabled()
    {
        await LoadAsync(Dtos.Summary(activeRun: Active()), Dtos.Summary("B", lifecycle: ProcessLifecycle.Blocked));
        _h.Gateway.SetStatus(FakeServiceGateway.Viewer());

        foreach (ProcessRowViewModel row in Vm.Items)
        {
            foreach (IAsyncRelayCommand command in new[] { row.RunNowCommand, row.CancelCommand, row.EnableCommand, row.DisableCommand, row.UnblockCommand, row.UnblockAndRunCommand, row.DeleteCommand })
            {
                command.CanExecute(null).ShouldBeFalse();
            }

            row.DeniedReason.ShouldBe("Requires Administrators or GapActivator Operators");
        }

        _h.Gateway.SetStatus(FakeServiceGateway.Operator());

        Vm.Items[0].RunNowCommand.CanExecute(null).ShouldBeTrue();
        Vm.Items[0].DeniedReason.ShouldBeNull();
    }

    [Fact]
    public async Task Language_Switched_RebuildsTheCachedTexts()
    {
        await LoadAsync(Dtos.Summary(lifecycle: ProcessLifecycle.Blocked, lastOutcome: RunOutcome.Failed));
        ProcessRowViewModel row = Vm.Items.Single();

        _h.L.SetLanguage(AppLanguage.Portuguese);

        row.LifecycleText.ShouldBe("Bloqueado");
        row.LifecycleTooltip.ShouldBe("O limite de falhas foi atingido");
        row.KindText.ShouldBe("Executa até concluir");
        row.NextText.ShouldBe("Sem próxima execução");
        row.LastOutcomeText.ShouldBe("Falhou");
        Vm.Items.Single().ShouldBeSameAs(row);
    }

    [Fact]
    public async Task Freshness_FollowsTheSameRulesAsTheDashboard()
    {
        Vm.HasData.ShouldBeFalse();
        Vm.ShowWaiting.ShouldBeTrue();
        Vm.ShowEmpty.ShouldBeFalse();

        await LoadAsync();
        Vm.HasData.ShouldBeTrue();
        Vm.ShowEmpty.ShouldBeTrue();
        Vm.IsStale.ShouldBeFalse();

        await LoadAsync(Dtos.Summary());
        Vm.ShowEmpty.ShouldBeFalse();

        _h.Gateway.SetStatus(FakeServiceGateway.Stopped);
        Vm.IsStale.ShouldBeTrue();
        Vm.StaleText.ShouldBe("Not connected: showing the last known state.");
        Vm.Items.Count.ShouldBe(1);
    }
}
