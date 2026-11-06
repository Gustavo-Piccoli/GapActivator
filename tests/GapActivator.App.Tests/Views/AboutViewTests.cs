using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GapActivator.App.Services;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.Updates;
using GapActivator.App.ViewModels;
using GapActivator.App.Views;
using Microsoft.Extensions.Time.Testing;

namespace GapActivator.App.Tests.Views;

public class AboutViewTests(HeadlessFixture fixture)
{
    private static readonly string[] ButtonNames = ["OpenServiceDataButton", "OpenServiceLogsButton", "OpenAppSettingsButton", "OpenAppLogsButton", "OpenRepositoryButton"];

    private static Window Show(DashboardHarness h)
    {
        var window = new Window { Width = 900, Height = 800, Content = new AboutView { DataContext = h.About } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static T Find<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    [Fact]
    public async Task AboutView_FolderButtons_NameTheirTarget()
    {
        var h = new DashboardHarness();

        var names = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                var result = ButtonNames
                    .Select(n => AutomationProperties.GetName(Find<Button>(window, n)))
                    .ToArray();
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        names.ShouldBe(
        [
            "Open the folder: Service data",
            "Open the folder: Service logs",
            "Open the folder: App settings",
            "Open the folder: App logs",
            "Open the GapActivator repository on GitHub",
        ]);
    }

    [Fact]
    public async Task AboutView_ReleaseButton_AppearsOnlyWithAnUpdate()
    {
        var h = new DashboardHarness();

        var (before, after, name) = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                var hidden = Find<Button>(window, "OpenReleaseButton").IsEffectivelyVisible;
                h.Updates.RaiseUpdateFound(new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, new Version(0, 2, 0), new Uri("https://github.com/Gustavo-Piccoli/GapActivator/releases/tag/v0.2.0")));
                Dispatcher.UIThread.RunJobs();
                var button = Find<Button>(window, "OpenReleaseButton");
                var result = (hidden, button.IsEffectivelyVisible, AutomationProperties.GetName(button));
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        before.ShouldBeFalse();
        after.ShouldBeTrue();
        name.ShouldBe("Open the release page for version 0.2.0 on GitHub");
    }

    [Fact]
    public async Task AboutView_MissingFolder_ShowsTheErrorRow()
    {
        var h = new DashboardHarness();

        var (visible, text) = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                h.About.OpenFolderCommand.Execute(Path.Combine(Path.GetTempPath(), "GapActivator.App.Tests", Guid.NewGuid().ToString("N")));
                Dispatcher.UIThread.RunJobs();
                var result = (Find<Grid>(window, "FolderErrorRow").IsEffectivelyVisible, Find<TextBlock>(window, "FolderErrorText").Text);
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        visible.ShouldBeTrue();
        text.ShouldBe("The folder does not exist yet.");
    }

    [Fact]
    public async Task MainWindow_RailNotice_NamesItsTargetAndKeepsTheFixedVisibleText()
    {
        var h = new DashboardHarness();
        var banner = new ConnectionBannerViewModel(h.L, h.Gateway, new FakeServiceStarter(), new FakeTimeProvider());
        var viewModel = new MainWindowViewModel(h.L, h.Navigator, banner, h.Permissions, h.Dashboard, h.Processes, h.History, h.Settings, h.About, h.Updates, new ImmediateUiDispatcher());

        var (hiddenBefore, name, visibleText) = await fixture.Session.Dispatch(
            () =>
            {
                var window = new MainWindow { DataContext = viewModel };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var hidden = Find<Button>(window, "UpdateNoticeButton").IsEffectivelyVisible;
                h.Updates.RaiseUpdateFound(new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, new Version(0, 2, 0), new Uri("https://github.com/Gustavo-Piccoli/GapActivator/releases/tag/v0.2.0")));
                Dispatcher.UIThread.RunJobs();
                var button = Find<Button>(window, "UpdateNoticeButton");
                var text = button.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Last();
                var result = (hidden, AutomationProperties.GetName(button), text);
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        hiddenBefore.ShouldBeFalse();
        name.ShouldBe("Update 0.2.0 available. Open About.");
        visibleText.ShouldBe("Update 0.2.0 available");
    }

    [Fact]
    public async Task AboutView_NeverChecked_ShowsNotCheckedYetAndTheAutomaticChecksNote()
    {
        var h = new DashboardHarness();
        h.AppSettings.Update(s => s with { CheckForUpdates = false });

        var (status, note) = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                var result = (Find<TextBlock>(window, "UpdateStatusText").Text, Find<TextBlock>(window, "AutomaticChecksNoteText").Text);
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        status.ShouldBe("Not checked yet.");
        note.ShouldBe("Automatic checks are off.");
    }

    [Fact]
    public async Task AboutView_LinkFailure_ShowsInTheCardOfTheButtonThatFailed()
    {
        var h = new DashboardHarness();
        h.Launcher.Failure = new InvalidOperationException("no browser");

        var (repoText, repoCard, folderCard) = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                h.About.OpenRepositoryCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                var result = (Find<TextBlock>(window, "RepositoryErrorText").Text, Find<Grid>(window, "RepositoryErrorRow").IsEffectivelyVisible, Find<Grid>(window, "FolderErrorRow").IsEffectivelyVisible);
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        repoText.ShouldBe("Could not open the page.");
        repoCard.ShouldBeTrue();
        folderCard.ShouldBeFalse();
    }
}
