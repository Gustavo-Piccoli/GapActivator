using System.Reflection;

namespace GapActivator.Domain;

/// <summary>Marker used by architecture tests to reference this assembly.</summary>
public static class DomainAssembly
{
    public static Assembly Reference => typeof(DomainAssembly).Assembly;
}
