using GapActivator.App.Services;

namespace GapActivator.App.Tests.Services;

public class ScServiceStarterTests
{
    [Theory]
    [InlineData(0, ServiceStartResult.Started, 0)]
    [InlineData(1056, ServiceStartResult.Started, 0)]
    [InlineData(5, ServiceStartResult.Failed, 5)]
    [InlineData(1060, ServiceStartResult.Failed, 1060)]
    public void MapExitCode_KnownCodes_GiveTheDocumentedResult(int exitCode, ServiceStartResult result, int errorCode) =>
        ScServiceStarter.MapExitCode(exitCode).ShouldBe(new ServiceStartOutcome(result, errorCode));

    [Fact]
    public void CreateStartInfo_ElevatedStart_UsesTheRootedSystemScExe()
    {
        var info = ScServiceStarter.CreateStartInfo();

        Path.IsPathRooted(info.FileName).ShouldBeTrue();
        info.FileName.ShouldEndWith(@"\sc.exe");
        info.FileName.ShouldBe(Path.Combine(Environment.SystemDirectory, "sc.exe"));
        info.Verb.ShouldBe("runas");
    }
}
