using System.Reflection;

namespace GapActivator.Service;

internal static class ServiceInfo
{
    /// <summary>The informational version of this assembly, as the service reports it in Hello.</summary>
    public static string Version { get; } =
        typeof(ServiceInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";
}
