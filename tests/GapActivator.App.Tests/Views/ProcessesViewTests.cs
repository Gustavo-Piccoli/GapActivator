using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.Views;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.Tests.Views;

public class ProcessesViewTests(HeadlessFixture fixture)
{
    private static async Task<DashboardHarness> WithProcessesAsync()
    {
        var h = new DashboardHarness();
        h.Gateway.Processes =
        [
            Dtos.Summary("Alpha"),
            Dtos.Summary("Bravo", lifecycle: ProcessLifecycle.Blocked),
        ];
        await h.Live.RefreshAsync();
        return h;
    }

    private static Window Show(DashboardHarness h)
    {
        var window = new Window { Width = 900, Height = 700, Content = new ProcessesView { DataContext = h.Processes } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    public async Task ProcessesView_ShowsTheStateAsWordsAndOffersTheRightButtons()
    {
        var h = await WithProcessesAsync();

        (string[] badges, string[] names, int unblocks, int disables) = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                var list = window.GetVisualDescendants().OfType<ItemsControl>().First(c => c.Name == "ProcessList");
                var texts = list.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "LifecycleBadge")
                    .Select(b => b.GetVisualDescendants().OfType<TextBlock>().First().Text ?? string.Empty).ToArray();
                var run = list.GetVisualDescendants().OfType<Button>().Where(b => b.Name == "RunNowButton")
                    .Select(b => Avalonia.Automation.AutomationProperties.GetName(b) ?? string.Empty).ToArray();
                var buttons = list.GetVisualDescendants().OfType<Button>().ToArray();
                var result = (texts, run, buttons.Count(b => b.Name == "UnblockButton" && b.IsVisible), buttons.Count(b => b.Name == "DisableButton" && b.IsVisible));
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        badges.ShouldBe(["Enabled", "Blocked"]);
        names.ShouldBe(["Run Alpha now", "Run Bravo now"]);
        unblocks.ShouldBe(1);
        disables.ShouldBe(1);
    }

    [Fact]
    public async Task ProcessesView_Viewer_ActionsAreDisabledWithTheDeniedTooltip()
    {
        var h = await WithProcessesAsync();
        h.Gateway.SetStatus(FakeServiceGateway.Viewer());

        (bool[] enabled, object?[] tips, bool showOnDisabled) = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                var buttons = window.GetVisualDescendants().OfType<Button>().Where(b => b.Name == "DeleteButton").ToArray();
                var result = (buttons.Select(b => b.IsEffectivelyEnabled).ToArray(), buttons.Select(ToolTip.GetTip).ToArray(), ToolTip.GetShowOnDisabled(buttons[0]));
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        enabled.ShouldBe([false, false]);
        tips.ShouldAllBe(t => (string?)t == "Requires Administrators or GapActivator Operators");
        showOnDisabled.ShouldBeTrue();
    }

    [Fact]
    public async Task ProcessesView_Disconnected_SaysTheListIsTheLastKnownState()
    {
        var h = await WithProcessesAsync();
        h.Gateway.SetStatus(FakeServiceGateway.Stopped);

        string[] texts = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                var result = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? string.Empty).ToArray();
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        texts.ShouldContain("Not connected: showing the last known state.");
        texts.ShouldContain("Alpha");
    }

    [Fact]
    public async Task ProcessesView_NoProcesses_SaysSoOnlyAfterTheServiceReported()
    {
        var h = new DashboardHarness();

        string[] before = await fixture.Session.Dispatch(() => VisibleTexts(h), TestContext.Current.CancellationToken);
        await h.Live.RefreshAsync();
        string[] after = await fixture.Session.Dispatch(() => VisibleTexts(h), TestContext.Current.CancellationToken);

        before.ShouldNotContain("No processes are registered yet.");
        before.ShouldContain("Waiting for the service to report its state…");
        after.ShouldContain("No processes are registered yet.");
    }

    private static string[] VisibleTexts(DashboardHarness h)
    {
        var window = Show(h);
        var result = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? string.Empty).ToArray();
        window.Close();
        return result;
    }
}
