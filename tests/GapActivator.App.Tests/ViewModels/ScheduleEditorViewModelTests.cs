using GapActivator.App.Localization;
using GapActivator.App.Scheduling;
using GapActivator.App.Services;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.ViewModels;
using GapActivator.Ipc;
using GapActivator.Ipc.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace GapActivator.App.Tests.ViewModels;

public sealed class ScheduleEditorViewModelTests : IAsyncDisposable
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(300);

    private readonly FakeServiceGateway _gateway = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2030, 1, 7, 12, 0, 0, TimeSpan.Zero));
    private readonly Localizer _l = new(AppLanguage.English);
    private readonly TimeZoneInfo _zone = Zones.Fixed(TimeSpan.FromHours(-3));
    private readonly TimeDisplay _times;
    private readonly ScheduleEditorViewModel _vm;

    public ScheduleEditorViewModelTests()
    {
        _times = new TimeDisplay(_l, _zone);
        _gateway.Preview = Occurrences;
        _vm = new ScheduleEditorViewModel(_l, _gateway, _time, _zone, _times, new ImmediateUiDispatcher());
    }

    public ValueTask DisposeAsync()
    {
        _vm.Dispose();
        return _gateway.DisposeAsync();
    }

    private static IReadOnlyList<DateTimeOffset> Occurrences(string rule, DateTimeOffset anchor, int count) =>
        [.. Enumerable.Range(1, count).Select(i => anchor.AddDays(i))];

    private int PreviewCalls() => _gateway.Calls.Count(c => c.Method == nameof(IServiceGateway.PreviewScheduleAsync));

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        condition().ShouldBeTrue();
    }

    [Fact]
    public void Load_NewSchedule_IsDailyAtTheNextFullHour()
    {
        _vm.Load(null);

        _vm.Mode.ShouldBe(ScheduleMode.Daily);
        _vm.Time.ShouldBe(TimeSpan.FromHours(10)); // 12:00 UTC is 09:00 at -03:00
        _vm.IsTimeMode.ShouldBeTrue();
        _vm.IsValid.ShouldBeTrue();
        _vm.Build(null).Rule.ShouldBe("FREQ=DAILY;BYHOUR=10;BYMINUTE=0");
    }

    [Fact]
    public void Load_AdvancedRule_StartsInAdvancedModeWithTheRawText()
    {
        var view = new ScheduleViewDto(Guid.NewGuid(), ScheduleInputKind.Recurring, new DateTimeOffset(2030, 1, 9, 12, 0, 0, TimeSpan.Zero), "FREQ=DAILY;COUNT=3;BYHOUR=8;BYMINUTE=0", null, null);

        _vm.Load(view);

        _vm.Mode.ShouldBe(ScheduleMode.Advanced);
        _vm.IsAdvanced.ShouldBeTrue();
        _vm.RawRule.ShouldBe("FREQ=DAILY;COUNT=3;BYHOUR=8;BYMINUTE=0");
        _vm.Build(view.Id).ShouldBe(new ScheduleInputDto(view.Id, ScheduleInputKind.Recurring, view.At, view.Rule));
    }

    [Fact]
    public void Load_FriendlyWeekly_FillsTheDaysAndTime()
    {
        var view = new ScheduleViewDto(Guid.NewGuid(), ScheduleInputKind.Recurring, new DateTimeOffset(2030, 1, 7, 11, 0, 0, TimeSpan.Zero), "FREQ=WEEKLY;BYDAY=MO,FR;BYHOUR=8;BYMINUTE=5", null, null);

        _vm.Load(view);

        _vm.Mode.ShouldBe(ScheduleMode.Weekly);
        (_vm.Monday, _vm.Tuesday, _vm.Friday, _vm.Sunday).ShouldBe((true, false, true, false));
        _vm.Time.ShouldBe(new TimeSpan(8, 5, 0));
        _vm.IsWeekly.ShouldBeTrue();
    }

    [Fact]
    public void Preview_ChangeThenWait300ms_CallsPreviewOnceWithFive()
    {
        _vm.Load(null);
        _time.Advance(Delay);

        PreviewCalls().ShouldBe(1);
        var call = _gateway.Calls.Single(c => c.Method == nameof(IServiceGateway.PreviewScheduleAsync));
        call.Args[0].ShouldBe("FREQ=DAILY;BYHOUR=10;BYMINUTE=0");
        call.Args[1].ShouldBe(new DateTimeOffset(2030, 1, 7, 10, 0, 0, TimeSpan.FromHours(-3)));
        call.Args[2].ShouldBe(5);
        _vm.Preview.Count.ShouldBe(ScheduleEditorViewModel.PreviewCount);
        _vm.Preview[0].ShouldBe(_times.WithWeekday(new DateTimeOffset(2030, 1, 8, 10, 0, 0, TimeSpan.FromHours(-3))));
        _vm.IsPreviewLoading.ShouldBeFalse();
        _vm.PreviewError.ShouldBeNull();
    }

    [Fact]
    public void Preview_ChangesWithin300ms_AreDebouncedIntoOneCall()
    {
        _vm.Load(null);
        _time.Advance(TimeSpan.FromMilliseconds(200));
        _vm.Time = TimeSpan.FromHours(11);
        _time.Advance(TimeSpan.FromMilliseconds(200));
        _vm.Time = TimeSpan.FromHours(12);
        _time.Advance(TimeSpan.FromMilliseconds(299));

        PreviewCalls().ShouldBe(0);

        _time.Advance(TimeSpan.FromMilliseconds(1));

        PreviewCalls().ShouldBe(1);
        _gateway.Calls.Single(c => c.Method == nameof(IServiceGateway.PreviewScheduleAsync)).Args[0]
            .ShouldBe("FREQ=DAILY;BYHOUR=12;BYMINUTE=0");
    }

    [Fact]
    public void Preview_WhileWaiting_ShowsTheLoadingState()
    {
        _vm.Load(null);

        _vm.IsPreviewLoading.ShouldBeTrue();
        _vm.ShowPreviewNone.ShouldBeFalse();
    }

    [Fact]
    public async Task Preview_StaleResponse_IsIgnored()
    {
        _vm.Load(null);
        TaskCompletionSource first = _gateway.Hold(nameof(IServiceGateway.PreviewScheduleAsync));
        _time.Advance(Delay);
        Task firstRefresh = _vm.PreviewRefresh;
        TaskCompletionSource second = _gateway.Hold(nameof(IServiceGateway.PreviewScheduleAsync));
        _vm.Time = TimeSpan.FromHours(13);
        _time.Advance(Delay);
        Task secondRefresh = _vm.PreviewRefresh;
        PreviewCalls().ShouldBe(2);

        second.SetResult();
        await secondRefresh;
        _vm.Preview.Count.ShouldBe(5);
        string latest = _vm.Preview[0];
        first.SetResult();
        await firstRefresh;

        latest.ShouldBe(_times.WithWeekday(new DateTimeOffset(2030, 1, 8, 13, 0, 0, TimeSpan.FromHours(-3))));
        _vm.Preview[0].ShouldBe(latest);
        _vm.IsPreviewLoading.ShouldBeFalse();
    }

    [Fact]
    public void Preview_InvalidRule_ShowsTheServiceMessage()
    {
        _vm.Mode = ScheduleMode.Advanced;
        _vm.RawRule = "FREQ=SECONDLY";
        _gateway.FailNext(nameof(IServiceGateway.PreviewScheduleAsync), new GapActivatorRequestException(IpcErrorCodes.Invalid, "SECONDLY is not supported.", retryable: false));

        _time.Advance(Delay);

        _vm.PreviewError.ShouldBe("The service rejected the input: SECONDLY is not supported.");
        _vm.Preview.ShouldBeEmpty();
        _vm.IsPreviewLoading.ShouldBeFalse();
        _vm.ShowPreviewNone.ShouldBeFalse();
    }

    [Fact]
    public void Preview_Disconnected_ShowsTheLocalizedConnectionText()
    {
        _vm.Load(null);
        _gateway.FailNext(nameof(IServiceGateway.PreviewScheduleAsync), new GapActivatorRequestException(IpcErrorCodes.Disconnected, "not connected", retryable: true));

        _time.Advance(Delay);

        _vm.PreviewError.ShouldBe("Not connected to the service.");
    }

    [Fact]
    public void Preview_NoOccurrences_ShowsTheEmptyState()
    {
        _gateway.Preview = (_, _, _) => [];
        _vm.Load(null);

        _time.Advance(Delay);

        _vm.Preview.ShouldBeEmpty();
        _vm.ShowPreviewNone.ShouldBeTrue();
        _vm.PreviewError.ShouldBeNull();
    }

    [Fact]
    public void Preview_Once_ShowsTheInstantWithoutCalling()
    {
        _vm.Mode = ScheduleMode.Once;
        _vm.StartDate = new DateTime(2030, 2, 1);
        _vm.StartTime = new TimeSpan(9, 30, 0);

        _time.Advance(Delay);

        PreviewCalls().ShouldBe(0);
        _vm.Preview.ShouldBe([_times.WithWeekday(new DateTimeOffset(2030, 2, 1, 9, 30, 0, TimeSpan.FromHours(-3)))]);
        _vm.PreviewError.ShouldBeNull();
        _vm.Build(null).Kind.ShouldBe(ScheduleInputKind.OneTime);
    }

    [Fact]
    public void Preview_OnceNotInTheFuture_ShowsTheScheduleInPastText()
    {
        _vm.Mode = ScheduleMode.Once;
        _vm.StartDate = new DateTime(2030, 1, 7);
        _vm.StartTime = new TimeSpan(9, 0, 0); // now is 09:00 at -03:00: not after now

        _time.Advance(Delay);

        PreviewCalls().ShouldBe(0);
        _vm.PreviewError.ShouldBe("A one-time schedule must be in the future.");
        _vm.Preview.ShouldBeEmpty();
    }

    [Fact]
    public void Weekly_NoDay_IsInvalidWithTheHint()
    {
        _vm.Load(null);
        _vm.Mode = ScheduleMode.Weekly;

        _vm.IsValid.ShouldBeFalse();
        _vm.ValidationText.ShouldBe("Choose at least one weekday.");
        _time.Advance(Delay);
        PreviewCalls().ShouldBe(0);
        Should.Throw<InvalidOperationException>(() => _vm.Build(null));

        _vm.Wednesday = true;

        _vm.IsValid.ShouldBeTrue();
        _vm.ValidationText.ShouldBeNull();
        _vm.Build(null).Rule.ShouldBe("FREQ=WEEKLY;BYDAY=WE;BYHOUR=10;BYMINUTE=0");
    }

    [Fact]
    public void Advanced_EmptyRule_IsInvalidAndCallsNothing()
    {
        _vm.Mode = ScheduleMode.Advanced;
        _vm.RawRule = "   ";

        _vm.IsValid.ShouldBeFalse();
        _vm.ValidationText.ShouldNotBeNull();
        _time.Advance(Delay);
        PreviewCalls().ShouldBe(0);
    }

    [Fact]
    public void Mode_Panels_FollowTheMode()
    {
        _vm.Load(null);

        (_vm.IsIntervalMode, _vm.IsTimeMode, _vm.IsWeekly, _vm.IsMonthly, _vm.IsOnce, _vm.IsAdvanced).ShouldBe((false, true, false, false, false, false));

        _vm.Mode = ScheduleMode.EveryHours;
        (_vm.IsIntervalMode, _vm.IsTimeMode, _vm.IsStartMode).ShouldBe((true, false, true));

        _vm.Mode = ScheduleMode.Monthly;
        (_vm.IsTimeMode, _vm.IsMonthly).ShouldBe((true, true));

        _vm.Mode = ScheduleMode.Once;
        (_vm.IsOnce, _vm.IsStartMode, _vm.IsTimeMode).ShouldBe((true, true, false));

        _vm.Mode = ScheduleMode.Advanced;
        (_vm.IsAdvanced, _vm.IsStartMode).ShouldBe((true, true));
    }

    [Fact]
    public void Interval_SwitchingToHours_KeepsItInsideTheHourLimit()
    {
        _vm.Load(null);
        _vm.Mode = ScheduleMode.EveryMinutes;
        _vm.Interval = 1000;

        _vm.Mode = ScheduleMode.EveryHours;

        _vm.Interval.ShouldBe(ScheduleRules.MaxHours);
        _vm.IntervalMaximum.ShouldBe(ScheduleRules.MaxHours);
    }

    [Fact]
    public void Build_Monthly_UsesTheDayAndTime()
    {
        _vm.Load(null);
        _vm.Mode = ScheduleMode.Monthly;
        _vm.DayOfMonth = 31;
        _vm.Time = new TimeSpan(23, 59, 0);

        Guid id = Guid.NewGuid();

        _vm.Build(id).ShouldBe(new ScheduleInputDto(
            id,
            ScheduleInputKind.Recurring,
            new DateTimeOffset(2030, 1, 7, 23, 59, 0, TimeSpan.FromHours(-3)),
            "FREQ=MONTHLY;BYMONTHDAY=31;BYHOUR=23;BYMINUTE=59"));
    }

    [Fact]
    public void Language_Switched_RebuildsThePreviewAndTheTexts()
    {
        _vm.Load(null);
        _time.Advance(Delay);
        string english = _vm.Preview[0];
        string englishMode = _vm.ModeOptions.Single(o => o.Mode == ScheduleMode.Daily).Text;

        _l.SetLanguage(AppLanguage.Portuguese);

        _vm.Preview[0].ShouldNotBe(english);
        _vm.Preview[0].ShouldBe(_times.WithWeekday(new DateTimeOffset(2030, 1, 8, 10, 0, 0, TimeSpan.FromHours(-3))));
        _vm.ModeOptions.Single(o => o.Mode == ScheduleMode.Daily).Text.ShouldBe("Diariamente");
        englishMode.ShouldBe("Daily");
        _vm.SelectedModeOption.Mode.ShouldBe(ScheduleMode.Daily);
    }

    [Fact]
    public void Language_Switched_RebuildsTheValidationText()
    {
        _vm.Load(null);
        _vm.Mode = ScheduleMode.Weekly;
        _vm.ValidationText.ShouldBe("Choose at least one weekday.");

        _l.SetLanguage(AppLanguage.Portuguese);

        _vm.ValidationText.ShouldBe("Escolha pelo menos um dia da semana.");
        _vm.SelectedModeOption.Mode.ShouldBe(ScheduleMode.Weekly);
    }

    [Fact]
    public void Language_Switched_RebuildsAPreviewError()
    {
        _vm.Load(null);
        _gateway.FailNext(nameof(IServiceGateway.PreviewScheduleAsync), new GapActivatorRequestException(IpcErrorCodes.Disconnected, "x", retryable: true));
        _time.Advance(Delay);
        _vm.PreviewError.ShouldBe("Not connected to the service.");

        _l.SetLanguage(AppLanguage.Portuguese);

        _vm.PreviewError.ShouldBe("Sem conexão com o serviço.");
    }

    [Fact]
    public void ModeOptions_AreTheSevenModesInOrder()
    {
        _vm.ModeOptions.Select(o => o.Mode).ShouldBe(
            [ScheduleMode.EveryMinutes, ScheduleMode.EveryHours, ScheduleMode.Daily, ScheduleMode.Weekly, ScheduleMode.Monthly, ScheduleMode.Once, ScheduleMode.Advanced]);
        _vm.ModeOptions.Select(o => o.Text).ShouldBe(
            ["Every N minutes", "Every N hours", "Daily", "Weekly", "Monthly", "Once", "Advanced (RRULE)"]);
    }

    [Fact]
    public void SelectedModeOption_Set_ChangesTheMode()
    {
        _vm.SelectedModeOption = _vm.ModeOptions.Single(o => o.Mode == ScheduleMode.Monthly);

        _vm.Mode.ShouldBe(ScheduleMode.Monthly);
    }

    // Preflight ruling C12: the timer thread never touches view-model state; the work goes through the UI dispatcher.
    [Fact]
    public void Preview_TimerCallback_GoesThroughTheUiDispatcher()
    {
        var queue = new QueueDispatcher();
        using var vm = new ScheduleEditorViewModel(_l, _gateway, _time, _zone, _times, queue);
        vm.Load(null);

        _time.Advance(Delay);

        PreviewCalls().ShouldBe(0);
        vm.Preview.ShouldBeEmpty();
        queue.Pending.ShouldBe(1);

        queue.Run();

        PreviewCalls().ShouldBe(1);
        vm.Preview.Count.ShouldBe(5);
    }

    [Fact]
    public async Task Dispose_CancelsTheInFlightPreviewRequest()
    {
        _vm.Load(null);
        TaskCompletionSource hold = _gateway.Hold(nameof(IServiceGateway.PreviewScheduleAsync));
        _time.Advance(Delay);
        CancellationToken token = _gateway.LastPreviewToken;
        token.IsCancellationRequested.ShouldBeFalse();
        Task refresh = _vm.PreviewRefresh;

        _vm.Dispose();

        token.IsCancellationRequested.ShouldBeTrue();
        hold.SetResult();
        await refresh;
        _vm.Preview.ShouldBeEmpty();
    }

    [Fact]
    public async Task Preview_NewRequest_CancelsTheOlderOne()
    {
        _vm.Load(null);
        TaskCompletionSource hold = _gateway.Hold(nameof(IServiceGateway.PreviewScheduleAsync));
        _time.Advance(Delay);
        CancellationToken first = _gateway.LastPreviewToken;
        _vm.Time = TimeSpan.FromHours(13);
        _time.Advance(Delay);

        first.IsCancellationRequested.ShouldBeTrue();
        _gateway.LastPreviewToken.IsCancellationRequested.ShouldBeFalse();
        hold.SetResult();
        await EventuallyAsync(() => !_vm.IsPreviewLoading);
        _vm.Preview.Count.ShouldBe(5);
    }

    [Fact]
    public async Task Preview_CancellationNotFromTheEditor_ClearsLoadingAndShowsTheUnexpectedError()
    {
        _vm.Load(null);
        _gateway.FailNext(nameof(IServiceGateway.PreviewScheduleAsync), new OperationCanceledException());

        _time.Advance(Delay);
        await EventuallyAsync(() => !_vm.IsPreviewLoading);

        _vm.PreviewError.ShouldStartWith("Unexpected error:");
    }

    [Fact]
    public void Preview_WhenBuildThrows_ClearsLoadingAndShowsTheUnexpectedError()
    {
        _vm.Mode = ScheduleMode.EveryMinutes;
        _vm.StartDate = DateTime.MaxValue.Date;
        _vm.StartTime = TimeSpan.FromHours(23); // plus the 3 h offset, past the largest instant
        _vm.IsPreviewLoading.ShouldBeTrue();

        _time.Advance(Delay);

        PreviewCalls().ShouldBe(0);
        _vm.IsPreviewLoading.ShouldBeFalse();
        _vm.PreviewError.ShouldStartWith("Unexpected error:");
    }

    [Fact]
    public void Interval_Cleared_IsInvalidWithTheRequiredText()
    {
        _vm.Load(null);
        _vm.Mode = ScheduleMode.EveryMinutes;

        _vm.Interval = null;

        _vm.IsValid.ShouldBeFalse();
        _vm.ValidationText.ShouldBe("Enter a whole number.");
        _time.Advance(Delay);
        PreviewCalls().ShouldBe(0);

        _vm.Interval = 5;

        _vm.IsValid.ShouldBeTrue();
        _vm.ValidationText.ShouldBeNull();
    }

    [Fact]
    public void DayOfMonth_Cleared_IsInvalidWithTheRequiredText()
    {
        _vm.Load(null);
        _vm.Mode = ScheduleMode.Monthly;

        _vm.DayOfMonth = null;

        _vm.IsValid.ShouldBeFalse();
        _vm.ValidationText.ShouldBe("Enter a whole number.");
    }

    [Fact]
    public void Build_FractionalNumbers_AreRoundedAsTheBoxesShowThem()
    {
        _vm.Load(null);
        _vm.Mode = ScheduleMode.EveryMinutes;
        _vm.Interval = 2.5m;
        _vm.Build(null).Rule.ShouldBe("FREQ=MINUTELY;INTERVAL=3");

        _vm.Mode = ScheduleMode.Monthly;
        _vm.DayOfMonth = 14.5m;
        _vm.Build(null).Rule.ShouldBe("FREQ=MONTHLY;BYMONTHDAY=15;BYHOUR=10;BYMINUTE=0");
    }

    [Fact]
    public void Preview_AfterReconnect_IsRequestedAgain()
    {
        _vm.Load(null);
        _gateway.FailNext(nameof(IServiceGateway.PreviewScheduleAsync), new GapActivatorRequestException(IpcErrorCodes.Disconnected, "x", retryable: true));
        _time.Advance(Delay);
        _vm.PreviewError.ShouldBe("Not connected to the service.");

        _gateway.SetStatus(FakeServiceGateway.Operator());
        _vm.IsPreviewLoading.ShouldBeTrue();
        _time.Advance(Delay);

        PreviewCalls().ShouldBe(2);
        _vm.PreviewError.ShouldBeNull();
        _vm.Preview.Count.ShouldBe(5);
    }

    [Fact]
    public void Preview_NotConnectedStatus_DoesNotRequestAgain()
    {
        _vm.Load(null);
        _time.Advance(Delay);

        _gateway.SetStatus(FakeServiceGateway.Stopped);
        _time.Advance(Delay);

        PreviewCalls().ShouldBe(1);
    }

    [Fact]
    public void Dispose_StopsListeningToTheConnection()
    {
        _vm.Load(null);
        _time.Advance(Delay);
        _vm.Dispose();

        _gateway.SetStatus(FakeServiceGateway.Operator());
        _time.Advance(Delay);

        PreviewCalls().ShouldBe(1);
    }

    [Fact]
    public void ClockIdentifier_FollowsTheLanguage()
    {
        _vm.ClockIdentifier.ShouldBe("12HourClock");

        _l.SetLanguage(AppLanguage.Portuguese);

        _vm.ClockIdentifier.ShouldBe("24HourClock");

        _l.SetLanguage(AppLanguage.English);

        _vm.ClockIdentifier.ShouldBe("12HourClock");
    }

    [Fact]
    public void Dispose_CancelsAPendingPreview()
    {
        _vm.Load(null);

        _vm.Dispose();
        _time.Advance(Delay);

        PreviewCalls().ShouldBe(0);
    }

    private sealed class QueueDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> _queue = new();

        public int Pending => _queue.Count;

        public void Post(Action action) => _queue.Enqueue(action);

        public void Run()
        {
            while (_queue.TryDequeue(out Action? action))
            {
                action();
            }
        }
    }
}
