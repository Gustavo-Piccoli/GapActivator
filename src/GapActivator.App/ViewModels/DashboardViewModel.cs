using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.ViewModels;

/// <summary>One upcoming run: the process and when it starts, already formatted.</summary>
public sealed record UpcomingItem(string ProcessName, string AtText);

/// <summary>The first page: the connection and the caller's rights, pause and resume, active runs, upcoming runs and recent failures.</summary>
public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly IServiceGateway _gateway;
    private readonly LiveState _live;
    private readonly RunActions _runs;
    private readonly FailureAcknowledgements _seen;
    private readonly INavigator _navigator;
    private readonly TimeDisplay _times;

    public DashboardViewModel(
        Localizer l,
        IServiceGateway gateway,
        LiveState live,
        Permissions permissions,
        RunActions runs,
        FailureAcknowledgements seen,
        INavigator navigator,
        TimeDisplay times)
        : base(l)
    {
        _gateway = gateway;
        _live = live;
        Permissions = permissions;
        _runs = runs;
        _seen = seen;
        _navigator = navigator;
        _times = times;

        live.Changed += (_, _) => Rebuild();
        seen.Changed += (_, _) =>
        {
            foreach (FailureItemViewModel failure in RecentFailures)
            {
                failure.RefreshSeen();
            }
        };
        gateway.ConnectionChanged += (_, _) => RefreshStatus();
        permissions.PropertyChanged += (_, _) => RefreshPermissions();

        // Cached texts (the items, the formatted times) are rebuilt when the language changes.
        l.PropertyChanged += (_, _) =>
        {
            RefreshStatus();
            Rebuild();
        };
        Rebuild();
    }

    public Permissions Permissions { get; }

    public ObservableCollection<ActiveRunItemViewModel> ActiveRuns { get; } = [];

    public ObservableCollection<UpcomingItem> Upcoming { get; } = [];

    public ObservableCollection<FailureItemViewModel> RecentFailures { get; } = [];

    /// <summary>"Connected", or the banner's explanation of why not.</summary>
    public string ConnectionText => ConnectionTexts.Banner(_gateway.Status, L) ?? L.Connected;

    public bool IsConnected => _gateway.Status.IsConnected;

    /// <summary>A check mark when connected, a warning sign otherwise: the state never rests on color alone.</summary>
    public string ConnectionGlyph => IsConnected ? "" : "";

    public string? CallerText => _gateway.Status is { IsConnected: true, Caller: { } caller }
        ? (caller.CanChange ? L.CallerCanChange(caller.Name) : L.CallerViewOnly(caller.Name))
        : null;

    public string? OperatorsModeText => _gateway.Status is { IsConnected: true, OperatorsMode: { } mode } ? L.OperatorsModeText(mode) : null;

    public bool IsPaused => _live.Dashboard?.IsPaused == true;

    public string PausedText => L.PausedText;

    /// <summary>False until the service has reported once; before that the lists would claim "nothing" when the answer is "unknown".</summary>
    public bool HasData => _live.Dashboard is not null;

    public bool HasActiveRuns => ActiveRuns.Count > 0;

    public bool HasUpcoming => Upcoming.Count > 0;

    public bool HasRecentFailures => RecentFailures.Count > 0;

    private bool CanPause => Permissions.CanChange && !IsPaused;

    private bool CanResume => Permissions.CanChange && IsPaused;

    [RelayCommand(CanExecute = nameof(CanPause))]
    private async Task PauseAsync() => await TryAsync(() => _gateway.PauseInstallationAsync()).ConfigureAwait(true);

    [RelayCommand(CanExecute = nameof(CanResume))]
    private async Task ResumeAsync() => await TryAsync(() => _gateway.ResumeInstallationAsync()).ConfigureAwait(true);

    private async Task CancelAsync(ActiveRunItemViewModel run) =>
        _ = await TryAsync(() => _runs.CancelOrStopAsync(run.RunId, run.ProcessId, run.ProcessName, run.Kind)).ConfigureAwait(true);

    private void RefreshStatus()
    {
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ConnectionGlyph));
        OnPropertyChanged(nameof(CallerText));
        OnPropertyChanged(nameof(OperatorsModeText));
        OnPropertyChanged(nameof(PausedText));
        RefreshPermissions();
    }

    private void RefreshPermissions()
    {
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        foreach (ActiveRunItemViewModel run in ActiveRuns)
        {
            run.RefreshPermissions();
        }
    }

    private void Rebuild()
    {
        DashboardDto? dashboard = _live.Dashboard;

        Reconcile(
            ActiveRuns,
            dashboard?.ActiveRuns ?? [],
            item => item.RunId,
            run => new ActiveRunItemViewModel(run, L, _times, Permissions, CancelAsync),
            (item, run) => item.Update(run));
        Reconcile(
            RecentFailures,
            dashboard?.RecentFailures ?? [],
            item => item.RunId,
            run => new FailureItemViewModel(run, L, _times, _seen, _navigator),
            (item, run) => item.Update(run));

        Upcoming.Clear();
        foreach (UpcomingRunDto next in dashboard?.Upcoming ?? [])
        {
            Upcoming.Add(new UpcomingItem(next.ProcessName, _times.WithWeekday(next.At)));
        }

        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(HasActiveRuns));
        OnPropertyChanged(nameof(HasUpcoming));
        OnPropertyChanged(nameof(HasRecentFailures));
        OnPropertyChanged(nameof(IsPaused));
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Brings <paramref name="items"/> in line with <paramref name="runs"/> while keeping the instance of every run that stays,
    /// so a button the user tabbed to keeps its focus across refreshes.
    /// </summary>
    private static void Reconcile<TItem>(
        ObservableCollection<TItem> items,
        IReadOnlyList<RunRowDto> runs,
        Func<TItem, Guid> key,
        Func<RunRowDto, TItem> create,
        Action<TItem, RunRowDto> update)
    {
        var wanted = runs.Select(r => r.Id).ToHashSet();
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(key(items[i])))
            {
                items.RemoveAt(i);
            }
        }

        for (var index = 0; index < runs.Count; index++)
        {
            RunRowDto run = runs[index];
            var existing = -1;
            for (var i = index; i < items.Count; i++)
            {
                if (key(items[i]) == run.Id)
                {
                    existing = i;
                    break;
                }
            }

            if (existing < 0)
            {
                items.Insert(index, create(run));
                continue;
            }

            if (existing != index)
            {
                items.Move(existing, index);
            }

            update(items[index], run);
        }
    }
}
