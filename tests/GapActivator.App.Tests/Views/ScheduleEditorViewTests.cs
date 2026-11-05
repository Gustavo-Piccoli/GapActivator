using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GapActivator.App.Localization;
using GapActivator.App.Scheduling;
using GapActivator.App.Services;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.ViewModels;
using GapActivator.App.Views;
using GapActivator.Ipc;
using Microsoft.Extensions.Time.Testing;

namespace GapActivator.App.Tests.Views;

public class ScheduleEditorViewTests(HeadlessFixture fixture)
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(300);

    private sealed class Rig
    {
        public Rig()
        {
            Gateway.Preview = (_, anchor, count) => [.. Enumerable.Range(1, count).Select(i => anchor.AddDays(i))];
            Vm = new ScheduleEditorViewModel(L, Gateway, Time, Zone, new TimeDisplay(L, Zone), new ImmediateUiDispatcher());
        }

        public Localizer L { get; } = new(AppLanguage.English);

        public FakeServiceGateway Gateway { get; } = new();

        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2030, 1, 7, 12, 0, 0, TimeSpan.Zero));

        public TimeZoneInfo Zone { get; } = Zones.Fixed(TimeSpan.FromHours(-3));

        public ScheduleEditorViewModel Vm { get; }
    }

    private static Window Show(ScheduleEditorViewModel vm)
    {
        var window = new Window { Width = 560, Height = 760, Content = new ScheduleEditorView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static T Find<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    [Fact]
    public async Task ScheduleEditorView_Weekly_ShowsWeekdayTogglesAndTime()
    {
        var rig = new Rig();

        (bool weekdays, bool time, bool interval, int toggles) = await fixture.Session.Dispatch(
            () =>
            {
                rig.Vm.Load(null);
                rig.Vm.Mode = ScheduleMode.Weekly;
                var window = Show(rig.Vm);
                var result = (
                    Find<Control>(window, "WeekdaysPanel").IsVisible,
                    Find<Control>(window, "TimePanel").IsVisible,
                    Find<Control>(window, "IntervalPanel").IsVisible,
                    Find<Control>(window, "WeekdaysPanel").GetVisualDescendants().OfType<ToggleButton>().Count());
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        weekdays.ShouldBeTrue();
        time.ShouldBeTrue();
        interval.ShouldBeFalse();
        toggles.ShouldBe(7);
    }

    [Fact]
    public async Task ScheduleEditorView_Preview_ListsFiveTimes()
    {
        var rig = new Rig();

        (int items, bool listVisible, bool loading) = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(rig.Vm);
                rig.Vm.Load(null);
                rig.Time.Advance(Delay);
                Dispatcher.UIThread.RunJobs();
                var list = Find<ItemsControl>(window, "PreviewList");
                var result = (list.ItemCount, list.IsVisible, Find<Control>(window, "PreviewLoading").IsVisible);
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        items.ShouldBe(5);
        listVisible.ShouldBeTrue();
        loading.ShouldBeFalse();
    }

    [Fact]
    public async Task ScheduleEditorView_PreviewError_ShowsTheTextAndHidesTheList()
    {
        var rig = new Rig();
        rig.Gateway.FailNext(nameof(IServiceGateway.PreviewScheduleAsync), new GapActivatorRequestException(IpcErrorCodes.Invalid, "bad rule", retryable: false));

        (string? text, bool errorVisible, bool listVisible) = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(rig.Vm);
                rig.Vm.Load(null);
                rig.Time.Advance(Delay);
                Dispatcher.UIThread.RunJobs();
                var error = Find<TextBlock>(window, "PreviewErrorText");
                var result = (error.Text, error.IsVisible, Find<ItemsControl>(window, "PreviewList").IsVisible);
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        text.ShouldBe("The service rejected the input: bad rule");
        errorVisible.ShouldBeTrue();
        listVisible.ShouldBeFalse();
    }

    [Fact]
    public async Task ScheduleEditorView_EmptyAndLoading_AreVisibleStates()
    {
        var rig = new Rig();
        rig.Gateway.Preview = (_, _, _) => [];

        (bool loadingFirst, bool emptyFirst, bool loadingAfter, bool emptyAfter) = await fixture.Session.Dispatch(
            () =>
            {
                var window = Show(rig.Vm);
                rig.Vm.Load(null);
                Dispatcher.UIThread.RunJobs();
                bool loading = Find<Control>(window, "PreviewLoading").IsVisible;
                bool empty = Find<Control>(window, "PreviewEmpty").IsVisible;
                rig.Time.Advance(Delay);
                Dispatcher.UIThread.RunJobs();
                var result = (loading, empty, Find<Control>(window, "PreviewLoading").IsVisible, Find<Control>(window, "PreviewEmpty").IsVisible);
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        loadingFirst.ShouldBeTrue();
        emptyFirst.ShouldBeFalse();
        loadingAfter.ShouldBeFalse();
        emptyAfter.ShouldBeTrue();
    }

    [Fact]
    public async Task ScheduleEditorView_Monthly_ShowsTheHintAndTheDayBox()
    {
        var rig = new Rig();

        (bool dayVisible, bool hintVisible, string? hint) = await fixture.Session.Dispatch(
            () =>
            {
                rig.Vm.Load(null);
                rig.Vm.Mode = ScheduleMode.Monthly;
                var window = Show(rig.Vm);
                var hintBlock = Find<TextBlock>(window, "MonthDayHint");
                var result = (Find<Control>(window, "MonthDayPanel").IsVisible, hintBlock.IsVisible, hintBlock.Text);
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        dayVisible.ShouldBeTrue();
        hintVisible.ShouldBeTrue();
        hint.ShouldBe("Months without this day are skipped.");
    }

    [Fact]
    public async Task ScheduleEditorView_Weekly_NoDay_ShowsTheValidationText()
    {
        var rig = new Rig();

        (string? text, bool visible) = await fixture.Session.Dispatch(
            () =>
            {
                rig.Vm.Load(null);
                rig.Vm.Mode = ScheduleMode.Weekly;
                var window = Show(rig.Vm);
                var block = Find<TextBlock>(window, "ValidationMessage");
                var result = (block.Text, block.IsVisible);
                window.Close();
                return result;
            },
            TestContext.Current.CancellationToken);

        text.ShouldBe("Choose at least one weekday.");
        visible.ShouldBeTrue();
    }
}
