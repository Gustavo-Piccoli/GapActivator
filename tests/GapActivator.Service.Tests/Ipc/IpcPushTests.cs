using GapActivator.Application.Events;
using GapActivator.Application.Ports;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Ipc;
using GapActivator.Ipc.Contracts;
using GapActivator.Service.Ipc;
using GapActivator.Service.Tests.Support;
using GapActivator.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using NotificationSeverity = GapActivator.Application.Ports.NotificationSeverity;

namespace GapActivator.Service.Tests.Ipc;

/// <summary>Server pushes: notifications, coalesced state hints and the drop of a client that stopped reading.</summary>
[Trait("Category", "Integration")]
public sealed class IpcPushTests
{
    private static readonly TimeSpan Short = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(600);

    private static readonly EmailInputDto TestEmail = new(
        true,
        "127.0.0.1",
        1025,
        TlsMode.None,
        null,
        new SecretChangeDto(SecretChangeKind.Clear, null),
        "sender@example.com",
        ["to@example.com"],
        [AlertKind.CycleFailed]);

    [Fact]
    public async Task Command_Committed_PushesStateChangedToEveryConnection()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        await using GapActivatorClient first = await server.ConnectAsync();
        await using GapActivatorClient second = await server.ConnectAsync();
        using var firstPushes = new PushRecorder(first);
        using var secondPushes = new PushRecorder(second);

        await first.PauseInstallationAsync(TestContext.Current.CancellationToken);

