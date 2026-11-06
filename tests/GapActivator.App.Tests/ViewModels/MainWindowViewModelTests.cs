using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.Updates;
using GapActivator.App.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace GapActivator.App.Tests.ViewModels;

public class MainWindowViewModelTests : IAsyncDisposable
{
    private readonly Localizer _l = new(AppLanguage.English);
    private readonly FakeServiceGateway _gateway = new();
    private readonly Navigator _navigator = new();

    private MainWindowViewModel Create()
    {
        var harness = new DashboardHarness(gateway: _gateway, localizer: _l, navigator: _navigator);
        return new MainWindowViewModel(
            _l,
            _navigator,
            new ConnectionBannerViewModel(_l, _gateway, new FakeServiceStarter(), new FakeTimeProvider()),
            harness.Permissions,
            harness.Dashboard,
            harness.Processes,
            harness.History,
            harness.Settings,
            harness.About,
            harness.Updates,
            new ImmediateUiDispatcher());
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return _gateway.DisposeAsync();
    }

    [Fact]
    public void PageContent_History_IsTheHistoryViewModel()
    {
        var vm = Create();

        vm.PageContent.ShouldBeOfType<DashboardViewModel>();
        _navigator.Navigate(AppPage.History);

        vm.PageContent.ShouldBeSameAs(vm.History);
    }

    [Fact]
    public void PageContent_Settings_IsTheSettingsViewModel()
    {
        var vm = Create();

        _navigator.Navigate(AppPage.Settings);

        vm.PageContent.ShouldBeSameAs(vm.Settings);
    }

    [Fact]
    public void PageContent_About_IsTheAboutViewModel()
    {
        var vm = Create();

        _navigator.Navigate(AppPage.About);

        vm.PageContent.ShouldBeSameAs(vm.About);
    }

    [Fact]
    public void MainWindow_UpdateFound_ShowsTheRailNotice()
    {
        var harness = new DashboardHarness(gateway: _gateway, localizer: _l, navigator: _navigator);
        var vm = new MainWindowViewModel(_l, _navigator, new ConnectionBannerViewModel(_l, _gateway, new FakeServiceStarter(), new FakeTimeProvider()), harness.Permissions, harness.Dashboard, harness.Processes, harness.History, harness.Settings, harness.About, harness.Updates, new ImmediateUiDispatcher());
        vm.UpdateNotice.ShouldBeNull();
        vm.HasUpdateNotice.ShouldBeFalse();

        harness.Updates.RaiseUpdateFound(new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, new Version(0, 2, 0), new Uri("https://github.com/Gustavo-Piccoli/GapActivator/releases/tag/v0.2.0")));

        vm.UpdateNotice.ShouldBe("Update 0.2.0 available");
        vm.HasUpdateNotice.ShouldBeTrue();

        _l.SetLanguage(AppLanguage.Portuguese);
        vm.UpdateNotice.ShouldBe("Atualização 0.2.0 disponível");

        vm.OpenUpdateNoticeCommand.Execute(null);
        _navigator.Current.ShouldBe(AppPage.About);
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
    public void Footer_DevelopmentService_DoesNotClaimToBeConnected()
    {
        _gateway.SetStatus(FakeServiceGateway.DevOperator);
        var vm = Create();

        vm.FooterText.ShouldBe("Not connected");
        vm.FooterGlyph.ShouldBe("");

        _gateway.SetStatus(FakeServiceGateway.Operator());

        vm.FooterText.ShouldBe("Connected");
        vm.FooterGlyph.ShouldBe("");
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
