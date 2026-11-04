namespace GapActivator.Service.Tests.Support;

/// <summary>
/// Where the child processes and the service executable are: the project references copy them next to the tests, in
/// whichever configuration the tests were built.
/// </summary>
internal static class ChildPaths
{
    public static string TestChild => Path.Combine(AppContext.BaseDirectory, "GapActivator.TestChild.exe");

    public static string SampleChild => Path.Combine(AppContext.BaseDirectory, "GapActivator.SampleChild.exe");

    public static string ServiceExe => Path.Combine(AppContext.BaseDirectory, "GapActivator.Service.exe");
}
