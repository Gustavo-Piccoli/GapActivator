using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Tests.Fakes;
using GapActivator.Ipc;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.Tests.Services;

public class ProcessActionsTests
{
    private readonly DashboardHarness _h = new();

    private ProcessActions Actions => _h.Actions;

    private static ActiveRunViewDto Active(Guid? runId = null) =>
        new(runId ?? Guid.NewGuid(), RunState.Running, RunTrigger.Manual, Dtos.Epoch, 100);

    private int Count(string method) => _h.Gateway.Calls.Count(c => c.Method == method);

    private List<string> Methods() => [.. _h.Gateway.Calls.Select(c => c.Method)];

    /// <summary>Advances the fake clock by one poll interval at a time until the task finishes; <paramref name="afterTick"/> runs before each step.</summary>
    private async Task PumpAsync(Task task, Action? afterTick = null)
    {
        for (var i = 0; i < 400 && !task.IsCompleted; i++)
        {
            await Task.Delay(1, TestContext.Current.CancellationToken);
            afterTick?.Invoke();
            _h.Time.Advance(ProcessActions.DeletePollInterval);
        }

        await task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private void ShouldHaveSent(int index, string method, params object?[] args)
    {
        var call = _h.Gateway.Calls[index];
        call.Method.ShouldBe(method);
        call.Args.ShouldBe(args);
    }

    [Fact]
    public async Task RunNow_Idle_SendsRunNow()
    {
        var process = Dtos.Summary();
        var runId = Guid.NewGuid();
        _h.Gateway.NextRunId = runId;

        Guid? result = await Actions.RunNowAsync(process);

        result.ShouldBe(runId);
        _h.Gateway.Calls.ShouldHaveSingleItem();
        ShouldHaveSent(0, "RunNowAsync", process.Id);
        _h.Dialogs.Asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunNow_ActiveRunToCompletion_AsksAndOnYesSendsRestartRun()
    {
        var process = Dtos.Summary(activeRun: Active());
        _h.Dialogs.Enqueue(DialogChoice.Primary);

        Guid? result = await Actions.RunNowAsync(process);

        result.ShouldBeNull();
        var asked = _h.Dialogs.Asked.ShouldHaveSingleItem();
        asked.Title.ShouldBe("Already running");
        asked.Message.ShouldBe("The process is already running. Do you want to cancel the current run and start a new one?");
        asked.Primary.ShouldBe("Yes");
        asked.Secondary.ShouldBeNull();
        asked.Cancel.ShouldBe("No");
        _h.Gateway.Calls.ShouldHaveSingleItem();
        ShouldHaveSent(0, "RestartRunAsync", process.Id);
    }

    [Fact]
    public async Task RunNow_ActiveRunToCompletion_InPortuguese_UsesTheSpecWording()
    {
        var h = new DashboardHarness(AppLanguage.Portuguese);
        h.Dialogs.Enqueue(DialogChoice.Cancel);

        await h.Actions.RunNowAsync(Dtos.Summary(activeRun: Active()));

        h.Dialogs.Asked.ShouldHaveSingleItem().Message.ShouldBe("O processo já está em execução. Deseja cancelar a execução atual e iniciar uma nova?");
    }

    [Fact]
    public async Task RunNow_ActiveService_AsksWithTheRestartWording()
    {
        var process = Dtos.Summary(kind: ProcessKind.Service, activeRun: Active());
        _h.Dialogs.Enqueue(DialogChoice.Primary);

        await Actions.RunNowAsync(process);

        var asked = _h.Dialogs.Asked.ShouldHaveSingleItem();
        asked.Message.ShouldBe("The process is already running. Restart it?");
        asked.Primary.ShouldBe("Restart");
        asked.Cancel.ShouldBe("No");
        Count("RestartRunAsync").ShouldBe(1);
    }

    [Fact]
    public async Task RunNow_No_SendsNothing()
    {
        var process = Dtos.Summary(activeRun: Active());
        _h.Dialogs.Enqueue(DialogChoice.Cancel);

        Guid? result = await Actions.RunNowAsync(process);

        result.ShouldBeNull();
        _h.Dialogs.Asked.Count.ShouldBe(1);
        _h.Gateway.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunNow_RejectedAsAlreadyActive_AsksThenRestarts()
    {
        var process = Dtos.Summary();
        _h.Gateway.FailNext("RunNowAsync", new GapActivatorRequestException(IpcErrorCodes.RunAlreadyActive, "busy", retryable: false));
        _h.Dialogs.Enqueue(DialogChoice.Primary);

        Guid? result = await Actions.RunNowAsync(process);

        result.ShouldBeNull();
        _h.Dialogs.Asked.Count.ShouldBe(1);
        Methods().ShouldBe(["RunNowAsync", "RestartRunAsync"]);
    }

    [Fact]
    public async Task RunNow_OtherRejection_Propagates()
    {
        _h.Gateway.FailNext("RunNowAsync", new GapActivatorRequestException(IpcErrorCodes.ProcessDisabled, "disabled", retryable: false));

        var error = await Should.ThrowAsync<GapActivatorRequestException>(() => Actions.RunNowAsync(Dtos.Summary()));

        error.Code.ShouldBe(IpcErrorCodes.ProcessDisabled);
        _h.Dialogs.Asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task UnblockAndRun_SendsUnblockWithRunNow()
    {
        var id = Guid.NewGuid();

        await Actions.UnblockAsync(id, runNow: true);

        _h.Gateway.Calls.ShouldHaveSingleItem();
        ShouldHaveSent(0, "UnblockProcessAsync", id, true);
    }

    [Fact]
    public async Task Unblock_SendsUnblockWithoutRun()
    {
        var id = Guid.NewGuid();

        await Actions.UnblockAsync(id, runNow: false);

        _h.Gateway.Calls.ShouldHaveSingleItem();
        ShouldHaveSent(0, "UnblockProcessAsync", id, false);
    }

    [Fact]
    public async Task EnableAndDisable_SendTheCommands()
    {
        var process = Dtos.Summary();

        await Actions.EnableAsync(process.Id);
        await Actions.DisableAsync(process);

        _h.Gateway.Calls.Count.ShouldBe(2);
        ShouldHaveSent(0, "EnableProcessAsync", process.Id);
        ShouldHaveSent(1, "DisableProcessAsync", process.Id, false);
        _h.Dialogs.Asked.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(DialogChoice.Primary, true)]
    [InlineData(DialogChoice.Secondary, false)]
    [InlineData(DialogChoice.Cancel, null)]
    public async Task Disable_ActiveRunToCompletion_OffersCancelOrLetFinish(DialogChoice choice, bool? cancelActiveRun)
    {
        var process = Dtos.Summary("Nightly", activeRun: Active());
        _h.Dialogs.Enqueue(choice);

        await Actions.DisableAsync(process);

        var asked = _h.Dialogs.Asked.ShouldHaveSingleItem();
        asked.Title.ShouldBe("Disable \"Nightly\"");
        asked.Message.ShouldBe("\"Nightly\" has an active run.");
        asked.Primary.ShouldBe("Disable and cancel the current run");
        asked.Secondary.ShouldBe("Disable and let it finish");
        asked.Cancel.ShouldBe("Cancel");
        if (cancelActiveRun is { } expected)
        {
            _h.Gateway.Calls.ShouldHaveSingleItem();
            ShouldHaveSent(0, "DisableProcessAsync", process.Id, expected);
        }
        else
        {
            _h.Gateway.Calls.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Disable_ActiveService_ConfirmsOnceAndSendsOneDisable()
    {
        var process = Dtos.Summary("Svc", kind: ProcessKind.Service, activeRun: Active());
        _h.Dialogs.Enqueue(DialogChoice.Primary);

        await Actions.DisableAsync(process);

        _h.Dialogs.Asked.ShouldHaveSingleItem().Title.ShouldBe("Stop the service-kind process");
        _h.Gateway.Calls.ShouldHaveSingleItem();
        ShouldHaveSent(0, "DisableProcessAsync", process.Id, true);
    }

    [Fact]
    public async Task Disable_ActiveService_Declined_SendsNothing()
    {
        _h.Dialogs.Enqueue(DialogChoice.Cancel);

        await Actions.DisableAsync(Dtos.Summary(kind: ProcessKind.Service, activeRun: Active()));

        _h.Gateway.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Delete_Confirmed_SendsDelete()
    {
        var process = Dtos.Summary("Old one");
        _h.Dialogs.Enqueue(DialogChoice.Primary);

        DeleteResult result = await Actions.DeleteAsync(process);

        result.ShouldBe(DeleteResult.Deleted);
        var asked = _h.Dialogs.Asked.ShouldHaveSingleItem();
        asked.Title.ShouldBe("Delete process");
        asked.Message.ShouldBe("Delete \"Old one\"? Its run history is kept.");
        asked.Primary.ShouldBe("Delete");
        _h.Gateway.Calls.ShouldHaveSingleItem();
        ShouldHaveSent(0, "DeleteProcessAsync", process.Id);
    }

    [Fact]
    public async Task Delete_Declined_SendsNothing()
    {
        _h.Dialogs.Enqueue(DialogChoice.Cancel);

        DeleteResult result = await Actions.DeleteAsync(Dtos.Summary());

        result.ShouldBe(DeleteResult.Declined);
        _h.Gateway.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Delete_WithActiveRun_CancelsWaitsThenDeletes()
    {
        var run = Active();
        var process = Dtos.Summary("Busy", activeRun: run);
        _h.Gateway.Details[process.Id] = Dtos.Details(process);
        _h.Dialogs.Enqueue(DialogChoice.Primary);

        Task<DeleteResult> task = Actions.DeleteAsync(process);
        await PumpAsync(task, () =>
        {
            // The third GetProcess answer shows the run gone.
            if (Count("GetProcessAsync") >= 2)
            {
                _h.Gateway.Details[process.Id] = Dtos.Details(process with { ActiveRun = null });
            }
        });

        (await task).ShouldBe(DeleteResult.Deleted);
        var asked = _h.Dialogs.Asked.ShouldHaveSingleItem();
        asked.Message.ShouldBe("\"Busy\" has an active run. Cancel it and delete the process?");
        asked.Primary.ShouldBe("Cancel and delete");
        asked.Secondary.ShouldBeNull();
        Methods().ShouldBe(["CancelRunAsync", "GetProcessAsync", "GetProcessAsync", "GetProcessAsync", "DeleteProcessAsync"]);
        ShouldHaveSent(0, "CancelRunAsync", run.RunId);
    }

    [Fact]
    public async Task Delete_RunEndedBeforeTheCancelArrived_TreatsRunNotActiveAsStoppedAndDeletes()
    {
        var run = Active();
        var process = Dtos.Summary("Busy", activeRun: run);
        _h.Gateway.Details[process.Id] = Dtos.Details(process with { ActiveRun = null });
        _h.Gateway.FailNext("CancelRunAsync", new GapActivatorRequestException(IpcErrorCodes.RunNotActive, "ended", retryable: false));
        _h.Dialogs.Enqueue(DialogChoice.Primary);

        DeleteResult result = await Actions.DeleteAsync(process);

        result.ShouldBe(DeleteResult.Deleted);
        Methods().ShouldBe(["CancelRunAsync", "GetProcessAsync", "DeleteProcessAsync"]);
    }

    [Fact]
    public async Task Delete_NewRunStartedDuringThePoll_IsCancelledBeforeTheDelete()
    {
        var first = Active();
        var second = Active();
        var process = Dtos.Summary("Busy", activeRun: first);
        _h.Gateway.Details[process.Id] = Dtos.Details(process with { ActiveRun = second });
        _h.Dialogs.Enqueue(DialogChoice.Primary);

        Task<DeleteResult> task = Actions.DeleteAsync(process);
        await PumpAsync(task, () =>
        {
            // Once the new run has been cancelled too, the next read shows nothing running.
            if (Count("CancelRunAsync") >= 2)
            {
                _h.Gateway.Details[process.Id] = Dtos.Details(process with { ActiveRun = null });
            }
        });

        (await task).ShouldBe(DeleteResult.Deleted);
        Methods().ShouldBe(["CancelRunAsync", "GetProcessAsync", "CancelRunAsync", "GetProcessAsync", "DeleteProcessAsync"]);
        ShouldHaveSent(0, "CancelRunAsync", first.RunId);
        ShouldHaveSent(2, "CancelRunAsync", second.RunId);
    }

    [Fact]
    public async Task Delete_RunStartsBetweenTheLastReadAndTheDelete_WaitsAndTriesAgain()
    {
        var process = Dtos.Summary("Busy", activeRun: Active());
        _h.Gateway.Details[process.Id] = Dtos.Details(process with { ActiveRun = null });
        _h.Gateway.FailNext("DeleteProcessAsync", new GapActivatorRequestException(IpcErrorCodes.HasActiveRun, "a run started", retryable: false));
        _h.Dialogs.Enqueue(DialogChoice.Primary);

        Task<DeleteResult> task = Actions.DeleteAsync(process);
        await PumpAsync(task);

        (await task).ShouldBe(DeleteResult.Deleted);
        Methods().ShouldBe(["CancelRunAsync", "GetProcessAsync", "DeleteProcessAsync", "GetProcessAsync", "DeleteProcessAsync"]);
    }

    [Fact]
    public async Task Delete_ActiveService_StopsWithoutAskingASecondQuestion()
    {
        var process = Dtos.Summary("Svc", kind: ProcessKind.Service, activeRun: Active());
        _h.Gateway.Details[process.Id] = Dtos.Details(process with { ActiveRun = null });
        _h.Dialogs.Enqueue(DialogChoice.Primary);

        DeleteResult result = await Actions.DeleteAsync(process);

        result.ShouldBe(DeleteResult.Deleted);
        _h.Dialogs.Asked.Count.ShouldBe(1);
        Methods().ShouldBe(["DisableProcessAsync", "GetProcessAsync", "DeleteProcessAsync"]);
        ShouldHaveSent(0, "DisableProcessAsync", process.Id, true);
    }

    [Fact]
    public async Task Delete_WithActiveRun_ReportsStoppingOnlyAfterTheUserConfirms()
    {
        var process = Dtos.Summary(activeRun: Active());
        _h.Gateway.Details[process.Id] = Dtos.Details(process with { ActiveRun = null });
        var stopping = 0;

        _h.Dialogs.Enqueue(DialogChoice.Cancel);
        await Actions.DeleteAsync(process, () => stopping++);
        stopping.ShouldBe(0);

        _h.Dialogs.Enqueue(DialogChoice.Primary);
        await Actions.DeleteAsync(process, () => stopping++);
        stopping.ShouldBe(1);
    }

    [Fact]
    public async Task Delete_ActiveRunDialog_UsesTheCommonCancelText()
    {
        // The dialog's cancel button is the common "Cancel" (a different Localizer key than the dashboard's "Cancel run" button, same words).
        _h.Dialogs.Enqueue(DialogChoice.Cancel);
        await Actions.DeleteAsync(Dtos.Summary(activeRun: Active()));
        _h.Dialogs.Asked.Single().Cancel.ShouldBe(_h.L.Cancel);

        _h.Dialogs.Asked.Clear();
        _h.Dialogs.Enqueue(DialogChoice.Cancel);
        await Actions.DisableAsync(Dtos.Summary(activeRun: Active()));
        _h.Dialogs.Asked.Single().Cancel.ShouldBe(_h.L.Cancel);
    }

    [Fact]
    public async Task Delete_ActiveRunDeclined_SendsNothing()
    {
        _h.Dialogs.Enqueue(DialogChoice.Cancel);

        DeleteResult result = await Actions.DeleteAsync(Dtos.Summary(activeRun: Active()));

        result.ShouldBe(DeleteResult.Declined);
        _h.Gateway.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Delete_RunStillActiveAfter60Seconds_ReturnsStillRunning()
    {
        var process = Dtos.Summary(activeRun: Active());
        _h.Gateway.Details[process.Id] = Dtos.Details(process);
        _h.Dialogs.Enqueue(DialogChoice.Primary);

        Task<DeleteResult> task = Actions.DeleteAsync(process);
        await PumpAsync(task);

        (await task).ShouldBe(DeleteResult.StillRunning);
        (_h.Time.GetUtcNow() - Dtos.Epoch).ShouldBeGreaterThanOrEqualTo(ProcessActions.DeleteWaitLimit);
        Count("DeleteProcessAsync").ShouldBe(0);
    }

    [Fact]
    public void Constants_AreHalfASecondAndSixtySeconds()
    {
        ProcessActions.DeletePollInterval.ShouldBe(TimeSpan.FromMilliseconds(500));
        ProcessActions.DeleteWaitLimit.ShouldBe(TimeSpan.FromSeconds(60));
    }
}
