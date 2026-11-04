namespace GapActivator.Infrastructure.Tests.Support;

/// <summary>Where the test child process is: the TestChild project reference copies it next to the tests.</summary>
internal static class ChildPaths
{
    public static string TestChild => Path.Combine(AppContext.BaseDirectory, "GapActivator.TestChild.exe");
}
