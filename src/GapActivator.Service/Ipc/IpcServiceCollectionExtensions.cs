using GapActivator.Application.Events;
using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GapActivator.Service.Ipc;

public static class IpcServiceCollectionExtensions
{
    /// <summary>
    /// Registers the pipe server and what it needs: the pipe-broadcast <see cref="IAppNotifier"/> (replacing any other), the
    /// <c>StateChanged</c> event handler and a wrapper around the registered <see cref="ICommandDispatcher"/> that hints
    /// after each committed command. It does not register a hosted service, and it expects <see cref="HostRestart"/> and an
    /// <see cref="ICommandDispatcher"/> to be registered.
    /// </summary>
    /// <exception cref="InvalidOperationException">No <see cref="ICommandDispatcher"/> is registered.</exception>
    public static IServiceCollection AddGapActivatorIpc(
        this IServiceCollection services,
        IpcOptions ipc,
        AuthorizationOptions authorization)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(ipc);
        ArgumentNullException.ThrowIfNull(authorization);

        services.AddSingleton(ipc);
        services.AddSingleton(authorization);
        services.AddSingleton<IpcRequestRouter>();
        services.AddSingleton<IpcConnectionRegistry>();
        services.AddSingleton<IpcServer>();
        services.TryAddSingleton<ICallerResolver, WindowsCallerResolver>();

        services.Replace(ServiceDescriptor.Singleton<IAppNotifier, PipeBroadcastNotifier>());
        services.AddScoped<INotificationHandler, StateChangedNotificationHandler>();
        DecorateCommandDispatcher(services);
        return services;
    }

    /// <summary>Wraps the last <see cref="ICommandDispatcher"/> descriptor, so a test's scripted dispatcher is kept.</summary>
    private static void DecorateCommandDispatcher(IServiceCollection services)
    {
        int index = -1;
        for (int i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(ICommandDispatcher) && !services[i].IsKeyedService)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            throw new InvalidOperationException("AddGapActivatorIpc needs an ICommandDispatcher to be registered first.");
        }

        ServiceDescriptor existing = services[index];
        Func<IServiceProvider, object> create = existing switch
        {
            { ImplementationFactory: { } factory } => factory,
            { ImplementationType: { } type } => provider => ActivatorUtilities.CreateInstance(provider, type),
            { ImplementationInstance: { } instance } => _ => instance,
            _ => throw new InvalidOperationException("The ICommandDispatcher registration cannot be wrapped."),
        };

        services[index] = new ServiceDescriptor(
            typeof(ICommandDispatcher),
            provider => new StateChangedCommandDispatcher(
                (ICommandDispatcher)create(provider),
                provider.GetRequiredService<IpcConnectionRegistry>()),
            existing.Lifetime);
    }
}
