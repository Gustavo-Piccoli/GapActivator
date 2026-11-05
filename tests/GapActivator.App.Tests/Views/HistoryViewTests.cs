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
        texts.ShouldNotContain("No runs match the filters.");
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

        texts.ShouldContain("No runs match the filters.");
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

        (string output, string[] texts, bool backVisible, bool listVisibleAfterBack) = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(h);
                h.Navigator.OpenRun(run.Id);
                Dispatcher.UIThread.RunJobs();
                var box = window.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "OutputBox");
                var shown = VisibleTexts(window);
                var back = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "BackButton");
                var visible = back.IsEffectivelyVisible;
                var text = box.Text ?? string.Empty;
                back.Command!.Execute(null);
                Dispatcher.UIThread.RunJobs();
                var list = window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "ListHost");
                var result = (text, shown, visible, list.IsEffectivelyVisible);
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        output.ShouldContain(note);
        texts.ShouldContain("Only the last 1 MiB of output is shown.");
        backVisible.ShouldBeTrue();
        listVisibleAfterBack.ShouldBeTrue();
        h.History.Detail.ShouldBeNull();
    }
}
