using Avalonia;
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

    [Fact]
    public async Task ProcessesView_LongNameAtMinimumWidth_KeepsTheBadgeInsideTheRow()
    {
        var h = new DashboardHarness();
        h.Gateway.Processes = [Dtos.Summary(new string('W', 120), lifecycle: ProcessLifecycle.Blocked, lastOutcome: RunOutcome.Crashed, nextScheduledAt: Dtos.Epoch.AddDays(1))];
        await h.Live.RefreshAsync();
        h.L.SetLanguage(GapActivator.App.Localization.AppLanguage.Portuguese);

        (double badgeRight, double badgeWidth, double rowWidth, double nameWidth, object? nameTip, double bottom, double listBottom) = await fixture.Session.Dispatch(
            () =>
            {
                // The main window's minimum width (640) less the rail (200) and the page margins (56).
                var window = new Window { Width = 384, Height = 700, Content = new ProcessesView { DataContext = h.Processes } };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var list = window.GetVisualDescendants().OfType<ItemsControl>().First(c => c.Name == "ProcessList");
                var badge = window.GetVisualDescendants().OfType<Border>().First(b => b.Name == "LifecycleBadge");
                var name = list.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text is { Length: 120 });
                var right = badge.TranslatePoint(new Point(badge.Bounds.Width, 0), list)!.Value.X;
                var meta = list.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("caption") && t.IsVisible).ToArray();
                var lowest = meta.Max(t => t.TranslatePoint(new Point(0, t.Bounds.Height), list)!.Value.Y);
                var result = (right, badge.Bounds.Width, list.Bounds.Width, name.Bounds.Width, ToolTip.GetTip(name), lowest, list.Bounds.Height);
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        badgeWidth.ShouldBeGreaterThan(0);
        badgeRight.ShouldBeLessThanOrEqualTo(rowWidth);
        nameWidth.ShouldBeLessThan(rowWidth);
        nameTip.ShouldBe(new string('W', 120));
        bottom.ShouldBeLessThanOrEqualTo(listBottom);
    }

    private static string[] VisibleTexts(DashboardHarness h)
    {
        var window = Show(h);
        var result = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? string.Empty).ToArray();
        window.Close();
        return result;
    }

    [Fact]
    public async Task ProcessesView_DeleteWaitingForTheRunToStop_ShowsTheProgressLineThroughFreshnessNotes()
    {
        var h = new DashboardHarness();
        var process = Dtos.Summary("Busy", activeRun: new ActiveRunViewDto(Guid.NewGuid(), RunState.Running, RunTrigger.Manual, Dtos.Epoch, 1));
        h.Gateway.Processes = [process];
        h.Gateway.Details[process.Id] = Dtos.Details(process);
        await h.Live.RefreshAsync();
        h.Dialogs.Enqueue(GapActivator.App.Services.DialogChoice.Primary);

        string[] texts = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                _ = h.Processes.Items.Single().DeleteCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();
                var visible = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? string.Empty).ToArray();
                var inNotes = window.GetVisualDescendants().OfType<FreshnessNotes>().Single().GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "ProgressLine" && t.IsEffectivelyVisible);
                h.Gateway.Details[process.Id] = Dtos.Details(process with { ActiveRun = null });
                h.Time.Advance(GapActivator.App.Services.ProcessActions.DeletePollInterval);
                window.Close();
                return inNotes ? visible : [];
            },
            TestContext.Current.CancellationToken);

        texts.ShouldContain("Stopping the run before deleting \"Busy\"…");
    }
}
