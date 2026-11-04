using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GapActivator.Service.Ipc;

public static class IpcServiceCollectionExtensions
{
    /// <summary>
    /// Registers the pipe server and what it needs. It does not register a hosted service, and it expects
    /// <see cref="HostRestart"/> to be registered.
    /// </summary>
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
        return services;
    }
}
