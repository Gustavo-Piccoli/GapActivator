using GapActivator.Domain.Runs;

namespace GapActivator.Infrastructure.Tests;

public sealed class GapActivatorPathsTests
{
    // BI-091
    [Fact]
    public void Paths_Default_IsProgramDataGapActivator()
    {
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string root = Path.Combine(programData, "GapActivator");

        GapActivatorPaths paths = GapActivatorPaths.Default;

        paths.DataRoot.ShouldBe(root);
        paths.DatabaseFile.ShouldBe(Path.Combine(root, "gapactivator.db"));
        paths.LogsDirectory.ShouldBe(Path.Combine(root, "logs"));
        paths.RunLogsDirectory.ShouldBe(Path.Combine(root, "logs", "runs"));
    }

    [Fact]
    public void Paths_FileNames_FollowTheFixedLayout()
    {
        var paths = new GapActivatorPaths(@"D:\Data\GapActivator");
        var runId = new RunId(Guid.Parse("0191D3C4-AB12-7CDE-8F00-1234567890AB"));

        paths.RunOutputFile(runId).ShouldBe(@"D:\Data\GapActivator\logs\runs\0191d3c4-ab12-7cde-8f00-1234567890ab.log");
        paths.ServiceLogFile(new DateOnly(2030, 1, 2)).ShouldBe(@"D:\Data\GapActivator\logs\gapactivator-20300102.log");
    }

    [Theory]
    [InlineData("GapActivator")]
    [InlineData(@"data\GapActivator")]
    [InlineData(@"\GapActivator")]
    [InlineData(@"C:GapActivator")]
    public void Paths_RelativeDataRoot_Throws(string dataRoot) =>
        Should.Throw<ArgumentException>(() => new GapActivatorPaths(dataRoot));
}
