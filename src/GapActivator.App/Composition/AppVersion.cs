using System.Reflection;

namespace GapActivator.App.Composition;

internal static class AppVersion
{
    public static string Informational { get; } =
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    public static string Display { get; } = ToDisplay(Informational);

    /// <summary>The version without build metadata: the text before '+'.</summary>
    internal static string ToDisplay(string informational)
    {
        ArgumentNullException.ThrowIfNull(informational);
        return informational.Split('+')[0];
    }
}
