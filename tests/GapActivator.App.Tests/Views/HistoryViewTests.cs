using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.Views;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.Tests.Views;

public class HistoryViewTests(HeadlessFixture fixture)
{
    private static Window Show(DashboardHarness h, double width = 900)
    {
        var window = new Window { Width = width, Height = 700, Content = new HistoryView { DataContext = h.History } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static string[] VisibleTexts(Window window) =>
        [.. window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? string.Empty)];

    [Fact]
    public async Task HistoryView_BeforeTheFirstPage_SaysLoadingAndNotNoRuns()
    {
        var h = new DashboardHarness();

        string[] texts = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                var visible = VisibleTexts(window);
                window.Close();
                return visible;
            },
            TestContext.Current.CancellationToken);

        texts.ShouldContain("Loading runs…");
        texts.ShouldNotContain("No runs yet.");
    }

    [Fact]
    public async Task HistoryView_NoRuns_SaysSo()
    {
        var h = new DashboardHarness();
        await h.History.ApplyCommand.ExecuteAsync(null);

        string[] texts = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                var visible = VisibleTexts(window);
                window.Close();
                return visible;
            },
            TestContext.Current.CancellationToken);

        texts.ShouldContain("No runs yet.");
        texts.ShouldNotContain("Loading runs…");
    }

    [Fact]
    public async Task HistoryView_Runs_ShowEachStatusAsWordsAndOpenOnClick()
    {
        var h = new DashboardHarness();
        var failed = Dtos.RunRow("Alpha", outcome: RunOutcome.Failed, exitCode: 2);
        h.Gateway.RunPages = _ => new RunRowPageDto([failed, Dtos.RunRow("Beta")], 2);
        h.Gateway.Runs[failed.Id] = Dtos.RunDetails(failed, error: "boom", output: "out");
        await h.History.ApplyCommand.ExecuteAsync(null);

        (string[] texts, string[] names, string[] after) = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                var shown = VisibleTexts(window);
                var buttons = window.GetVisualDescendants().OfType<Button>().Where(b => b.Name == "OpenRunButton").ToArray();
                var accessible = buttons.Select(b => Avalonia.Automation.AutomationProperties.GetName(b) ?? string.Empty).ToArray();
                buttons[0].Command!.Execute(buttons[0].CommandParameter);
                Dispatcher.UIThread.RunJobs();
                var detail = VisibleTexts(window);
                window.Close();
                return (shown, accessible, detail);
            },
            TestContext.Current.CancellationToken);

        texts.ShouldContain("Failed");
        texts.ShouldContain("Succeeded");
        names.Length.ShouldBe(2);
        names[0].ShouldStartWith("Alpha, Failed,");
        after.ShouldContain("Error");
    }

    [Fact]
    public async Task HistoryView_Disconnected_SaysTheRunsAreTheLastKnownState()
    {
        var h = new DashboardHarness();
        h.Gateway.RunPages = _ => new RunRowPageDto([Dtos.RunRow("Alpha")], 1);
        await h.History.ApplyCommand.ExecuteAsync(null);
        h.Gateway.SetStatus(FakeServiceGateway.Stopped);

        string[] texts = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                var visible = VisibleTexts(window);
                window.Close();
                return visible;
            },
            TestContext.Current.CancellationToken);

        texts.ShouldContain("Not connected: showing the last known state.");
        texts.ShouldContain("Alpha");
    }

    [Fact]
    public async Task HistoryView_DetailWithTailAndReadoptionNote_ShowsBothAndGoesBack()
    {
        const string note = "[GapActivator] The service restarted and re-adopted this process at 2030-01-01T12:00:00Z; later output is not captured.";
        var h = new DashboardHarness();
        var run = Dtos.RunRow("Alpha", state: RunState.Running, outcome: null, exitCode: null);
        h.Gateway.Runs[run.Id] = Dtos.RunDetails(run, output: "first line\n" + note + "\n", truncated: true);

        (string[] texts, bool backVisible, bool listVisibleAfterBack) = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                h.Navigator.OpenRun(run.Id);
                Dispatcher.UIThread.RunJobs();
                var shown = VisibleTexts(window);
                var back = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "BackButton");
                var visible = back.IsEffectivelyVisible;
                back.Command!.Execute(null);
                Dispatcher.UIThread.RunJobs();
                var list = window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "ListHost");
                var result = (shown, visible, list.IsEffectivelyVisible);
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        texts.ShouldContain(note);
        texts.ShouldContain("Only the last 1 MiB of output is shown.");
        backVisible.ShouldBeTrue();
        listVisibleAfterBack.ShouldBeTrue();
        h.History.Detail.ShouldBeNull();
    }

    [Fact]
    public async Task HistoryView_LargeOutput_RealizesFarFewerContainersThanLines()
    {
        var h = new DashboardHarness();
        var run = Dtos.RunRow("Alpha");
        var text = string.Join("\n", Enumerable.Range(0, 15000).Select(i => $"line {i}"));
        h.Gateway.Runs[run.Id] = Dtos.RunDetails(run, output: text);

        (int lines, int containers, double offset, double extent, double viewport) = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                h.Navigator.OpenRun(run.Id);
                Dispatcher.UIThread.RunJobs();
                Dispatcher.UIThread.RunJobs();
                var list = window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "OutputList");
                var scroller = list.GetVisualDescendants().OfType<ScrollViewer>().First();
                var result = (h.History.Detail!.OutputLines.Count, list.GetVisualDescendants().OfType<ListBoxItem>().Count(), scroller.Offset.Y, scroller.Extent.Height, scroller.Viewport.Height);
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        lines.ShouldBe(15000);
        containers.ShouldBeGreaterThan(0);
        containers.ShouldBeLessThan(200);

        // Not scrolled yet: the list follows the tail, so the last page of lines is in view.
        (offset + viewport).ShouldBeGreaterThanOrEqualTo(extent - 1);
    }
}
