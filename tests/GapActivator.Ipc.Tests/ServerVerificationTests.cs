namespace GapActivator.Ipc.Tests;

public class ServerVerificationTests
{
    private const uint ServerPid = 4242;

    private static ServiceLookup Lookup(string kind, uint number) => kind switch
    {
        "NotInstalled" => new ServiceLookup.NotInstalled(),
        "Running" => new ServiceLookup.Running(number),
        "Stopped" => new ServiceLookup.Stopped(),
        _ => new ServiceLookup.Failed((int)number),
    };

    [Theory]
    [InlineData("NotInstalled", 0u, null)]
    [InlineData("Running", ServerPid, true)]
    [InlineData("Running", ServerPid + 1, false)]
    [InlineData("Stopped", 0u, false)]
    [InlineData("Failed", 5u, false)]
    public void Decide_EveryLookup_GivesTheExpectedVerdict(string kind, uint number, bool? expected) =>
        ServerVerification.Decide(ServerPid, Lookup(kind, number)).ShouldBe(expected);

    [Fact]
    public void Scm_LookupOfAServiceThatDoesNotExist_IsNotInstalled() =>
        ScmServiceLookup.Query("GapActivator.Tests.NotInstalled").ShouldBeOfType<ServiceLookup.NotInstalled>();
}
