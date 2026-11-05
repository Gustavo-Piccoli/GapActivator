using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.ViewModels;
using GapActivator.Ipc.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace GapActivator.App.Tests.ViewModels;

public class RunDetailViewModelTests : IAsyncDisposable
{
    private readonly FakeServiceGateway _gateway = new();
    private readonly FakeTimeProvider _time = new(Dtos.Epoch.AddSeconds(30));
    private readonly Localizer _l = new(AppLanguage.English);

    private int GetRunCalls => _gateway.Calls.Count(c => c.Method == nameof(FakeServiceGateway.GetRunAsync));

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return _gateway.DisposeAsync();
    }

    private RunDetailViewModel Create() => new(_l, _gateway, _time, new TimeDisplay(_l, TimeZoneInfo.Utc), new ImmediateUiDispatcher());

    [Fact]
    public async Task Detail_ShowsEventsExitCodeDurationAndOutput()
    {
        var run = Dtos.RunRow("Alpha", outcome: RunOutcome.Succeeded, exitCode: 1, trigger: RunTrigger.Manual);
        _gateway.Runs[run.Id] = Dtos.RunDetails(
            run,
            entries: [Dtos.Entry(HistoryEntryKind.RunStarted), Dtos.Entry(HistoryEntryKind.RunCompleted, Dtos.Epoch.AddSeconds(5), "done")],
            output: "line 1\r\nline 2");
        using var vm = Create();

        await vm.LoadAsync(run.Id);

        vm.ProcessName.ShouldBe("Alpha");
        vm.StatusText.ShouldBe("Succeeded");
        vm.TriggerText.ShouldBe("Started manually");
        vm.ExitCodeText.ShouldBe("1");
        vm.DurationText.ShouldBe("5 s");
        vm.Events.ShouldBe(["1/1/2030 12:00 PM  Run started", "1/1/2030 12:00 PM  Run completed — done"]);
        vm.Output.ShouldBe("line 1\r\nline 2");
        vm.OutputNote.ShouldBeNull();
        vm.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Detail_ActiveRun_RefreshesEverySecondUntilCompleted()
    {
        var run = Dtos.RunRow("Alpha", state: RunState.Running, outcome: null, startedAt: Dtos.Epoch, exitCode: null);
        _gateway.Runs[run.Id] = Dtos.RunDetails(run);
        using var vm = Create();
        await vm.LoadAsync(run.Id);
        vm.IsActive.ShouldBeTrue();
        vm.DurationText.ShouldBe("30 s");

        _time.Advance(TimeSpan.FromSeconds(1));
        _gateway.Runs[run.Id] = Dtos.RunDetails(
            Dtos.RunRow("Alpha", id: run.Id, state: RunState.Completed, outcome: RunOutcome.Succeeded, startedAt: Dtos.Epoch, endedAt: Dtos.Epoch.AddSeconds(31)),
            output: "end");
        _time.Advance(TimeSpan.FromSeconds(1));
        _time.Advance(TimeSpan.FromSeconds(1));

        GetRunCalls.ShouldBe(3);
        vm.IsActive.ShouldBeFalse();
        vm.Output.ShouldBe("end");
        vm.DurationText.ShouldBe("31 s");
    }

    [Fact]
    public async Task Detail_Closed_StopsRefreshing()
    {
        var run = Dtos.RunRow(state: RunState.Running, outcome: null, exitCode: null);
        _gateway.Runs[run.Id] = Dtos.RunDetails(run);
        using var vm = Create();
        await vm.LoadAsync(run.Id);

        vm.Close();
        _time.Advance(TimeSpan.FromSeconds(5));

        GetRunCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Detail_Disposed_StopsRefreshing()
    {
        var run = Dtos.RunRow(state: RunState.Running, outcome: null, exitCode: null);
        _gateway.Runs[run.Id] = Dtos.RunDetails(run);
        var vm = Create();
        await vm.LoadAsync(run.Id);

        vm.Dispose();
        _time.Advance(TimeSpan.FromSeconds(5));

        GetRunCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Detail_ResultArrivesAfterClose_IsDiscarded()
    {
        var run = Dtos.RunRow(state: RunState.Running, outcome: null, exitCode: null);
        _gateway.Runs[run.Id] = Dtos.RunDetails(run, output: "first");
        using var vm = Create();
        await vm.LoadAsync(run.Id);
        TaskCompletionSource hold = _gateway.Hold(nameof(FakeServiceGateway.GetRunAsync));
        _gateway.Runs[run.Id] = Dtos.RunDetails(run, output: "late");
        _time.Advance(TimeSpan.FromSeconds(1));

        vm.Close();
        hold.SetResult();
        await Task.Delay(50, TestContext.Current.CancellationToken);

        vm.Output.ShouldBe("first");
    }

    [Fact]
    public async Task Detail_UnchangedOutput_DoesNotRaiseOutputChanged()
    {
        var run = Dtos.RunRow(state: RunState.Running, outcome: null, exitCode: null);
        var big = new string('x', 1024 * 1024);
        _gateway.Runs[run.Id] = Dtos.RunDetails(run, output: big, truncated: true);
        using var vm = Create();
        await vm.LoadAsync(run.Id);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _gateway.Runs[run.Id] = Dtos.RunDetails(run, output: new string('x', 1024 * 1024), truncated: true);
        _time.Advance(TimeSpan.FromSeconds(1));
        raised.ShouldNotContain(nameof(RunDetailViewModel.Output));

        _gateway.Runs[run.Id] = Dtos.RunDetails(run, output: big + "y", truncated: true);
        _time.Advance(TimeSpan.FromSeconds(1));
        raised.ShouldContain(nameof(RunDetailViewModel.Output));
    }

    [Fact]
    public async Task Detail_Truncated_ShowsTheTailNote()
    {
        var run = Dtos.RunRow();
        _gateway.Runs[run.Id] = Dtos.RunDetails(run, output: "tail", truncated: true);
        using var vm = Create();

        await vm.LoadAsync(run.Id);

        vm.OutputNote.ShouldBe("Only the last 1 MiB of output is shown.");
    }

    [Fact]
    public async Task Detail_NoOutputFile_ShowsNoOutput()
    {
        var run = Dtos.RunRow();
        _gateway.Runs[run.Id] = Dtos.RunDetails(run, exists: false);
        using var vm = Create();

        await vm.LoadAsync(run.Id);

        vm.OutputNote.ShouldBe("No output was captured.");
        vm.Output.ShouldBeEmpty();
    }

    [Fact]
    public async Task Detail_ReadoptionNoteLine_IsShownVerbatim()
    {
        const string note = "[GapActivator] The service restarted and re-adopted this process at 2030-01-01T12:00:00Z; later output is not captured.";
        var run = Dtos.RunRow(state: RunState.Running, outcome: null, exitCode: null);
        _gateway.Runs[run.Id] = Dtos.RunDetails(run, output: "before\n" + note + "\n");
        using var vm = Create();

        await vm.LoadAsync(run.Id);

        vm.Output.ShouldBe("before\n" + note + "\n");
    }

    [Fact]
    public async Task Detail_Failed_ShowsTheErrorDetailUnderError()
    {
        var run = Dtos.RunRow(outcome: RunOutcome.Failed, exitCode: 2);
        _gateway.Runs[run.Id] = Dtos.RunDetails(run, error: "exit code 2");
        using var vm = Create();

        await vm.LoadAsync(run.Id);

        vm.ErrorLabel.ShouldBe(_l.RunError);
        vm.RunErrorText.ShouldBe("exit code 2");
        vm.ErrorText.ShouldBeNull();
    }

    [Fact]
    public async Task Detail_ActiveRefresh_KeepsTheRunErrorDetail()
    {
        var run = Dtos.RunRow(state: RunState.Running, outcome: null, exitCode: null);
        _gateway.Runs[run.Id] = Dtos.RunDetails(run, error: "detail");
        using var vm = Create();
        await vm.LoadAsync(run.Id);

        _time.Advance(TimeSpan.FromSeconds(1));

        vm.RunErrorText.ShouldBe("detail");
    }

    [Fact]
    public async Task Detail_Cancelled_UsesReasonNotError()
    {
        var run = Dtos.RunRow(outcome: RunOutcome.Cancelled, exitCode: null);
        _gateway.Runs[run.Id] = Dtos.RunDetails(run, error: "cancelled by an operator");
        using var vm = Create();

        await vm.LoadAsync(run.Id);

        vm.ErrorLabel.ShouldBe("Reason");
    }

    [Fact]
    public async Task Detail_LoadFails_ShowsTheCallError()
    {
        using var vm = Create();

        await vm.LoadAsync(Guid.NewGuid());

        vm.ErrorText.ShouldBe("The item no longer exists. Refresh and try again.");
    }

    [Fact]
    public async Task Detail_LanguageSwitched_RebuildsTheTexts()
    {
        var run = Dtos.RunRow(outcome: RunOutcome.Failed, trigger: RunTrigger.Retry);
        _gateway.Runs[run.Id] = Dtos.RunDetails(run, error: "x", entries: [Dtos.Entry(HistoryEntryKind.RunStarted)]);
        using var vm = Create();
        await vm.LoadAsync(run.Id);

        _l.SetLanguage(AppLanguage.Portuguese);

        vm.StatusText.ShouldBe("Falhou");
        vm.ErrorLabel.ShouldBe("Erro");
        vm.TriggerText.ShouldBe("Nova tentativa");
        vm.Events[0].ShouldEndWith("Execução iniciada");
    }
}
