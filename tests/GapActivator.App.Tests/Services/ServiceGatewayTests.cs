using GapActivator.App.Services;
using GapActivator.App.Shell;
using GapActivator.App.Tests.Fakes;
using GapActivator.Ipc;
using GapActivator.Ipc.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GapActivator.App.Tests.Services;

public class ServiceGatewayTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Start_TimeoutAndNotInstalled_StateIsServiceNotInstalled()
    {
        await using var h = new Harness(ServiceInstallState.NotInstalled);
        h.Script(new TimeoutException());

        h.Gateway.Start();
        await h.WaitTimersAsync(1);

        h.Gateway.Status.State.ShouldBe(ConnectionState.ServiceNotInstalled);
        h.Gateway.Status.IsConnected.ShouldBeFalse();
    }

    [Fact]
    public async Task Start_TimeoutAndStopped_StateIsServiceStopped()
    {
        await using var h = new Harness(ServiceInstallState.Stopped);
        h.Script(new TimeoutException());

        h.Gateway.Start();
        await h.WaitTimersAsync(1);

        h.Gateway.Status.State.ShouldBe(ConnectionState.ServiceStopped);
    }

    [Fact]
    public async Task Start_TimeoutAndRunning_StateStaysConnecting()
    {
        await using var h = new Harness(ServiceInstallState.Running);
        h.Script(new TimeoutException());

        h.Gateway.Start();
        await h.WaitTimersAsync(1);

        h.Gateway.Status.State.ShouldBe(ConnectionState.Connecting);
        h.Probe.Queries.ShouldBe(1);
    }

    [Fact]
    public async Task Start_ServerNotVerified_StateIsRefusedWithThatCode()
    {
        await using var h = new Harness(ServiceInstallState.Running);
        h.Script(new GapActivatorRequestException(IpcErrorCodes.ServerNotVerified, "no", retryable: false));

        h.Gateway.Start();
        await h.WaitTimersAsync(1);

        h.Gateway.Status.State.ShouldBe(ConnectionState.Refused);
        h.Gateway.Status.ErrorCode.ShouldBe("server-not-verified");
    }

    [Fact]
    public async Task Start_Busy_IsRefusedAndRetried()
    {
        await using var h = new Harness(ServiceInstallState.Running);
        h.Script(new GapActivatorRequestException(IpcErrorCodes.Busy, "busy", retryable: true));
        h.Script(new GapActivatorRequestException(IpcErrorCodes.Busy, "busy", retryable: true));

        h.Gateway.Start();
        await h.WaitTimersAsync(1);
        h.Gateway.Status.State.ShouldBe(ConnectionState.Refused);
        h.Gateway.Status.ErrorCode.ShouldBe("busy");

        h.Time.Advance(ServiceGateway.RetryDelays[0]);
        await h.WaitTimersAsync(2);

        h.Attempts.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Start_OtherFailure_StaysConnectingAndLogsAWarning()
    {
        var logger = new ListLogger<ServiceGateway>();
        await using var h = new Harness(ServiceInstallState.Running, logger);
        h.Script(new InvalidOperationException("boom"));

        h.Gateway.Start();
        await h.WaitTimersAsync(1);

        h.Gateway.Status.State.ShouldBe(ConnectionState.Connecting);
        logger.Levels.ShouldBe([Microsoft.Extensions.Logging.LogLevel.Warning]);
    }

    [Fact]
    public void RetryDelays_AreTwoFourEightThenTen() =>
        ServiceGateway.RetryDelays.ShouldBe([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(10)]);

    [Fact]
    public async Task Retries_FollowTheDelays()
    {
        await using var h = new Harness(ServiceInstallState.Stopped);
        for (var i = 0; i < 6; i++)
        {
            h.Script(new TimeoutException());
        }

        h.Gateway.Start();
        TimeSpan[] steps = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)];
        for (var i = 0; i < steps.Length; i++)
        {
            await h.WaitTimersAsync(i + 1);
            h.Time.Advance(steps[i]);
        }

        await h.WaitTimersAsync(6);

        h.Attempts.Select(a => (int)a.TotalSeconds).ShouldBe([0, 2, 6, 14, 24, 34]);
    }

    [Fact]
    public async Task ConnectionChanged_OnlyWhenTheStatusChanges()
    {
        await using var h = new Harness(ServiceInstallState.Stopped);
        for (var i = 0; i < 3; i++)
        {
            h.Script(new TimeoutException());
        }

        var raised = new List<GatewayStatus>();
        h.Gateway.ConnectionChanged += (_, s) => raised.Add(s);

        h.Gateway.Start();
        await h.WaitTimersAsync(1);
        h.Time.Advance(TimeSpan.FromSeconds(2));
        await h.WaitTimersAsync(2);
        h.Time.Advance(TimeSpan.FromSeconds(4));
        await h.WaitTimersAsync(3);

        h.Attempts.Count.ShouldBe(3);
        raised.Count.ShouldBe(1);
        raised[0].State.ShouldBe(ConnectionState.ServiceStopped);
    }

    [Theory]
    [InlineData("GetDashboardAsync")]
    [InlineData("RunNowAsync")]
    [InlineData("UpdateSettingsAsync")]
    public async Task Calls_WhileNotConnected_ThrowRetryableDisconnected(string method)
    {
        await using var h = new Harness(ServiceInstallState.Stopped);
        ServiceGateway gateway = h.Gateway;

        Func<Task> call = () => method switch
        {
            "GetDashboardAsync" => gateway.GetDashboardAsync(TestContext.Current.CancellationToken),
            "RunNowAsync" => gateway.RunNowAsync(Guid.NewGuid(), TestContext.Current.CancellationToken),
            _ => gateway.UpdateSettingsAsync(Dtos.SettingsInput(), TestContext.Current.CancellationToken),
        };

        var ex = await Should.ThrowAsync<GapActivatorRequestException>(call);
        ex.Code.ShouldBe(IpcErrorCodes.Disconnected);
        ex.Retryable.ShouldBeTrue();
        ex.Message.ShouldBe("Not connected to the service.");
    }

    [Fact]
    public async Task Dispose_StopsRetrying()
    {
        var h = new Harness(ServiceInstallState.Stopped);
        h.Script(new TimeoutException());
        h.Script(new TimeoutException());
        h.Gateway.Start();
        await h.WaitTimersAsync(1);

        await h.Gateway.DisposeAsync();
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        h.Attempts.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Start_CalledTwice_StartsOneLoop()
    {
        await using var h = new Harness(ServiceInstallState.Stopped);
        h.Script(new TimeoutException());
        h.Script(new TimeoutException());

        h.Gateway.Start();
        h.Gateway.Start();
        await h.WaitTimersAsync(1);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        h.Attempts.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Guard_DisconnectedFromTheHeldClient_CompletesThatConnectionsSignalAndRethrows()
    {
        var connection = new GatewayConnection(null!);
        var failure = new GapActivatorRequestException(IpcErrorCodes.Disconnected, "gone", retryable: true);

        var ex = await Should.ThrowAsync<GapActivatorRequestException>(
            () => ServiceGateway.Guard<int>(connection, _ => Task.FromException<int>(failure)));

        ex.ShouldBeSameAs(failure);
        connection.Gone.Task.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Guard_OtherFailures_LeaveTheConnectionAlone()
    {
        var connection = new GatewayConnection(null!);

        await Should.ThrowAsync<GapActivatorRequestException>(
            () => ServiceGateway.Guard<int>(connection, _ => Task.FromException<int>(new GapActivatorRequestException(IpcErrorCodes.NotFound, "x", retryable: false))));
        await Should.ThrowAsync<InvalidOperationException>(
            () => ServiceGateway.Guard<int>(connection, _ => Task.FromException<int>(new InvalidOperationException())));

        connection.Gone.Task.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task Guard_StaleConnection_DoesNotEndTheNewerOne()
    {
        var stale = new GatewayConnection(null!);
        var current = new GatewayConnection(null!);

        await Should.ThrowAsync<GapActivatorRequestException>(
            () => ServiceGateway.Guard<int>(stale, _ => Task.FromException<int>(new GapActivatorRequestException(IpcErrorCodes.Disconnected, "gone", retryable: true))));

        stale.Gone.Task.IsCompleted.ShouldBeTrue();
        current.Gone.Task.IsCompleted.ShouldBeFalse();
    }

    /// <summary>A fake clock that counts the timers created, so a test advances only once the loop is waiting.</summary>
    private sealed class SpyTimeProvider : FakeTimeProvider
    {
        private int _timers;

        public int Timers => Volatile.Read(ref _timers);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ITimer timer = base.CreateTimer(callback, state, dueTime, period);
            Interlocked.Increment(ref _timers);
            return timer;
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly Queue<Exception> _script = new();
        private readonly List<TimeSpan> _attempts = [];
        private readonly DateTimeOffset _start;

        public Harness(ServiceInstallState probeState, ILogger<ServiceGateway>? logger = null)
        {
            Time = new SpyTimeProvider();
            _start = Time.GetUtcNow();
            Probe = new FakeServiceStatusProbe { State = probeState };
            Gateway = new ServiceGateway(
                new GapActivatorClientOptions(),
                Probe,
                new ImmediateUiDispatcher(),
                Time,
                logger ?? NullLogger<ServiceGateway>.Instance,
                Connect);
        }

        public SpyTimeProvider Time { get; }

        public FakeServiceStatusProbe Probe { get; }

        public ServiceGateway Gateway { get; }

        public IReadOnlyList<TimeSpan> Attempts
        {
            get
            {
                lock (_attempts)
                {
                    return [.. _attempts];
                }
            }
        }

        public void Script(Exception error) => _script.Enqueue(error);

        public async Task WaitTimersAsync(int count)
        {
            using var cts = new CancellationTokenSource(Wait);
            while (Time.Timers < count)
            {
                cts.Token.ThrowIfCancellationRequested();
                await Task.Delay(1, cts.Token);
            }
        }

        public ValueTask DisposeAsync() => Gateway.DisposeAsync();

        private Task<GapActivatorClient> Connect(GapActivatorClientOptions options, CancellationToken ct)
        {
            lock (_attempts)
            {
                _attempts.Add(Time.GetUtcNow() - _start);
            }

            return Task.FromException<GapActivatorClient>(_script.Dequeue());
        }
    }
}
