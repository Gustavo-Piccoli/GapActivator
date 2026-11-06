using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GapActivator.App.Localization;
using GapActivator.App.Scheduling;
using GapActivator.App.Services;
using GapActivator.Ipc;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.ViewModels;

/// <summary>One entry of the mode list; its text follows the language.</summary>
public sealed class ScheduleModeOption(ScheduleMode mode, Func<string> text) : ObservableObject
{
    public ScheduleMode Mode { get; } = mode;

    public string Text => text();

    internal void Refresh() => OnPropertyChanged(nameof(Text));
}

/// <summary>One weekday toggle; it reads and writes the editor's flag for its day.</summary>
public sealed class WeekdayToggle(DayOfWeek day, Func<bool> get, Action<bool> set, Func<string> text, Func<string> name) : ObservableObject
{
    public DayOfWeek Day { get; } = day;

    public bool IsChecked
    {
        get => get();
        set
        {
            set(value);
            OnPropertyChanged();
        }
    }

    public string Text => text();

    /// <summary>The full day name, read by screen readers (the short text alone is ambiguous).</summary>
    public string Name => name();

    internal void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>
/// Edits one schedule: a mode, the fields of that mode and a live "next 5 runs" preview from the service. The preview waits 300 ms
/// after the last change, drops answers older than the latest request and rebuilds its text when the language changes.
/// </summary>
public sealed partial class ScheduleEditorViewModel : ViewModelBase, IDisposable
{
    public const int PreviewCount = 5;

    private static readonly DayOfWeek[] MondayFirst =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    ];

    private static readonly HashSet<string> InputProperties =
    [
        nameof(Mode), nameof(Interval), nameof(Time), nameof(Monday), nameof(Tuesday), nameof(Wednesday), nameof(Thursday),
        nameof(Friday), nameof(Saturday), nameof(Sunday), nameof(DayOfMonth), nameof(StartDate), nameof(StartTime), nameof(RawRule),
    ];

    private readonly IServiceGateway _gateway;
    private readonly TimeProvider _time;
    private readonly TimeZoneInfo _zone;
    private readonly TimeDisplay _times;
    private readonly IUiDispatcher _ui;
    private readonly Debouncer _debouncer;

    private int _generation;
    private bool _suspend;
    private bool _disposed;
    private bool _previewReady;
    private Validation _validation;
    private IReadOnlyList<DateTimeOffset> _instants = [];
    private Func<string>? _previewErrorText;
    private CancellationTokenSource? _previewCts;

    public ScheduleEditorViewModel(Localizer l, IServiceGateway gateway, TimeProvider time, TimeZoneInfo zone, TimeDisplay times, IUiDispatcher ui)
        : base(l)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(ui);
        _gateway = gateway;
        _time = time;
        _zone = zone;
        _times = times;
        _ui = ui;
        _debouncer = new Debouncer(PreviewDelay, time);

        ModeOptions =
        [
            .. Enum.GetValues<ScheduleMode>().Select(m => new ScheduleModeOption(m, () => L.ModeText(m))),
        ];
        Weekdays =
        [
            .. MondayFirst.Select(d => new WeekdayToggle(d, () => GetDay(d), v => SetDay(d, v), () => L.DayShort(d), () => L.DayFull(d))),
        ];

