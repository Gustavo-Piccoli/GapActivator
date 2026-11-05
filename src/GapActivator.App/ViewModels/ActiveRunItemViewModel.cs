using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.ViewModels;

/// <summary>One active run on the dashboard. The dashboard keeps the instance while the run stays active and calls <see cref="Update"/>.</summary>
public sealed partial class ActiveRunItemViewModel : ViewModelBase
{
    private readonly TimeDisplay _times;
    private readonly Permissions _permissions;

    public ActiveRunItemViewModel(RunRowDto run, Localizer l, TimeDisplay times, Permissions permissions, Func<ActiveRunItemViewModel, Task> cancel)
        : base(l)
    {
        _times = times;
        _permissions = permissions;
        RunId = run.Id;
        CancelCommand = new AsyncRelayCommand(() => cancel(this), () => _permissions.CanChange);
        Update(run);
    }

    public Guid RunId { get; }

    public Guid ProcessId { get; private set; }

    public ProcessKind Kind { get; private set; }

    [ObservableProperty]
    public partial string ProcessName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string StateText { get; private set; } = string.Empty;

    /// <summary>When the run started, or empty while it is still launching.</summary>
    [ObservableProperty]
    public partial string StartedText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ActionText { get; private set; } = string.Empty;

    /// <summary>The cancel button's accessible name: the action and the process, because every row says the same word.</summary>
    [ObservableProperty]
    public partial string ActionAutomationName { get; private set; } = string.Empty;

    public IAsyncRelayCommand CancelCommand { get; }

    /// <summary>Why the user cannot cancel; null when they can.</summary>
    public string? DeniedReason => _permissions.DeniedReason;

    /// <summary>Recomputes every text and the capture of the run's data; also the way to follow a language switch.</summary>
    public void Update(RunRowDto run)
    {
        ArgumentNullException.ThrowIfNull(run);
        ProcessId = run.ProcessId;
        Kind = run.Kind;
        ProcessName = run.ProcessName;
        StateText = L.StateText(run.State);
        StartedText = run.StartedAt is { } at ? _times.DateTime(at) : string.Empty;
        var service = run.Kind == ProcessKind.Service;
        ActionText = service ? L.StopService : L.CancelRun;
        ActionAutomationName = service ? L.StopServiceFor(run.ProcessName) : L.CancelRunFor(run.ProcessName);
    }

    /// <summary>Follows a change of what the user may do.</summary>
    public void RefreshPermissions()
    {
        CancelCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(DeniedReason));
    }
}
