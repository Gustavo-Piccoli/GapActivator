using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.ViewModels;
using GapActivator.App.Views;
using GapActivator.Ipc.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace GapActivator.App.Tests.Views;

public class ProcessEditorViewTests(HeadlessFixture fixture)
{
    private sealed class Rig
    {
        public Rig()
        {
            var time = new FakeTimeProvider(Dtos.Epoch);
            var zone = Zones.Fixed(TimeSpan.FromHours(-3));
            var times = new TimeDisplay(L, zone);
            Vm = new ProcessEditorViewModel(
                L,
                Gateway,
                new Permissions(L, Gateway),
                new ScriptedDialogService(),
                new FailureAcknowledgements(time),
                times,
                zone,
                () => new ScheduleEditorViewModel(L, Gateway, time, zone, times, new ImmediateUiDispatcher()),
                new FakeFilePicker());
        }

        public Localizer L { get; } = new(AppLanguage.English);

        public FakeServiceGateway Gateway { get; } = new();

        public ProcessEditorViewModel Vm { get; }
    }

    private static Window Show(ProcessEditorViewModel vm)
    {
        var window = new Window { Width = 900, Height = 900, Content = new ProcessEditorView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static T Find<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    [Fact]
    public async Task EditorView_Viewer_SaveDisabledWithTooltipAndHiddenArguments()
    {
        var rig = new Rig();
        rig.Gateway.SetStatus(FakeServiceGateway.Viewer());
        var summary = Dtos.Summary();
        rig.Gateway.Details[summary.Id] = Dtos.Details(summary) with { SensitiveHidden = true, Arguments = null };
        await rig.Vm.LoadAsync(summary.Id);

        (bool enabled, object? tip, string? arguments, bool readOnly) = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(rig.Vm);
                var save = Find<Button>(window, "SaveButton");
                var box = Find<TextBox>(window, "ArgumentsBox");
                var result = (save.IsEffectivelyEnabled, ToolTip.GetTip(save), box.Text, box.IsReadOnly);
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        enabled.ShouldBeFalse();
        tip.ShouldBe("Requires Administrators or GapActivator Operators");
        arguments.ShouldBe("Hidden");
        readOnly.ShouldBeTrue();
    }

    [Fact]
    public async Task EditorView_ServiceKind_HidesSchedulesAndDisablesTimeLimit()
    {
        var rig = new Rig();
        rig.Vm.LoadNew();

        (bool schedulesBefore, bool limitBefore, bool schedulesAfter, bool limitAfter) = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(rig.Vm);
                var before = (Find<Control>(window, "SchedulesSection").IsVisible, Find<Control>(window, "TimeLimitBox").IsEnabled);
                rig.Vm.Kind = ProcessKind.Service;
                Dispatcher.UIThread.RunJobs();
                var after = (Find<Control>(window, "SchedulesSection").IsVisible, Find<Control>(window, "TimeLimitBox").IsEnabled);
                window.Close();
                return (before.Item1, before.Item2, after.Item1, after.Item2);
            },
            TestContext.Current.CancellationToken);

        schedulesBefore.ShouldBeTrue();
        limitBefore.ShouldBeTrue();
        schedulesAfter.ShouldBeFalse();
        limitAfter.ShouldBeFalse();
    }

    [Fact]
    public async Task EditorView_PercentInExecutable_ShowsTheHint()
    {
        var rig = new Rig();
        rig.Vm.LoadNew();

        (bool before, bool after, string? text) = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(rig.Vm);
                bool hidden = Find<Control>(window, "VariableHint").IsVisible;
                rig.Vm.Executable = "C:\\%TOOLS%\\x.exe";
                Dispatcher.UIThread.RunJobs();
                var hint = Find<TextBlock>(window, "VariableHint");
                var result = (hidden, hint.IsVisible, hint.Text);
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        before.ShouldBeFalse();
        after.ShouldBeTrue();
        text.ShouldBe("Environment variables such as %VAR% are not expanded. Enter the full path.");
    }

    [Fact]
    public async Task EditorView_ErrorAndStoredSecret_AreVisibleStates()
    {
        var rig = new Rig();
        var summary = Dtos.Summary();
        rig.Gateway.Details[summary.Id] = Dtos.Details(summary) with { Environment = [new EnvironmentVariableViewDto("TOKEN", null, true)] };
        await rig.Vm.LoadAsync(summary.Id);

        (char mask, string? watermark, bool errorVisible) = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(rig.Vm);
                var box = window.GetVisualDescendants().OfType<TextBox>().First(b => b.Name == "VariableValueBox");
                var before = Find<Control>(window, "ErrorLine").IsVisible;
                var result = (box.PasswordChar, box.PlaceholderText, before);
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        mask.ShouldBe('\u2022');
        watermark.ShouldBe("•••••• (stored; type to replace)");
        errorVisible.ShouldBeFalse();
    }
}
