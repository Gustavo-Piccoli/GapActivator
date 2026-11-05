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
}
