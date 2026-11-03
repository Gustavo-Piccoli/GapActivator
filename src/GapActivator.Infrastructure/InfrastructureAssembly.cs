using System.Reflection;

namespace GapActivator.Infrastructure;

/// <summary>Marker used by architecture tests to reference this assembly.</summary>
public static class InfrastructureAssembly
{
    public static Assembly Reference => typeof(InfrastructureAssembly).Assembly;
}
