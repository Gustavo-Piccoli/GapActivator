using System.Reflection;

namespace GapActivator.App;

/// <summary>Marker for the app assembly, used by the architecture tests.</summary>
public static class AppAssembly
{
    public static Assembly Reference { get; } = typeof(AppAssembly).Assembly;
}
