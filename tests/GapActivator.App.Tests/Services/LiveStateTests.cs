using GapActivator.App.Services;
using GapActivator.App.Tests.Fakes;
using GapActivator.Ipc;
using GapActivator.Ipc.Contracts;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace GapActivator.App.Tests.Services;

public class LiveStateTests : IAsyncDisposable
{
    private readonly FakeServiceGateway _gateway = new();
    private readonly ListLogger<LiveState> _logger = new();

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return _gateway.DisposeAsync();
    }

    private int Count(string method) => _gateway.Calls.Count(c => c.Method == method);

    [Fact]
    public async Task Connected_RefreshesDashboardAndProcesses()
    {
        var live = new LiveState(_gateway, _logger);
        live.Start();
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        live.Changed += (_, _) => changed.TrySetResult();
        _gateway.Processes = [Dtos.Summary("Nightly")];
        _gateway.Dashboard = Dtos.Dashboard(isPaused: true);

        _gateway.SetStatus(FakeServiceGateway.Operator());
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        live.Dashboard.ShouldNotBeNull().IsPaused.ShouldBeTrue();
        live.Processes.Select(p => p.Name).ShouldBe(["Nightly"]);
    }

    [Fact]
    public async Task Start_AlreadyConnected_RefreshesRightAway()
    {
        var live = new LiveState(_gateway, _logger);

        live.Start();
        await live.RefreshAsync();

        Count(nameof(IServiceGateway.GetDashboardAsync)).ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task StateChanged_RefreshesTheSnapshot()
    {
        var live = new LiveState(_gateway, _logger);
        live.Start();
        await live.RefreshAsync();
        var before = Count(nameof(IServiceGateway.GetDashboardAsync));

        _gateway.RaiseStateChanged(new StateChangedDto(null, null));
        await live.RefreshAsync();

        Count(nameof(IServiceGateway.GetDashboardAsync)).ShouldBeGreaterThan(before);
    }

    [Fact]
    public async Task StateChanged_DuringARefresh_RefreshesExactlyOnceMore()
    {
        var live = new LiveState(_gateway, _logger);
        TaskCompletionSource hold = _gateway.Hold(nameof(IServiceGateway.GetDashboardAsync));

        Task first = live.RefreshAsync();
        Task second = live.RefreshAsync();
        Task third = live.RefreshAsync();
        Task fourth = live.RefreshAsync();
        hold.SetResult();
        await Task.WhenAll(first, second, third, fourth).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Count(nameof(IServiceGateway.GetDashboardAsync)).ShouldBe(2);
        Count(nameof(IServiceGateway.ListProcessesAsync)).ShouldBe(2);
    }

    [Fact]
    public async Task RefreshFails_KeepsTheLastSnapshotAndLogsWarning()
    {
        var live = new LiveState(_gateway, _logger);
        _gateway.Dashboard = Dtos.Dashboard(isPaused: true);
        await live.RefreshAsync();
        DashboardDto? kept = live.Dashboard;

        _gateway.FailNext(nameof(IServiceGateway.GetDashboardAsync), new GapActivatorRequestException(IpcErrorCodes.Disconnected, "gone", retryable: true));
        await live.RefreshAsync();

        live.Dashboard.ShouldBeSameAs(kept);
        _logger.Levels.ShouldContain(LogLevel.Warning);
    }

    [Fact]
    public async Task RefreshAfterAFailure_RecoversAndRaisesChanged()
    {
        var live = new LiveState(_gateway, _logger);
        _gateway.FailNext(nameof(IServiceGateway.GetDashboardAsync), new TimeoutException());
        await live.RefreshAsync();
        var raised = 0;
        live.Changed += (_, _) => raised++;

        await live.RefreshAsync();

        raised.ShouldBe(1);
        live.Dashboard.ShouldNotBeNull();
    }

    [Fact]
    public void RunOutcomes_Failures_AreTheFiveFailureOutcomes() =>
        RunOutcomes.Failures.ShouldBe([RunOutcome.Failed, RunOutcome.FailedToStart, RunOutcome.TimedOut, RunOutcome.Abandoned, RunOutcome.Crashed]);
}
