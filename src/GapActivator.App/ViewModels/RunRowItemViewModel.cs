using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.ViewModels;

/// <summary>One run in the history list. Opening it shows the run detail.</summary>
public sealed partial class RunRowItemViewModel : ViewModelBase
{
    private readonly TimeDisplay _times;
    private RunRowDto _run;

    public RunRowItemViewModel(RunRowDto run, Localizer l, TimeDisplay times, Action<RunRowItemViewModel> open)
        : base(l)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(open);
        _times = times;
        _run = run;
        Id = run.Id;
        OpenCommand = new RelayCommand(() => open(this));
        Update(run);
    }

    public Guid Id { get; }

    public IRelayCommand OpenCommand { get; }

    [ObservableProperty]
    public partial string ProcessName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string TriggerText { get; private set; } = string.Empty;

    /// <summary>The outcome while the run is over, the state while it is active.</summary>
    [ObservableProperty]
    public partial string StatusText { get; private set; } = string.Empty;

    /// <summary>True for the outcomes the app calls failures: the row adds a glyph so the state never rests on color.</summary>
    [ObservableProperty]
    public partial bool IsFailure { get; private set; }

    [ObservableProperty]
    public partial bool IsActive { get; private set; }

    /// <summary>True for a run that is over and is neither a failure nor active (succeeded, cancelled, skipped...): the neutral badge.</summary>
    [ObservableProperty]
    public partial bool IsNeutral { get; private set; }

    /// <summary>A symbol that repeats the status, so it never rests on the badge color alone.</summary>
    [ObservableProperty]
    public partial string StatusGlyph { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLate { get; private set; }

    [ObservableProperty]
    public partial string CreatedText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? DurationText { get; private set; }

    [ObservableProperty]
    public partial string? ExitCodeText { get; private set; }

    /// <summary>The row's accessible name: process, status and time, then the action.</summary>
    [ObservableProperty]
    public partial string AutomationName { get; private set; } = string.Empty;

    /// <summary>Recomputes every text; also the way to follow a language switch or a refresh.</summary>
    public void Update(RunRowDto run)
    {
        ArgumentNullException.ThrowIfNull(run);
        _run = run;
        ProcessName = run.ProcessName;
        TriggerText = L.TriggerText(run.Trigger);
        IsActive = run.State != RunState.Completed;
        StatusText = !IsActive && run.Outcome is { } outcome ? L.OutcomeText(outcome) : L.StateText(run.State);
        IsFailure = !IsActive && run.Outcome is { } o && RunOutcomes.Failures.Contains(o);
        IsNeutral = !IsActive && !IsFailure;
        StatusGlyph = IsActive ? "\uE895" : IsFailure ? "\uE7BA" : run.Outcome == RunOutcome.Succeeded ? "\uE73E" : "\uE711";
        IsLate = run.IsLate;
        CreatedText = _times.DateTime(run.CreatedAt);
        DurationText = run.StartedAt is { } start && run.EndedAt is { } end ? Durations.Format(end - start, L) : null;
        ExitCodeText = run.ExitCode?.ToString(L.Culture);
        AutomationName = L.OpenRunFor(ProcessName, StatusText, CreatedText);
    }

    /// <summary>Re-reads the texts of the last run; called after a language switch.</summary>
    public void Refresh() => Update(_run);
}
