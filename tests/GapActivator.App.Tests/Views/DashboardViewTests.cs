using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GapActivator.App.Localization;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.Views;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.Tests.Views;

public class DashboardViewTests(HeadlessFixture fixture)
{
    private static async Task<DashboardHarness> WithTwoActiveRunsAsync()
    {
        var h = new DashboardHarness();
        h.Gateway.Dashboard = Dtos.Dashboard(activeRuns:
        [
            Dtos.RunRow("First", state: RunState.Running, outcome: null),
            Dtos.RunRow("Second", state: RunState.Launching, outcome: null),
        ]);
        await h.Live.RefreshAsync();
        return h;
    }

    private static Window Show(DashboardHarness h)
    {
        var window = new Window { Width = 900, Height = 700, Content = new DashboardView { DataContext = h.Dashboard } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    public async Task DashboardView_TwoActiveRuns_ShowsTwoRowsWithCancel()
    {
        var h = await WithTwoActiveRunsAsync();

        (int cancels, string[] names) = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                var list = window.GetVisualDescendants().OfType<ItemsControl>().First(c => c.Name == "ActiveRuns");
                var buttons = list.GetVisualDescendants().OfType<Button>().Where(b => b.Name == "CancelRun").ToArray();
                var result = (buttons.Length,
                    buttons.Select(b => Avalonia.Automation.AutomationProperties.GetName(b) ?? string.Empty).ToArray());
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        cancels.ShouldBe(2);
        names.ShouldBe(["Cancel First", "Cancel Second"]);
    }

    [Fact]
    public async Task DashboardView_Viewer_CancelIsDisabledWithTheDeniedTooltip()
    {
        var h = await WithTwoActiveRunsAsync();
        h.Gateway.SetStatus(FakeServiceGateway.Viewer());

        (bool[] enabled, object?[] tips, bool showOnDisabled, bool pauseEnabled) = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                var buttons = window.GetVisualDescendants().OfType<Button>().Where(b => b.Name == "CancelRun").ToArray();
                var pause = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "PauseButton");
                var result = (buttons.Select(b => b.IsEffectivelyEnabled).ToArray(), buttons.Select(ToolTip.GetTip).ToArray(), ToolTip.GetShowOnDisabled(buttons[0]), pause.IsEffectivelyEnabled);
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        enabled.ShouldBe([false, false]);
        tips.ShouldBe(["Requires Administrators or GapActivator Operators", "Requires Administrators or GapActivator Operators"]);
        showOnDisabled.ShouldBeTrue();
        pauseEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task DashboardView_Operator_CancelIsEnabledWithoutATooltip()
    {
        var h = await WithTwoActiveRunsAsync();

        (bool enabled, object? tip) = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                var button = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "CancelRun");
                var result = (button.IsEffectivelyEnabled, ToolTip.GetTip(button));
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        enabled.ShouldBeTrue();
        tip.ShouldBeNull();
    }

    [Fact]
    public async Task DashboardView_SwitchToPortuguese_UpdatesTheHeadingLive()
    {
        var h = new DashboardHarness();
        await h.Live.RefreshAsync();

        (string before, string after) = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                var heading = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "ActiveRunsHeading");
                var english = heading.Text ?? string.Empty;
                h.L.SetLanguage(AppLanguage.Portuguese);
                var portuguese = heading.Text ?? string.Empty;
                window.Close();
                return (english, portuguese);
            },
            TestContext.Current.CancellationToken);

        before.ShouldBe("Active runs");
        after.ShouldBe("Execuções ativas");
    }

    [Fact]
    public async Task DashboardView_Disconnected_SaysTheListsAreTheLastKnownState()
    {
        var h = await WithTwoActiveRunsAsync();
        h.Gateway.SetStatus(FakeServiceGateway.Stopped);

        string[] texts = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                var visible = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? string.Empty).ToArray();
                window.Close();
                return visible;
            },
            TestContext.Current.CancellationToken);

        texts.ShouldContain("Not connected: showing the last known state.");
        texts.ShouldNotContain("Waiting for the service to report its state…");
    }

    [Fact]
    public async Task DashboardView_NothingToShow_SaysSoInEachSection()
    {
        var h = new DashboardHarness();
        await h.Live.RefreshAsync();

        string[] texts = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                var visible = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? string.Empty).ToArray();
                window.Close();
                return visible;
            },
            TestContext.Current.CancellationToken);

        texts.ShouldContain("No runs are active.");
        texts.ShouldContain("No runs are scheduled.");
        texts.ShouldContain("No recent failures.");
    }
}
