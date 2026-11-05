using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Settings;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.ViewModels;
using GapActivator.App.Views;
using Microsoft.Extensions.Time.Testing;

namespace GapActivator.App.Tests.Views;

public class MainWindowServiceTests(HeadlessFixture fixture)
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static MainWindow NewWindow(FakeServiceGateway gateway)
    {
        var l = new Localizer(AppLanguage.English);
        var banner = new ConnectionBannerViewModel(l, gateway, new FakeServiceStarter(), new FakeTimeProvider());
        var harness = new DashboardHarness(gateway: gateway, localizer: l);
        var viewModel = new MainWindowViewModel(l, harness.Navigator, banner, harness.Permissions, harness.Dashboard, harness.Processes, harness.History);
        return new MainWindow { DataContext = viewModel };
    }

    private static AppSettingsService NewSettings(TempDirectory dir) =>
        new(new AppSettingsStore(Path.Combine(dir.Path, "app-settings.json"), new ListLogger<AppSettingsStore>(), English), AppSettings.Defaults(English));

    [Fact]
    public async Task MainWindowService_UserClose_HidesAndSavesTheBounds()
    {
        using var dir = new TempDirectory();
        var settings = NewSettings(dir);
        var service = new MainWindowService(
            () => NewWindow(new FakeServiceGateway()),
            settings,
            () => [new PixelRect(0, 0, 1920, 1080)]);

        (bool visibleAfterClose, bool visibleBefore) = await fixture.Session.Dispatch(
            () =>
            {
                service.Show();
                var window = service.Window.ShouldNotBeNull();
                var before = window.IsVisible;
                window.Close();
                return (window.IsVisible, before);
            },
            TestContext.Current.CancellationToken);

        visibleBefore.ShouldBeTrue();
        visibleAfterClose.ShouldBeFalse();
        WindowBounds saved = settings.Current.Window.ShouldNotBeNull();
        saved.Width.ShouldBeGreaterThanOrEqualTo(640);
        saved.Height.ShouldBeGreaterThanOrEqualTo(480);
    }

    [Fact]
    public async Task MainWindowService_ShowAfterHide_ShowsTheSameWindow()
    {
        using var dir = new TempDirectory();
        var service = new MainWindowService(() => NewWindow(new FakeServiceGateway()), NewSettings(dir), () => []);

        (MainWindow first, MainWindow second, bool visible) = await fixture.Session.Dispatch(
            () =>
            {
                service.Show();
                var one = service.Window.ShouldNotBeNull();
                one.Close();
                service.Show();
                return (one, service.Window.ShouldNotBeNull(), one.IsVisible);
            },
            TestContext.Current.CancellationToken);

        second.ShouldBeSameAs(first);
        visible.ShouldBeTrue();
    }

    [Fact]
    public async Task MainWindow_RailItems_ExposeTheLocalizedLabelAsAutomationName()
    {
        (string[] english, string[] portuguese) = await fixture.Session.Dispatch(
            () =>
            {
                var window = NewWindow(new FakeServiceGateway());
                var vm = (MainWindowViewModel)window.DataContext!;
                window.Show();
                string[] Names() => [.. window.GetVisualDescendants().OfType<ListBoxItem>().Select(i => AutomationProperties.GetName(i) ?? string.Empty)];
                var en = Names();
                vm.L.SetLanguage(AppLanguage.Portuguese);
                var pt = Names();
                window.Close();
                return (en, pt);
            },
            TestContext.Current.CancellationToken);

        english.ShouldBe(["Dashboard", "Processes", "History", "Settings", "About"]);
        portuguese.ShouldBe(["Painel", "Processos", "Histórico", "Configurações", "Sobre"]);
    }

    [Fact]
    public async Task MainWindow_DevelopmentStrip_WrapsItsTextAtTheMinimumWidth()
    {
        var gateway = new FakeServiceGateway();
        gateway.SetStatus(FakeServiceGateway.DevOperator);

        (double textWidth, double available, double height) = await fixture.Session.Dispatch(
            () =>
            {
                var window = NewWindow(gateway);
                window.Width = 640;
                window.Show();
                var strip = window.FindControl<Border>("DevelopmentStrip")!;
                var text = strip.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("warning") && !t.Classes.Contains("railIcon"));
                var result = (text.Bounds.Width, strip.Bounds.Width, text.Bounds.Height);
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        textWidth.ShouldBeLessThan(available);
        height.ShouldBeGreaterThan(20);
    }

    [Fact]
    public async Task MainWindow_ConnectionStates_ShowTheBannerAndTheDevelopmentStrip()
    {
        var gateway = new FakeServiceGateway();
        gateway.SetStatus(FakeServiceGateway.Stopped);

        (bool bannerVisible, bool devVisible) = await fixture.Session.Dispatch(
            () =>
            {
                var window = NewWindow(gateway);
                var vm = (MainWindowViewModel)window.DataContext!;
                window.Show();
                var stopped = vm.Banner.IsVisible;
                gateway.SetStatus(FakeServiceGateway.DevOperator);
                var dev = vm.Banner.IsDevelopmentMode;
                window.Close();
                return (stopped, dev);
            },
            TestContext.Current.CancellationToken);

        bannerVisible.ShouldBeTrue();
        devVisible.ShouldBeTrue();
    }
}
