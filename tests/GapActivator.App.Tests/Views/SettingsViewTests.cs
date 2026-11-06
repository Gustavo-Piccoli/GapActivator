using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GapActivator.App.Settings;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.Views;

namespace GapActivator.App.Tests.Views;

public class SettingsViewTests(HeadlessFixture fixture)
{
    private static Window Show(DashboardHarness h, double width = 900)
    {
        var window = new Window { Width = width, Height = 800, Content = new SettingsView { DataContext = h.Settings } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static T Find<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    private static string[] VisibleTexts(Window window) =>
        [.. window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? string.Empty)];

    [Fact]
    public async Task SettingsView_BeforeTheFirstRead_HidesTheServiceCardsAndKeepsTheAppOptions()
    {
        var h = new DashboardHarness();
        h.Gateway.Hold(nameof(GapActivator.App.Services.IServiceGateway.GetSettingsAsync));

        var (service, app, loading) = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                var result = (Find<StackPanel>(window, "ServiceSections").IsEffectivelyVisible, Find<Border>(window, "AppCard").IsEffectivelyVisible, VisibleTexts(window));
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        service.ShouldBeFalse();
        app.ShouldBeTrue();
        loading.ShouldContain("Loading settings…");
    }

    [Fact]
    public async Task SettingsView_AfterTheRead_ShowsTheValuesAndTheSelectedOptions()
    {
        var h = new DashboardHarness();
        h.AppSettings.Update(s => s with { Theme = AppTheme.Dark });
        await h.Settings.LoadCommand.ExecuteAsync(null);

        var (theme, cycle, host, authorization) = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                var result = (
                    Find<ComboBox>(window, "ThemeBox").SelectedItem,
                    Find<NumericUpDown>(window, "CycleBox").Value,
                    Find<TextBox>(window, "HostBox").Text,
                    Find<Border>(window, "AuthorizationCard").IsEffectivelyVisible);
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        theme.ShouldBeSameAs(h.Settings.ThemeChoice);
        cycle.ShouldBe(30m);
        host.ShouldBe("smtp.example.com");
        authorization.ShouldBeTrue();
    }

    [Fact]
    public async Task SettingsView_Viewer_ServiceFieldsAreReadOnlyAndAppFieldsAreNot()
    {
        var h = new DashboardHarness();
        h.Gateway.SetStatus(FakeServiceGateway.Viewer());
        await h.Settings.LoadCommand.ExecuteAsync(null);

        var (hostReadOnly, cycleReadOnly, save, restart, themeEnabled, reason) = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                var result = (
                    Find<TextBox>(window, "HostBox").IsReadOnly,
                    Find<NumericUpDown>(window, "CycleBox").IsReadOnly,
                    Find<Button>(window, "SaveButton").IsEffectivelyEnabled,
                    Find<Button>(window, "RestartServiceButton").IsEffectivelyEnabled,
                    Find<ComboBox>(window, "ThemeBox").IsEffectivelyEnabled,
                    Find<Grid>(window, "ReadOnlyReason").IsEffectivelyVisible);
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        hostReadOnly.ShouldBeTrue();
        cycleReadOnly.ShouldBeTrue();
        save.ShouldBeFalse();
        restart.ShouldBeFalse();
        themeEnabled.ShouldBeTrue();
        reason.ShouldBeTrue();
    }

    [Fact]
    public async Task SettingsView_FailedRead_SaysWhyInsteadOfShowingAnEmptyForm()
    {
        var h = new DashboardHarness();
        h.Gateway.FailNext(
            nameof(GapActivator.App.Services.IServiceGateway.GetSettingsAsync),
            new GapActivator.Ipc.GapActivatorRequestException(GapActivator.Ipc.IpcErrorCodes.Disconnected, "down", retryable: true));

        var (service, error) = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                var result = (Find<StackPanel>(window, "ServiceSections").IsEffectivelyVisible, VisibleTexts(window));
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        service.ShouldBeFalse();
        error.ShouldContain("Not connected to the service.");
    }
}
