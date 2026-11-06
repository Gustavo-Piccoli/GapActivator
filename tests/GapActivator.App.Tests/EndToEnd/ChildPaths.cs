namespace GapActivator.App.Tests.EndToEnd;

/// <summary>Where the sample child is: the project reference copies it next to the tests, in whichever configuration they were built.</summary>
internal static class ChildPaths
{
    public static string SampleChild => Path.Combine(AppContext.BaseDirectory, "GapActivator.SampleChild.exe");
}