        (await firstPushes.WaitForStateChangedAsync(1, Short)).ShouldBeTrue();
        (await secondPushes.WaitForStateChangedAsync(1, Short)).ShouldBeTrue();
        secondPushes.StateChanges[0].ShouldBe(new StateChangedDto(null, null));
    }

    [Fact]
    public async Task QueriesOnly_PushNoStateChanged()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        await using GapActivatorClient client = await server.ConnectAsync();
        using var pushes = new PushRecorder(client);

        for (int i = 0; i < 5; i++)
        {
            await client.GetDashboardAsync(TestContext.Current.CancellationToken);
        }

        (await pushes.WaitForStateChangedAsync(1, Quiet)).ShouldBeFalse();
    }

    [Fact]
    public async Task UngatedCommand_PushesNoStateChanged()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        await using GapActivatorClient client = await server.ConnectAsync();
        using var pushes = new PushRecorder(client);

        await client.SendTestEmailAsync(TestEmail, TestContext.Current.CancellationToken);

        server.Host.Email.Sent.Count.ShouldBe(1);
        (await pushes.WaitForStateChangedAsync(1, Quiet)).ShouldBeFalse();
    }

    [Fact]
    public async Task Event_WithIds_PushesThoseIds()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        await using GapActivatorClient client = await server.ConnectAsync();
        using var pushes = new PushRecorder(client);
        var runId = RunId.New();
        var processId = ProcessId.New();

        using (IServiceScope scope = server.Host.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<EventPublisher>().PublishAsync(
                [new RunCompleted(runId, processId, GapActivator.Domain.Runs.RunOutcome.Succeeded, 0, false, ApplicationTestHost.StartTime)],
                TestContext.Current.CancellationToken);
        }

        (await pushes.WaitForStateChangedAsync(1, Short)).ShouldBeTrue();
        pushes.StateChanges[0].ShouldBe(new StateChangedDto(processId.Value, runId.Value));
    }

    [Fact]
    public async Task EventBurst_IsCoalescedPerConnection()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        await using GapActivatorClient client = await server.ConnectAsync();
        using var pushes = new PushRecorder(client);

        using (IServiceScope scope = server.Host.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<EventPublisher>();
            for (int i = 0; i < 50; i++)
            {
                await publisher.PublishAsync(
                    [new RunStarted(RunId.New(), ProcessId.New(), i + 1, ApplicationTestHost.StartTime)],
                    TestContext.Current.CancellationToken);
            }
        }

        DateTimeOffset lastPublish = DateTimeOffset.UtcNow;
        (await pushes.WaitForStateChangedAsync(2, Short)).ShouldBeTrue();
        await Task.Delay(Quiet, TestContext.Current.CancellationToken);

        pushes.StateChanges.Count.ShouldBeInRange(1, 3);
        pushes.LastStateChangedAt.ShouldBeGreaterThan(lastPublish);
    }

    [Fact]
    public async Task Notification_IsBroadcastSanitizedAndLoggedAtItsSeverity()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        await using GapActivatorClient client = await server.ConnectAsync();
        using var pushes = new PushRecorder(client);
        var processId = ProcessId.New();
        var runId = RunId.New();

        await server.Host.Services.GetRequiredService<IAppNotifier>().NotifyAsync(
            new AppNotification(
                NotificationSeverity.Error, "Run failed\r\nFAKE ENTRY", "line1\nline2\u001b", processId, runId),
            TestContext.Current.CancellationToken);

        (await pushes.WaitForNotificationsAsync(1, Short)).ShouldBeTrue();
        pushes.Notifications[0].ShouldBe(new NotificationDto(
            GapActivator.Ipc.Contracts.NotificationSeverity.Error,
            "Run failed  FAKE ENTRY",
            "line1 line2 ",
            processId.Value,
            runId.Value));
        LogEntry entry = server.Host.Log.Entries.Single(e => e.Category == "PipeBroadcastNotifier");
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Message.ShouldBe("Run failed  FAKE ENTRY: line1 line2 ");
    }

    [Fact]
    public async Task ConcurrentRequestsAndPushes_FramesNeverInterleave()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync();
        await using GapActivatorClient client = await server.ConnectAsync();
        using var pushes = new PushRecorder(client);
        IAppNotifier notifier = server.Host.Services.GetRequiredService<IAppNotifier>();

        Task broadcasts = Task.Run(
            async () =>
            {
                for (int i = 0; i < 50; i++)
                {
                    await notifier.NotifyAsync(
                        new AppNotification(NotificationSeverity.Information, $"n{i}", "m", null, null),
                        TestContext.Current.CancellationToken);
                }
            },
            TestContext.Current.CancellationToken);
        Task<DashboardDto>[] requests =
        [
            .. Enumerable.Range(0, 50).Select(_ => Task.Run(
                () => client.GetDashboardAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)),
        ];

        await broadcasts.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        DashboardDto[] results = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        results.Length.ShouldBe(50);
        (await pushes.WaitForNotificationsAsync(50, TimeSpan.FromSeconds(10))).ShouldBeTrue();
        pushes.Notifications.Select(n => n.Title).ShouldBe(Enumerable.Range(0, 50).Select(i => $"n{i}"));
        server.Registry.Count.ShouldBe(1);
        pushes.Disconnects.ShouldBe(0);
    }

    [Fact]
    public async Task StuckClient_IsDroppedWithinTheWriteTimeout_OthersStillGetPushes()
    {
        await using IpcTestServer server = await IpcTestServer.StartAsync(
            options: o => o with { WriteTimeout = TimeSpan.FromSeconds(1) });
        await using GapActivatorClient normal = await server.ConnectAsync();
        using var pushes = new PushRecorder(normal);
        await using RawPipeClient stuck = await server.ConnectRawAsync();
        await stuck.HelloAsync();
        server.Registry.Count.ShouldBe(2);

        // The stuck client asks for large answers and never reads them.
        Task flood = Task.Run(
            async () =>
            {
                const string Payload = """{"rule":"FREQ=HOURLY","anchor":"2030-01-01T12:00:00+00:00","count":50}""";
                for (int i = 0; i < 200; i++)
                {
                    await stuck.SendRequestAsync(i + 2, "PreviewSchedule", Payload);
                }
            },
            TestContext.Current.CancellationToken);

        // The normal client alternates two commands, so every send changes the state.
        using var stop = new CancellationTokenSource();
        Task commands = Task.Run(
            async () =>
            {
                for (int i = 0; !stop.IsCancellationRequested; i++)
                {
                    await (i % 2 == 0 ? normal.PauseInstallationAsync(stop.Token) : normal.ResumeInstallationAsync(stop.Token));
                    await Task.Delay(200, stop.Token);
                }
            },
            TestContext.Current.CancellationToken);

        bool dropped = await BecomesTrueAsync(() => server.Registry.Count == 1, TimeSpan.FromSeconds(6));
        int seenAtDrop = pushes.StateChanges.Count;
        bool stillPushed = await pushes.WaitForStateChangedAsync(seenAtDrop + 2, TimeSpan.FromSeconds(3));
        await stop.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => commands);

        dropped.ShouldBeTrue();
        seenAtDrop.ShouldBeGreaterThan(0);
        stillPushed.ShouldBeTrue();
        server.Host.Log.Entries.ShouldContain(
            e => e.Level == LogLevel.Warning && e.Message.Contains("stopped reading", StringComparison.Ordinal));
        try
        {
            await flood.WaitAsync(Short, TestContext.Current.CancellationToken);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or TimeoutException)
        {
            // The server closed the pipe under the flood.
        }
    }

    private static async Task<bool> BecomesTrueAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!condition())
        {
            try
            {
                await Task.Delay(10, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return condition();
            }
        }

        return true;
    }

    /// <summary>Collects the pushes a client raises.</summary>
    private sealed class PushRecorder : IDisposable
    {
        private readonly GapActivatorClient _client;
        private readonly Lock _sync = new();
        private readonly List<StateChangedDto> _states = [];
        private readonly List<NotificationDto> _notifications = [];
        private DateTimeOffset _lastState;
        private int _disconnects;

        public PushRecorder(GapActivatorClient client)
        {
            _client = client;
            client.StateChanged += OnState;
            client.NotificationReceived += OnNotification;
            client.Disconnected += OnDisconnected;
        }

        public int Disconnects => Volatile.Read(ref _disconnects);

        public IReadOnlyList<StateChangedDto> StateChanges
        {
            get
            {
                lock (_sync)
                {
                    return [.. _states];
                }
            }
        }

        public IReadOnlyList<NotificationDto> Notifications
        {
            get
            {
                lock (_sync)
                {
                    return [.. _notifications];
                }
            }
        }

        public DateTimeOffset LastStateChangedAt
        {
            get
            {
                lock (_sync)
                {
                    return _lastState;
                }
            }
        }

        public Task<bool> WaitForStateChangedAsync(int count, TimeSpan timeout) =>
            BecomesTrueAsync(() => StateChanges.Count >= count, timeout);

        public Task<bool> WaitForNotificationsAsync(int count, TimeSpan timeout) =>
            BecomesTrueAsync(() => Notifications.Count >= count, timeout);

        public void Dispose()
        {
            _client.StateChanged -= OnState;
            _client.NotificationReceived -= OnNotification;
            _client.Disconnected -= OnDisconnected;
        }

        private void OnState(object? sender, StateChangedDto dto)
        {
            lock (_sync)
            {
                _states.Add(dto);
                _lastState = DateTimeOffset.UtcNow;
            }
        }

        private void OnNotification(object? sender, NotificationDto dto)
        {
            lock (_sync)
            {
                _notifications.Add(dto);
            }
        }

        private void OnDisconnected(object? sender, DisconnectedEventArgs args) => Interlocked.Increment(ref _disconnects);
    }
}
