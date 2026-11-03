using System.Reflection;

namespace GapActivator.Application;

/// <summary>Marker used by architecture tests to reference this assembly.</summary>
public static class ApplicationAssembly
{
    public static Assembly Reference => typeof(ApplicationAssembly).Assembly;
}