        _suspend = true;
        ApplyDefaults();
        _suspend = false;
        Revalidate();
        L.PropertyChanged += OnLanguageChanged;
        _gateway.ConnectionChanged += OnConnectionChanged;
    }

    public static TimeSpan PreviewDelay { get; } = TimeSpan.FromMilliseconds(300);

    [ObservableProperty]
    public partial ScheduleMode Mode { get; set; }

    [ObservableProperty]
    public partial decimal? Interval { get; set; }

    [ObservableProperty]
    public partial TimeSpan Time { get; set; }

    [ObservableProperty]
    public partial bool Monday { get; set; }

    [ObservableProperty]
    public partial bool Tuesday { get; set; }

    [ObservableProperty]
    public partial bool Wednesday { get; set; }

    [ObservableProperty]
    public partial bool Thursday { get; set; }

    [ObservableProperty]
    public partial bool Friday { get; set; }

    [ObservableProperty]
    public partial bool Saturday { get; set; }

    [ObservableProperty]
    public partial bool Sunday { get; set; }

    [ObservableProperty]
    public partial decimal? DayOfMonth { get; set; }

    [ObservableProperty]
    public partial DateTime? StartDate { get; set; }

    [ObservableProperty]
    public partial TimeSpan StartTime { get; set; }

    [ObservableProperty]
    public partial string RawRule { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? PreviewError { get; private set; }

    [ObservableProperty]
    public partial bool IsPreviewLoading { get; private set; }

    [ObservableProperty]
    public partial bool IsValid { get; private set; }

    [ObservableProperty]
    public partial string? ValidationText { get; private set; }

    public ObservableCollection<ScheduleModeOption> ModeOptions { get; }

    public ObservableCollection<WeekdayToggle> Weekdays { get; }

    public ObservableCollection<string> Preview { get; } = [];

    /// <summary>The latest preview request; tests await it, the screen does not need to.</summary>
    internal Task PreviewRefresh { get; private set; } = Task.CompletedTask;

    public ScheduleModeOption SelectedModeOption
    {
        get => ModeOptions.First(o => o.Mode == Mode);
        set
        {
            if (value is not null)
            {
                Mode = value.Mode;
            }
        }
    }

    public bool IsIntervalMode => Mode is ScheduleMode.EveryMinutes or ScheduleMode.EveryHours;

    public bool IsTimeMode => Mode is ScheduleMode.Daily or ScheduleMode.Weekly or ScheduleMode.Monthly;

    public bool IsWeekly => Mode == ScheduleMode.Weekly;

    public bool IsMonthly => Mode == ScheduleMode.Monthly;

    public bool IsOnce => Mode == ScheduleMode.Once;

    public bool IsAdvanced => Mode == ScheduleMode.Advanced;

    /// <summary>The modes that carry a start date and time: the interval modes, Once and Advanced.</summary>
    public bool IsStartMode => IsIntervalMode || IsOnce || IsAdvanced;

    public decimal IntervalMaximum => Mode == ScheduleMode.EveryHours ? ScheduleRules.MaxHours : ScheduleRules.MaxMinutes;

    public string IntervalUnitText => Mode == ScheduleMode.EveryHours ? L.HoursUnit : L.MinutesUnit;

    /// <summary>The clock the time pickers show: 12-hour when the language's short time pattern uses <c>h</c>, as the preview text does.</summary>
    public string ClockIdentifier => L.Culture.DateTimeFormat.ShortTimePattern.Contains('h') ? "12HourClock" : "24HourClock";

    public string StartLabelText => IsOnce ? L.OnceLabel : L.StartingLabel;

    public bool HasPreviewError => PreviewError is not null;

    public bool ShowPreviewList => Preview.Count > 0 && PreviewError is null;

    public bool ShowPreviewNone => IsValid && _previewReady && !IsPreviewLoading && PreviewError is null && Preview.Count == 0;

    private enum Validation
    {
        None,
        NoWeekday,
        RuleRequired,
        DateRequired,
        NumberRequired,
    }

    /// <summary>Shows <paramref name="existing"/> (null: a new schedule, Daily at the next full hour).</summary>
    public void Load(ScheduleViewDto? existing)
    {
        _suspend = true;
        try
        {
            if (existing is null)
            {
                ApplyDefaults();
            }
            else
            {
                Apply(ScheduleRules.FromView(existing.Kind, existing.At, existing.Rule, _zone));
            }
        }
        finally
        {
            _suspend = false;
        }

        RaiseModePanels();
        foreach (WeekdayToggle toggle in Weekdays)
        {
            toggle.Refresh();
        }

        Touch();
    }

    /// <summary>The input to send to the service. Only call it while <see cref="IsValid"/>.</summary>
    public ScheduleInputDto Build(Guid? id)
    {
        if (!IsValid)
        {
            throw new InvalidOperationException("The schedule is not valid.");
        }

        return ScheduleRules.ToInput(Current(), id, _zone, Today());
    }

    /// <summary>True once <see cref="Dispose"/> has run.</summary>
    public bool IsDisposed => _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        L.PropertyChanged -= OnLanguageChanged;
        _gateway.ConnectionChanged -= OnConnectionChanged;
        _debouncer.Dispose();
        CancelPreview();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        string? name = e.PropertyName;
        if (name is null || !InputProperties.Contains(name))
        {
            return;
        }

        if (name == nameof(Mode))
        {
            ClampInterval();
            RaiseModePanels();
        }
        else if (Array.Exists(MondayFirst, d => d.ToString() == name))
        {
            Weekdays.First(w => w.Day.ToString() == name).Refresh();
        }

        Touch();
    }

    private static string Describe(Exception error, Localizer l) => ServiceErrors.Describe(error, l);

    private DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _zone).DateTime);

    private bool GetDay(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => Monday,
        DayOfWeek.Tuesday => Tuesday,
        DayOfWeek.Wednesday => Wednesday,
        DayOfWeek.Thursday => Thursday,
        DayOfWeek.Friday => Friday,
        DayOfWeek.Saturday => Saturday,
        _ => Sunday,
    };

    private void SetDay(DayOfWeek day, bool value)
    {
        switch (day)
        {
            case DayOfWeek.Monday:
                Monday = value;
                break;
            case DayOfWeek.Tuesday:
                Tuesday = value;
                break;
            case DayOfWeek.Wednesday:
                Wednesday = value;
                break;
            case DayOfWeek.Thursday:
                Thursday = value;
                break;
            case DayOfWeek.Friday:
                Friday = value;
                break;
            case DayOfWeek.Saturday:
                Saturday = value;
                break;
            default:
                Sunday = value;
                break;
        }
    }

    private void ApplyDefaults()
    {
        DateTime local = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _zone).DateTime;
        DateTime nextHour = local.Date.AddHours(local.Hour + 1);
        Apply(new FriendlySchedule(ScheduleMode.Daily, 1, TimeOnly.FromDateTime(nextHour), [], 1, nextHour, null));
    }

    private void Apply(FriendlySchedule schedule)
    {
        Mode = schedule.Mode;
        Interval = schedule.Interval;
        Time = schedule.Time.ToTimeSpan();
        Monday = schedule.Days.Contains(DayOfWeek.Monday);
        Tuesday = schedule.Days.Contains(DayOfWeek.Tuesday);
        Wednesday = schedule.Days.Contains(DayOfWeek.Wednesday);
        Thursday = schedule.Days.Contains(DayOfWeek.Thursday);
        Friday = schedule.Days.Contains(DayOfWeek.Friday);
        Saturday = schedule.Days.Contains(DayOfWeek.Saturday);
        Sunday = schedule.Days.Contains(DayOfWeek.Sunday);
        DayOfMonth = schedule.DayOfMonth;
        StartDate = schedule.Start.Date;
        StartTime = schedule.Start.TimeOfDay;
        RawRule = schedule.Rule ?? string.Empty;
    }

    private FriendlySchedule Current()
    {
        DateTime start = (StartDate ?? Today().ToDateTime(TimeOnly.MinValue)).Date + StartTime;
        List<DayOfWeek> days = [.. MondayFirst.Where(GetDay)];
        return new FriendlySchedule(
            Mode,
            (int)Math.Clamp(Math.Round(Interval ?? 1, MidpointRounding.AwayFromZero), 1, IntervalMaximum),
            TimeOnly.FromTimeSpan(Time),
            days,
            (int)Math.Clamp(Math.Round(DayOfMonth ?? 1, MidpointRounding.AwayFromZero), 1, 31),
            DateTime.SpecifyKind(start, DateTimeKind.Unspecified),
            Mode == ScheduleMode.Advanced ? RawRule : null);
    }

    private void ClampInterval()
    {
        if (Interval > IntervalMaximum)
        {
            Interval = IntervalMaximum;
        }
    }

    private void RaiseModePanels()
    {
        OnPropertyChanged(nameof(SelectedModeOption));
        OnPropertyChanged(nameof(IsIntervalMode));
        OnPropertyChanged(nameof(IsTimeMode));
        OnPropertyChanged(nameof(IsWeekly));
        OnPropertyChanged(nameof(IsMonthly));
        OnPropertyChanged(nameof(IsOnce));
        OnPropertyChanged(nameof(IsAdvanced));
        OnPropertyChanged(nameof(IsStartMode));
        OnPropertyChanged(nameof(IntervalMaximum));
        OnPropertyChanged(nameof(IntervalUnitText));
        OnPropertyChanged(nameof(StartLabelText));
    }

    private Validation Validate() => Mode switch
    {
        ScheduleMode.EveryMinutes or ScheduleMode.EveryHours when Interval is null => Validation.NumberRequired,
        ScheduleMode.Monthly when DayOfMonth is null => Validation.NumberRequired,
        ScheduleMode.Weekly when !MondayFirst.Any(GetDay) => Validation.NoWeekday,
        ScheduleMode.Advanced when string.IsNullOrWhiteSpace(RawRule) => Validation.RuleRequired,
        ScheduleMode.EveryMinutes or ScheduleMode.EveryHours or ScheduleMode.Once or ScheduleMode.Advanced when StartDate is null => Validation.DateRequired,
        _ => Validation.None,
    };

    private string? ValidationFor(Validation validation) => validation switch
    {
        Validation.NoWeekday => L.NoWeekday,
        Validation.RuleRequired => L.RuleRequired,
        Validation.DateRequired => L.DateRequired,
        Validation.NumberRequired => L.NumberRequired,
        _ => null,
    };

    private void Revalidate()
    {
        _validation = Validate();
        IsValid = _validation == Validation.None;
        ValidationText = ValidationFor(_validation);
    }

    // Every change of a field lands here: older answers stop counting, and a valid schedule is previewed after the debounce.
    private void Touch()
    {
        if (_suspend || _disposed)
        {
            return;
        }

        _generation++;
        Revalidate();
        if (!IsValid)
        {
            _instants = [];
            _previewReady = false;
            SetPreviewError(null);
            IsPreviewLoading = false;
            RebuildPreview();
            return;
        }

        IsPreviewLoading = true;
        NotifyPreviewState();

        // The debouncer's timer fires on a thread-pool thread; the view-model state is touched only after the hop to the UI thread.
        _debouncer.Schedule(() =>
        {
            _ui.Post(() => PreviewRefresh = RefreshPreviewAsync());
            return Task.CompletedTask;
        });
    }

    private async Task RefreshPreviewAsync()
    {
        if (_disposed || _validation != Validation.None)
        {
            return;
        }

        int generation = _generation;
        CancellationTokenSource cts = NewPreviewRequest();
        try
        {
            ScheduleInputDto input = Build(null);
            if (input.Kind == ScheduleInputKind.OneTime)
            {
                ShowOnce(input.At);
                return;
            }

            IReadOnlyList<DateTimeOffset> result = await _gateway.PreviewScheduleAsync(input.Rule!, input.At, PreviewCount, cts.Token).ConfigureAwait(true);
            if (generation != _generation || _disposed)
            {
                return;
            }

            _instants = [.. result];
            _previewReady = true;
            SetPreviewError(null);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Superseded by a newer request (which keeps the loading state) or disposed: nothing to show. Only a request that
            // nobody replaced clears the state, so "Calculating" never sticks.
            if (!_disposed && ReferenceEquals(_previewCts, cts))
            {
                IsPreviewLoading = false;
                NotifyPreviewState();
            }

            return;
        }
        catch (Exception ex)
        {
            if (generation != _generation || _disposed)
            {
                return;
            }

            _instants = [];
            _previewReady = true;
            SetPreviewError(() => Describe(ex, L));
        }

        IsPreviewLoading = false;
        RebuildPreview();
    }

    private CancellationTokenSource NewPreviewRequest()
    {
        CancellationTokenSource cts = new();
        CancellationTokenSource? previous = Interlocked.Exchange(ref _previewCts, cts);
        CancelAndDispose(previous);
        return cts;
    }

    private void CancelPreview() => CancelAndDispose(Interlocked.Exchange(ref _previewCts, null));

    private static void CancelAndDispose(CancellationTokenSource? cts)
    {
        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        cts.Dispose();
    }

    // The gateway raises this through the UI dispatcher already; the post only defers the refresh until the handlers are done.
    // Coming back means a failed preview can work now.
    private void OnConnectionChanged(object? sender, GatewayStatus status)
    {
        if (status.State == ConnectionState.Connected)
        {
            _ui.Post(Touch);
        }
    }

    private void ShowOnce(DateTimeOffset at)
    {
        _previewReady = true;
        if (at <= _time.GetUtcNow())
        {
            _instants = [];
            SetPreviewError(() => ServiceErrors.Describe(IpcErrorCodes.ScheduleInPast, null, L));
        }
        else
        {
            _instants = [at];
            SetPreviewError(null);
        }

        IsPreviewLoading = false;
        RebuildPreview();
    }

    private void SetPreviewError(Func<string>? text)
    {
        _previewErrorText = text;
        PreviewError = text?.Invoke();
        OnPropertyChanged(nameof(HasPreviewError));
    }

    // The lines are formatted from the instants, so they can be rebuilt when the language changes.
    private void RebuildPreview()
    {
        Preview.Clear();
        foreach (DateTimeOffset instant in _instants)
        {
            Preview.Add(_times.WithWeekday(instant));
        }

        NotifyPreviewState();
    }

    private void NotifyPreviewState()
    {
        OnPropertyChanged(nameof(ShowPreviewList));
        OnPropertyChanged(nameof(ShowPreviewNone));
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (ScheduleModeOption option in ModeOptions)
        {
            option.Refresh();
        }

        foreach (WeekdayToggle toggle in Weekdays)
        {
            toggle.Refresh();
        }

        ValidationText = ValidationFor(_validation);
        if (_previewErrorText is not null)
        {
            PreviewError = _previewErrorText();
        }

        RebuildPreview();
        OnPropertyChanged(string.Empty);
    }

    partial void OnPreviewErrorChanged(string? value) => NotifyPreviewState();

    partial void OnIsPreviewLoadingChanged(bool value) => NotifyPreviewState();

    partial void OnIsValidChanged(bool value) => NotifyPreviewState();
}
