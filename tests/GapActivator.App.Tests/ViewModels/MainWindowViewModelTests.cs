using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace GapActivator.App.Tests.ViewModels;

public class MainWindowViewModelTests : IAsyncDisposable
{
    private readonly Localizer _l = new(AppLanguage.English);
    private readonly FakeServiceGateway _gateway = new();
    private readonly Navigator _navigator = new();

    private MainWindowViewModel Create() => new(
        _l,
        _navigator,
        new ConnectionBannerViewModel(_l, _gateway, new FakeServiceStarter(), new FakeTimeProvider()),
        new Permissions(_l, _gateway));

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return _gateway.DisposeAsync();
    }

    [Fact]
    public void NavItems_AreTheFivePagesInOrder()
    {
        var vm = Create();

        vm.NavItems.Select(i => i.Page).ShouldBe([AppPage.Dashboard, AppPage.Processes, AppPage.History, AppPage.Settings, AppPage.About]);
        vm.NavItems.Select(i => i.Label).ShouldBe(["Dashboard", "Processes", "History", "Settings", "About"]);
    }

    [Fact]
    public void SelectingAnItem_NavigatesAndUpdatesCurrentPage()
    {
        var vm = Create();

        vm.SelectedNavItem = vm.NavItems[2];

        _navigator.Current.ShouldBe(AppPage.History);
        vm.CurrentPage.ShouldBe(AppPage.History);
    }

    [Fact]
    public void NavigatorRequest_SelectsTheMatchingItem()
    {
        var vm = Create();

        _navigator.OpenRun(Guid.NewGuid());

        vm.CurrentPage.ShouldBe(AppPage.History);
        vm.SelectedNavItem.Page.ShouldBe(AppPage.History);
        vm.PageTitle.ShouldBe("History");
    }

    [Fact]
    public void LanguageSwitched_RelabelsTheNavigation()
    {
        var vm = Create();

        _l.SetLanguage(AppLanguage.Portuguese);

        vm.NavItems[0].Label.ShouldBe("Painel");
        vm.PageTitle.ShouldBe("Painel");
    }

    [Fact]
    public void FooterText_FollowsTheConnection()
    {
        var vm = Create();
        vm.IsConnected.ShouldBeTrue();
        vm.FooterText.ShouldBe("Connected");

        _gateway.SetStatus(FakeServiceGateway.Stopped);

        vm.IsConnected.ShouldBeFalse();
        vm.FooterText.ShouldBe("Not connected");
    }
}
