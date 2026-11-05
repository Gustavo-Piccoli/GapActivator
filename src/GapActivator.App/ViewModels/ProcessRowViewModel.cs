using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.ViewModels;

/// <summary>
/// One process in the list. The page keeps the instance while the process stays and calls <see cref="Update"/>, so a button the
/// user tabbed to keeps its focus across refreshes. Every command needs the right to change things; Run now stays available on a
/// blocked or disabled process so the service can say why it refuses.
/// </summary>
public sealed partial class ProcessRowViewModel : ViewModelBase
{
    private readonly TimeDisplay _times;
    private readonly Permissions _permissions;

    internal ProcessRowViewModel(ProcessSummaryDto summary, Localizer l, TimeDisplay times, Permissions permissions, ProcessesViewModel owner)
        : base(l)
    {
        _times = times;
        _permissions = permissions;
        Summary = summary;
        Id = summary.Id;

        bool CanChange() => _permissions.CanChange;
        RunNowCommand = new AsyncRelayCommand(() => owner.RunAsync(() => owner.Actions.RunNowAsync(Summary)), CanChange);
        CancelCommand = new AsyncRelayCommand(
            () => Summary.ActiveRun is { } run
                ? owner.RunAsync(() => owner.Runs.CancelOrStopAsync(run.RunId, Id, Summary.Name, Summary.Kind))
                : Task.CompletedTask,
            CanChange);
        EnableCommand = new AsyncRelayCommand(() => owner.RunAsync(() => owner.Actions.EnableAsync(Id)), CanChange);
        DisableCommand = new AsyncRelayCommand(() => owner.RunAsync(() => owner.Actions.DisableAsync(Summary)), CanChange);
        UnblockCommand = new AsyncRelayCommand(() => owner.RunAsync(() => owner.Actions.UnblockAsync(Id, false)), CanChange);
        UnblockAndRunCommand = new AsyncRelayCommand(() => owner.RunAsync(() => owner.Actions.UnblockAsync(Id, true)), CanChange);
        DeleteCommand = new AsyncRelayCommand(() => owner.DeleteAsync(this), CanChange);
        Update(summary);
    }

    public Guid Id { get; }

    internal ProcessSummaryDto Summary { get; private set; }

    public ProcessLifecycle Lifecycle => Summary.Lifecycle;

    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string KindText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string LifecycleText { get; private set; } = string.Empty;

    /// <summary>What the badge means; null for Enabled, which needs no explanation.</summary>
    [ObservableProperty]
    public partial string? LifecycleTooltip { get; private set; }

    /// <summary>The badge's accessible name: the state and, when it has one, its explanation.</summary>
    [ObservableProperty]
    public partial string BadgeAutomationName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string NextText { get; private set; } = string.Empty;

    /// <summary>How the last run ended; null when the process has not run yet.</summary>
    [ObservableProperty]
    public partial string? LastOutcomeText { get; private set; }

    public bool IsRunning => Summary.ActiveRun is not null;

    /// <summary>The state of the active run in words ("Running"); empty when none is active.</summary>
    [ObservableProperty]
    public partial string RunningText { get; private set; } = string.Empty;

    public bool ShowEnable => Summary.Lifecycle == ProcessLifecycle.Disabled;

    public bool ShowDisable => Summary.Lifecycle == ProcessLifecycle.Enabled;

    public bool ShowUnblock => Summary.Lifecycle == ProcessLifecycle.Blocked;

    public bool ShowUnblockAndRun => Summary.Lifecycle == ProcessLifecycle.Blocked && !IsRunning;

    public bool ShowCancel => IsRunning;

    // The badge's style class: one flag per lifecycle, so the view binds without converters.
    public bool IsEnabledState => Summary.Lifecycle == ProcessLifecycle.Enabled;

    public bool IsDisabledState => Summary.Lifecycle == ProcessLifecycle.Disabled;

    public bool IsBlockedState => Summary.Lifecycle == ProcessLifecycle.Blocked;

    /// <summary>"Cancel" for a run to completion, "Stop" for a service-kind process.</summary>
    [ObservableProperty]
    public partial string CancelText { get; private set; } = string.Empty;

    // Every row says the same words on its buttons, so the accessible names carry the process too.
    [ObservableProperty]
    public partial string RunNowAutomationName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string CancelAutomationName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string EnableAutomationName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string DisableAutomationName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string UnblockAutomationName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string UnblockAndRunAutomationName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string DeleteAutomationName { get; private set; } = string.Empty;

    /// <summary>Why the user cannot change anything; null when they can.</summary>
    public string? DeniedReason => _permissions.DeniedReason;

    public IAsyncRelayCommand RunNowCommand { get; }

    public IAsyncRelayCommand CancelCommand { get; }

    public IAsyncRelayCommand EnableCommand { get; }

    public IAsyncRelayCommand DisableCommand { get; }

    public IAsyncRelayCommand UnblockCommand { get; }

    public IAsyncRelayCommand UnblockAndRunCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }

    /// <summary>Recomputes every text and flag from the process; also the way to follow a language switch.</summary>
    public void Update(ProcessSummaryDto summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        Summary = summary;
        Name = summary.Name;
        KindText = L.KindText(summary.Kind);
        LifecycleText = L.LifecycleText(summary.Lifecycle);
        LifecycleTooltip = L.LifecycleTooltip(summary.Lifecycle);
        BadgeAutomationName = LifecycleTooltip is null ? LifecycleText : $"{LifecycleText}: {LifecycleTooltip}";
        NextText = summary.NextScheduledAt is { } next ? L.NextRun(_times.DateTime(next)) : L.NoNextRun;
        LastOutcomeText = summary.LastOutcome is { } outcome ? L.OutcomeText(outcome) : null;
        RunningText = summary.ActiveRun is { } run ? L.StateText(run.State) : string.Empty;

        var service = summary.Kind == ProcessKind.Service;
        CancelText = service ? L.StopService : L.CancelRun;
        RunNowAutomationName = L.RunNowFor(summary.Name);
        CancelAutomationName = service ? L.StopServiceFor(summary.Name) : L.CancelRunFor(summary.Name);
        EnableAutomationName = L.EnableFor(summary.Name);
        DisableAutomationName = L.DisableFor(summary.Name);
        UnblockAutomationName = L.UnblockFor(summary.Name);
        UnblockAndRunAutomationName = L.UnblockAndRunFor(summary.Name);
        DeleteAutomationName = L.DeleteFor(summary.Name);

        OnPropertyChanged(nameof(Lifecycle));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(ShowEnable));
        OnPropertyChanged(nameof(ShowDisable));
        OnPropertyChanged(nameof(ShowUnblock));
        OnPropertyChanged(nameof(ShowUnblockAndRun));
        OnPropertyChanged(nameof(ShowCancel));
        OnPropertyChanged(nameof(IsEnabledState));
        OnPropertyChanged(nameof(IsDisabledState));
        OnPropertyChanged(nameof(IsBlockedState));
    }

    /// <summary>Follows a change of what the user may do.</summary>
    public void RefreshPermissions()
    {
        RunNowCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        EnableCommand.NotifyCanExecuteChanged();
        DisableCommand.NotifyCanExecuteChanged();
        UnblockCommand.NotifyCanExecuteChanged();
        UnblockAndRunCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(DeniedReason));
    }
}
