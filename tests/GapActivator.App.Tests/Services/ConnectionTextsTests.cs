using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Tests.Fakes;

namespace GapActivator.App.Tests.Services;

public class ConnectionTextsTests
{
    private readonly Localizer _l = new(AppLanguage.English);

    [Fact]
    public void Banner_Connected_IsNull() => ConnectionTexts.Banner(FakeServiceGateway.Operator(), _l).ShouldBeNull();

    [Fact]
    public void Banner_RefusedWithoutACode_FallsBackToNotConnected() =>
        ConnectionTexts.Banner(GatewayStatus.Initial with { State = ConnectionState.Refused }, _l).ShouldBe("Not connected to the service.");
}
