using GapActivator.Application.Events;
using GapActivator.Application.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GapActivator.Application;

public static class DependencyInjection
{
    /// <summary>Registers the application layer. The host or tests may override the <see cref="TimeProvider"/>.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<WriteGate>();
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
        services.AddSingleton<IQueryDispatcher, QueryDispatcher>();
        services.AddScoped<CallerAccessor>();
        services.AddScoped<AfterCommit>();
        services.AddScoped<EventBuffer>();
        services.AddScoped<EventPublisher>();
        return services;
    }
}
