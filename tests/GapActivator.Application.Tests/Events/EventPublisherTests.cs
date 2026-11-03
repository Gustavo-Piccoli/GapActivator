using GapActivator.Application.Events;
using GapActivator.Application.Messaging;
using GapActivator.Application.Tests.Messaging;
using GapActivator.Domain.Common;
using GapActivator.Domain.Settings;
using GapActivator.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace GapActivator.Application.Tests.Events;

public class EventPublisherTests
{
    // BI-096
    [Fact]
    public async Task Publish_HandlerThrows_OtherHandlersStillRun()
    {
        var state = new ProbeState();
        await using ApplicationTestHost host = ProbeHost.Create(
            state, services => services.AddScoped<INotificationHandler, ThrowingNotificationHandler>());

        int result = await host.Run<ProbeCommand, int>(new ProbeCommand());

        result.ShouldBe(42);
        state.Published.ShouldHaveSingleItem().ShouldBeOfType<InstallationPaused>();
        host.Log.Entries.Any(e => e.Level == LogLevel.Warning && e.Exception?.Message == "notification failed")
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Publish_Event_IsLoggedAtInformation()
    {
        var state = new ProbeState();
        await using ApplicationTestHost host = ProbeHost.Create(state);

        await host.Run<ProbeCommand, int>(new ProbeCommand());

        host.Log.Entries.Any(e =>
            e.Level == LogLevel.Information && e.Message.Contains("InstallationPaused", StringComparison.Ordinal))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Publish_NoHandlersRegistered_Completes()
    {
        var publisher = new EventPublisher([], NullLogger<EventPublisher>.Instance);

        await publisher.PublishAsync([new InstallationPaused(DateTimeOffset.UnixEpoch)], CancellationToken.None);
    }

    [Fact]
    public async Task Publish_CancelledWhileHandling_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var publisher = new EventPublisher([new CancellingHandler()], NullLogger<EventPublisher>.Instance);

        await Should.ThrowAsync<OperationCanceledException>(() =>
            publisher.PublishAsync([new InstallationPaused(DateTimeOffset.UnixEpoch)], cts.Token));
    }

    private sealed class CancellingHandler : INotificationHandler
    {
        public Task HandleAsync(IDomainEvent domainEvent, CancellationToken ct) =>
            Task.FromCanceled(ct);
    }
}
