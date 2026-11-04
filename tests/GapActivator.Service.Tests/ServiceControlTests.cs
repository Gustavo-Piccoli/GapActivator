using GapActivator.Application.Events;
using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using GapActivator.Application.Settings;
using GapActivator.Domain.Common;
using GapActivator.Ipc;
using GapActivator.Service.Tests.Support;
using GapActivator.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GapActivator.Service.Tests;

public sealed class ServiceControlTests
{
    // BI-077 (addition c): the stop begins only after the restart command has published its events.
    [Fact]
    public async Task Restart_CommandSent_StopBeginsOnlyAfterEventsArePublished()
    {
        var lifetime = new RecordingLifetime();
        var exitCodes = new List<int>();
        await using ApplicationTestHost host = CreateHost(lifetime, exitCodes);

        await host.Send<RestartGapActivator, Unit>(new RestartGapActivator(), ct: TestContext.Current.CancellationToken);

        StopProbe probe = host.Services.GetRequiredService<StopProbe>();
        probe.StopCountsSeen.ShouldBe([0]);
        lifetime.StopCount.ShouldBe(1);
        exitCodes.ShouldBe([ServiceExitCodes.Restart]);
        host.Services.GetRequiredService<ExitCodeSink>().Last.ShouldBe(3);
    }

    [Fact]
    public async Task Restart_RestartAsync_ReturnsAtOnceWithoutStopping()
    {
        var lifetime = new RecordingLifetime();
        var exitCodes = new List<int>();
        await using ApplicationTestHost host = CreateHost(lifetime, exitCodes);

        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
        {
            Task restart = scope.ServiceProvider.GetRequiredService<IServiceControl>()
                .RestartAsync(TestContext.Current.CancellationToken);

            restart.IsCompletedSuccessfully.ShouldBeTrue();
            lifetime.StopCount.ShouldBe(0);
            exitCodes.ShouldBeEmpty();
        }

        lifetime.StopCount.ShouldBe(1);
        exitCodes.ShouldBe([ServiceExitCodes.Restart]);
    }

    [Fact]
    public async Task Restart_TwoRequests_StopOnce()
    {
        var lifetime = new RecordingLifetime();
        var exitCodes = new List<int>();
        await using ApplicationTestHost host = CreateHost(lifetime, exitCodes);

        await host.Send<RestartGapActivator, Unit>(new RestartGapActivator(), ct: TestContext.Current.CancellationToken);
        await host.Send<RestartGapActivator, Unit>(new RestartGapActivator(), ct: TestContext.Current.CancellationToken);

        lifetime.StopCount.ShouldBe(1);
        exitCodes.ShouldBe([ServiceExitCodes.Restart]);
    }

    [Fact]
    public async Task Restart_ScopeDisposedWithoutRequest_DoesNotStop()
    {
        var lifetime = new RecordingLifetime();
        var exitCodes = new List<int>();
        await using ApplicationTestHost host = CreateHost(lifetime, exitCodes);

        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IServiceControl>().ShouldNotBeNull();
        }

        lifetime.StopCount.ShouldBe(0);
        exitCodes.ShouldBeEmpty();
    }

    [Fact]
    public void HostRestart_Deferred_BeginsOnlyWhenTheLastDeferralEnds()
    {
        var lifetime = new RecordingLifetime();
        var exitCodes = new List<int>();
        var restart = new HostRestart(lifetime, new ExitCodeSink(exitCodes.Add));

        IDisposable first = restart.Defer();
        IDisposable second = restart.Defer();
        restart.Begin();
        lifetime.StopCount.ShouldBe(0);

        first.Dispose();
        lifetime.StopCount.ShouldBe(0);
        exitCodes.ShouldBeEmpty();

        second.Dispose();
        second.Dispose();
        lifetime.StopCount.ShouldBe(1);
        exitCodes.ShouldBe([ServiceExitCodes.Restart]);
    }

    [Fact]
    public void HostRestart_NotDeferred_BeginsAtOnce()
    {
        var lifetime = new RecordingLifetime();
        var exitCodes = new List<int>();
        var restart = new HostRestart(lifetime, new ExitCodeSink(exitCodes.Add));

        restart.Begin();
        restart.Begin();

        lifetime.StopCount.ShouldBe(1);
        exitCodes.ShouldBe([ServiceExitCodes.Restart]);
    }

    [Fact]
    public void HostRestart_DeferralEndsWithoutBegin_DoesNotStop()
    {
        var lifetime = new RecordingLifetime();
        var restart = new HostRestart(lifetime, new ExitCodeSink(_ => { }));

        restart.Defer().Dispose();

        lifetime.StopCount.ShouldBe(0);
    }

    // The restart route's response is on the wire before the stop begins.
    [Fact]
    [Trait("Category", "Integration")]
    public async Task RestartService_StopBeginsOnlyAfterTheResponseIsWritten()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using IpcTestServer server = await IpcTestServer.StartAsync(
            services => services.Replace(ServiceDescriptor.Scoped<IServiceControl, ScopedServiceControl>()));
        bool? responseWrittenFirst = null;
        server.Lifetime.OnStop = () =>
        {
            responseWrittenFirst = server.Host.Log.Entries.Any(
                e => e.Level == LogLevel.Debug && e.Message == "Response 2 to RestartService was written.");
            _ = server.StopAsync();
        };
        await using GapActivatorClient client = await server.ConnectAsync();
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += (_, _) => disconnected.TrySetResult();

        await client.RestartServiceAsync(ct);

        await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        server.Lifetime.StopCount.ShouldBe(1);
        responseWrittenFirst.ShouldBe(true);
        server.ExitCodes.ShouldBe([ServiceExitCodes.Restart]);
    }

    private static ApplicationTestHost CreateHost(RecordingLifetime lifetime, List<int> exitCodes) =>
        new(services =>
        {
            services.AddSingleton<IHostApplicationLifetime>(lifetime);
            services.AddSingleton(new ExitCodeSink(exitCodes.Add));
            services.AddSingleton<HostRestart>();
            services.AddScoped<IServiceControl, ScopedServiceControl>();
            services.AddSingleton(new StopProbe(lifetime));
            services.AddScoped<INotificationHandler>(sp => sp.GetRequiredService<StopProbe>());
        });

    /// <summary>Records how many stops had been requested each time a <see cref="GapActivatorRestarted"/> arrived.</summary>
    private sealed class StopProbe(RecordingLifetime lifetime) : INotificationHandler
    {
        private readonly List<int> _seen = [];

        public IReadOnlyList<int> StopCountsSeen => _seen;

        public Task HandleAsync(IDomainEvent domainEvent, CancellationToken ct)
        {
            if (domainEvent is GapActivatorRestarted)
            {
                _seen.Add(lifetime.StopCount);
            }

            return Task.CompletedTask;
        }
    }
}
