using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.ViewModels;
using GapActivator.Ipc;
using Microsoft.Extensions.Time.Testing;

namespace GapActivator.App.Tests.ViewModels;

public class ConnectionBannerViewModelTests : IAsyncDisposable
{
    private readonly Localizer _l = new(AppLanguage.English);
    private readonly FakeServiceGateway _gateway = new();
    private readonly FakeServiceStarter _starter = new();
    private readonly FakeTimeProvider _time = new();

    private ConnectionBannerViewModel Create(GatewayStatus status)
    {
        _gateway.SetStatus(status);
        return new ConnectionBannerViewModel(_l, _gateway, _starter, _time);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return _gateway.DisposeAsync();
    }

    [Fact]
    public void Banner_Connected_IsHidden()
    {
        var banner = Create(FakeServiceGateway.Operator());

        banner.IsVisible.ShouldBeFalse();
        banner.Text.ShouldBeNull();
        banner.ShowStartService.ShouldBeFalse();
    }

    [Fact]
    public void Banner_Connecting_ShowsTheConnectingText()
    {
        var banner = Create(GatewayStatus.Initial);

        banner.IsVisible.ShouldBeTrue();
        banner.Text.ShouldBe("Connecting to the service…");
        banner.ShowStartService.ShouldBeFalse();
    }

    [Fact]
    public void Banner_ServiceStopped_ShowsTextAndStartService()
    {
        var banner = Create(FakeServiceGateway.Stopped);

        banner.IsVisible.ShouldBeTrue();
        banner.Text.ShouldBe("The GapActivator service is stopped.");
        banner.ShowStartService.ShouldBeTrue();
    }

    [Fact]
    public void Banner_NotInstalled_SaysSoWithoutStartService()
    {
        var banner = Create(FakeServiceGateway.NotInstalled);

        banner.Text.ShouldBe("The GapActivator service is not installed.");
        banner.ShowStartService.ShouldBeFalse();
    }

    [Fact]
    public void Banner_Refused_ShowsTheServerNotVerifiedText()
    {
        var banner = Create(GatewayStatus.Initial with { State = ConnectionState.Refused, ErrorCode = IpcErrorCodes.ServerNotVerified });

        banner.Text.ShouldBe("The pipe is not served by the installed GapActivator service. The connection was refused.");
        banner.ShowStartService.ShouldBeFalse();
    }

    [Fact]
    public void Banner_LanguageSwitched_RewritesTheText()
    {
        var banner = Create(FakeServiceGateway.Stopped);

        _l.SetLanguage(AppLanguage.Portuguese);

        banner.Text.ShouldBe("O serviço GapActivator está parado.");
    }

    [Fact]
    public void DevelopmentBanner_ConnectedButNotVerified_IsShownUntilDisconnected()
    {
        var banner = Create(FakeServiceGateway.DevOperator);

        banner.IsDevelopmentMode.ShouldBeTrue();
        banner.DevelopmentText.ShouldBe("Not connected to the installed service — development mode");
        banner.IsVisible.ShouldBeFalse();

        _gateway.SetStatus(FakeServiceGateway.Stopped);

        banner.IsDevelopmentMode.ShouldBeFalse();
    }

    [Fact]
    public void DevelopmentBanner_VerifiedService_IsNotShown()
    {
        var banner = Create(FakeServiceGateway.Operator());

        banner.IsDevelopmentMode.ShouldBeFalse();
    }

    [Fact]
    public async Task StartService_Declined_ChangesNothing()
    {
        var banner = Create(FakeServiceGateway.Stopped);
        _starter.Outcome = new ServiceStartOutcome(ServiceStartResult.Declined, 1223);

        await banner.StartServiceCommand.ExecuteAsync(null);

        _starter.Calls.ShouldBe(1);
        banner.Text.ShouldBe("The GapActivator service is stopped.");
        banner.ShowStartService.ShouldBeTrue();
        banner.ErrorText.ShouldBeNull();
    }

    [Fact]
    public async Task StartService_Started_ShowsStartingUntilConnected()
    {
        var banner = Create(FakeServiceGateway.Stopped);

        Task run = banner.StartServiceCommand.ExecuteAsync(null);

        banner.Text.ShouldBe("Starting the service…");
        banner.ShowStartService.ShouldBeFalse();

        _gateway.SetStatus(FakeServiceGateway.Operator());
        await run;

        banner.IsVisible.ShouldBeFalse();
    }

    [Fact]
    public async Task StartService_StartedButNeverConnects_StopsShowingStartingAfterThirtySeconds()
    {
        var banner = Create(FakeServiceGateway.Stopped);

        Task run = banner.StartServiceCommand.ExecuteAsync(null);
        banner.Text.ShouldBe("Starting the service…");

        _time.Advance(TimeSpan.FromSeconds(30));
        await run;

        banner.Text.ShouldBe("The GapActivator service is stopped.");
        banner.ShowStartService.ShouldBeTrue();
    }

    [Fact]
    public async Task StartService_Failed_ShowsTheErrorCode()
    {
        var banner = Create(FakeServiceGateway.Stopped);
        _starter.Outcome = new ServiceStartOutcome(ServiceStartResult.Failed, 5);

        await banner.StartServiceCommand.ExecuteAsync(null);

        banner.ErrorText.ShouldBe("The service could not be started (error 5).");
        banner.ShowStartService.ShouldBeTrue();
    }
}
