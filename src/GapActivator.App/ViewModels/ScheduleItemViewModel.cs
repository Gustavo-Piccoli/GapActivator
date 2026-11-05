using CommunityToolkit.Mvvm.ComponentModel;
using GapActivator.App.Localization;
using GapActivator.App.Scheduling;
using GapActivator.App.Services;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.ViewModels;

/// <summary>
/// One schedule in the editor's list: what the service stored (<see cref="Original"/>, null for a new one) and what will be sent
/// (<see cref="Input"/>). An unchanged schedule is sent exactly as it came; a fulfilled one-time schedule is not "next", it is
/// no longer pending.
/// </summary>
public sealed partial class ScheduleItemViewModel : ViewModelBase
{
    private readonly TimeDisplay _times;
    private readonly TimeZoneInfo _zone;

    internal ScheduleItemViewModel(ScheduleViewDto? original, ScheduleInputDto input, Localizer l, TimeDisplay times, TimeZoneInfo zone)
        : base(l)
    {
        Original = original;
        Input = input;
        _times = times;
        _zone = zone;
        IsModified = original is null || input != ToInput(original);
        Refresh();
    }

    public ScheduleViewDto? Original { get; }

    public ScheduleInputDto Input { get; }

    public bool IsModified { get; }

    [ObservableProperty]
    public partial string SummaryText { get; private set; } = string.Empty;

    /// <summary>The next run, "no next run" or "no longer pending since"; a changed schedule says it is saved with the process.</summary>
    [ObservableProperty]
    public partial string StatusText { get; private set; } = string.Empty;

    public string EditAutomationName => L.EditScheduleFor(SummaryText);

    public string RemoveAutomationName => L.RemoveScheduleFor(SummaryText);

    internal static ScheduleInputDto ToInput(ScheduleViewDto view) => new(view.Id, view.Kind, view.At, view.Rule);

    /// <summary>The schedule as the form of the schedule editor reads it.</summary>
    internal ScheduleViewDto AsView() =>
        Original is not null && !IsModified
            ? Original
            : new ScheduleViewDto(Input.Id ?? Guid.Empty, Input.Kind, Input.At, Input.Rule, null, null);

    /// <summary>Rebuilds the cached texts; also the way to follow a language switch.</summary>
    internal void Refresh()
    {
        SummaryText = ScheduleText.Summary(ScheduleRules.FromView(Input.Kind, Input.At, Input.Rule, _zone), L, _times, _zone);
        if (IsModified)
        {
            StatusText = L.ScheduleChanged;
        }
        else if (Original is { Kind: ScheduleInputKind.OneTime, FulfilledAt: { } fulfilled })
        {
            StatusText = L.ScheduleNoLongerPending(_times.DateTime(fulfilled));
        }
        else
        {
            StatusText = Original?.NextOccurrenceAt is { } next ? L.NextRun(_times.DateTime(next)) : L.NoNextRun;
        }

        OnPropertyChanged(nameof(EditAutomationName));
        OnPropertyChanged(nameof(RemoveAutomationName));
    }
}
