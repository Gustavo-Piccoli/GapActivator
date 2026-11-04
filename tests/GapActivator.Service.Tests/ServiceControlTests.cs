using GapActivator.Application.Events;
using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using GapActivator.Application.Settings;
using GapActivator.Domain.Common;
using GapActivator.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

    private sealed class RecordingLifetime : IHostApplicationLifetime
    {
        private int _stopCount;

        public int StopCount => Volatile.Read(ref _stopCount);

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => Interlocked.Increment(ref _stopCount);
    }

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
