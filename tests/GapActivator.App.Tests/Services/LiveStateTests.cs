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
    public void Start_AlreadyConnected_RefreshesRightAway()
    {
        var live = new LiveState(_gateway, _logger);

        live.Start();

        // The fake answers synchronously, so the refresh Start began is already done.
        Count(nameof(IServiceGateway.GetDashboardAsync)).ShouldBe(1);
        Count(nameof(IServiceGateway.ListProcessesAsync)).ShouldBe(1);
        live.Dashboard.ShouldNotBeNull();
    }

    [Fact]
    public void StateChanged_RefreshesTheSnapshot()
    {
        var live = new LiveState(_gateway, _logger);
        live.Start();
        var before = Count(nameof(IServiceGateway.GetDashboardAsync));
        _gateway.Dashboard = Dtos.Dashboard(isPaused: true);

        _gateway.RaiseStateChanged(new StateChangedDto(null, null));

        Count(nameof(IServiceGateway.GetDashboardAsync)).ShouldBe(before + 1);
        live.Dashboard.ShouldNotBeNull().IsPaused.ShouldBeTrue();
    }

    [Fact]
    public async Task StateChanged_DuringARefresh_RefreshesExactlyOnceMore()
    {
        var live = new LiveState(_gateway, _logger);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var changes = 0;
        live.Changed += (_, _) =>
        {
            if (Interlocked.Increment(ref changes) == 2)
            {
                finished.SetResult();
            }
        };
        TaskCompletionSource hold = _gateway.Hold(nameof(IServiceGateway.GetDashboardAsync));
        live.Start(); // the connected gateway starts the first refresh, which waits on the hold

        _gateway.RaiseStateChanged(new StateChangedDto(null, null));
        _gateway.RaiseStateChanged(new StateChangedDto(null, null));
        _gateway.RaiseStateChanged(new StateChangedDto(null, null));
        hold.SetResult();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Count(nameof(IServiceGateway.GetDashboardAsync)).ShouldBe(2);
        Count(nameof(IServiceGateway.ListProcessesAsync)).ShouldBe(2);
        changes.ShouldBe(2);
    }

    [Fact]
    public async Task Subscriber_Throws_IsLoggedAndLaterRefreshesStillRun()
    {
        var live = new LiveState(_gateway, _logger);
        live.Changed += (_, _) => throw new InvalidOperationException("subscriber");
        var other = 0;
        live.Changed += (_, _) => other++;

        live.Start();
        _gateway.RaiseStateChanged(new StateChangedDto(null, null));
        await live.RefreshAsync(); // completes successfully: callers wait for the refresh, the failure is logged

        _logger.Levels.Count(l => l == LogLevel.Error).ShouldBe(3);
        other.ShouldBe(3);
        Count(nameof(IServiceGateway.GetDashboardAsync)).ShouldBe(3);
    }

    [Fact]
    public async Task Subscriber_ThrowsDuringARefresh_DoesNotDropTheQueuedRefresh()
    {
        var live = new LiveState(_gateway, _logger);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var changes = 0;
        live.Changed += (_, _) =>
        {
            if (Interlocked.Increment(ref changes) == 2)
            {
                finished.SetResult();
            }

            throw new InvalidOperationException("subscriber");
        };
        TaskCompletionSource hold = _gateway.Hold(nameof(IServiceGateway.GetDashboardAsync));
        live.Start();
        _gateway.RaiseStateChanged(new StateChangedDto(null, null));

        hold.SetResult();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Count(nameof(IServiceGateway.GetDashboardAsync)).ShouldBe(2);
        _logger.Levels.ShouldContain(LogLevel.Error);
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
        live.RefreshFailed.ShouldBeTrue();
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
        live.RefreshFailed.ShouldBeFalse();
    }

    [Fact]
    public async Task RefreshFails_RaisesChangedSoPagesLearnOfIt()
    {
        var live = new LiveState(_gateway, _logger);
        var raised = 0;
        live.Changed += (_, _) => raised++;
        _gateway.FailNext(nameof(IServiceGateway.GetDashboardAsync), new TimeoutException());

        await live.RefreshAsync();

        raised.ShouldBe(1);
        live.RefreshFailed.ShouldBeTrue();
    }

    [Fact]
    public void RunOutcomes_Failures_AreTheFiveFailureOutcomes() =>
        RunOutcomes.Failures.ShouldBe([RunOutcome.Failed, RunOutcome.FailedToStart, RunOutcome.TimedOut, RunOutcome.Abandoned, RunOutcome.Crashed]);
}
