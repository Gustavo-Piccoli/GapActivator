using System.Security.Principal;

namespace GapActivator.Service.Tests.Support;

/// <summary>Runtime check used by the elevated-only integration tests, which skip themselves otherwise.</summary>
internal static class Elevation
{
    public static bool IsElevated
    {
        get
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static void SkipUnlessElevated() =>
        Assert.SkipUnless(IsElevated, "Needs an elevated process.");
}
