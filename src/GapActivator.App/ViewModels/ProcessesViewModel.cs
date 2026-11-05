using System.Collections.ObjectModel;
using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.ViewModels;

/// <summary>The process list: one row per registered process, ordered by name, with the actions of each.</summary>
public sealed class ProcessesViewModel : PageViewModel
{
    private readonly LiveState _live;
    private readonly Permissions _permissions;
    private readonly TimeDisplay _times;
    private readonly LiveFreshness _freshness;

    // The editor (a later task) opens a process from here and marks its failures as seen.
    private readonly FailureAcknowledgements _seen;
    private readonly INavigator _navigator;

    public ProcessesViewModel(
        Localizer l,
        IServiceGateway gateway,
        LiveState live,
        Permissions permissions,
        ProcessActions actions,
        RunActions runs,
        FailureAcknowledgements seen,
        INavigator navigator,
        TimeDisplay times)
        : base(l)
    {
        _live = live;
        _permissions = permissions;
        Actions = actions;
        Runs = runs;
        _seen = seen;
        _navigator = navigator;
        _times = times;
        _freshness = new LiveFreshness(l, gateway, live);

        live.Changed += (_, _) => Rebuild();
        gateway.ConnectionChanged += (_, _) => NotifyFreshness();
        permissions.PropertyChanged += (_, _) =>
        {
            foreach (ProcessRowViewModel row in Items)
            {
                row.RefreshPermissions();
            }
        };

        // Cached texts (the rows, the formatted times) are rebuilt when the language changes.
        l.PropertyChanged += (_, _) =>
        {
            foreach (ProcessRowViewModel row in Items)
            {
                row.Update(row.Summary);
            }

            NotifyFreshness();
        };
        Rebuild();
    }

    public ObservableCollection<ProcessRowViewModel> Items { get; } = [];

    internal ProcessActions Actions { get; }

    internal RunActions Runs { get; }

    public Permissions Permissions => _permissions;

    public bool IsConnected => _freshness.IsConnected;

    public bool HasData => _freshness.HasData;

    /// <summary>True while the list shows the last known state because the service is not connected; the view says so and dims it.</summary>
    public bool IsStale => _freshness.IsStale;

    public string StaleText => _freshness.StaleText;

    public bool HasRefreshError => _freshness.HasRefreshError;

    public string RefreshErrorText => _freshness.RefreshErrorText;

    public bool ShowWaiting => _freshness.ShowWaiting;

    /// <summary>True when the service has reported and has no process; never before the first answer.</summary>
    public bool ShowEmpty => HasData && Items.Count == 0;

    internal Task<bool> RunAsync(Func<Task> action) => TryAsync(action);

    internal async Task<bool> DeleteAsync(ProcessRowViewModel row) =>
        await TryAsync(async () =>
        {
            if (await Actions.DeleteAsync(row.Summary).ConfigureAwait(true) == DeleteResult.StillRunning)
            {
                ErrorText = L.DeleteNotStopped;
            }
        }).ConfigureAwait(true);

    private void NotifyFreshness()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(IsStale));
        OnPropertyChanged(nameof(StaleText));
        OnPropertyChanged(nameof(HasRefreshError));
        OnPropertyChanged(nameof(RefreshErrorText));
        OnPropertyChanged(nameof(ShowWaiting));
        OnPropertyChanged(nameof(ShowEmpty));
    }

    /// <summary>Brings <see cref="Items"/> in line with the live state, keeping the instance of every process that stays.</summary>
    private void Rebuild()
    {
        List<ProcessSummaryDto> wanted = [.. _live.Processes.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)];
        var ids = wanted.Select(p => p.Id).ToHashSet();
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!ids.Contains(Items[i].Id))
            {
                Items.RemoveAt(i);
            }
        }

        for (var index = 0; index < wanted.Count; index++)
        {
            ProcessSummaryDto summary = wanted[index];
            var existing = -1;
            for (var i = index; i < Items.Count; i++)
            {
                if (Items[i].Id == summary.Id)
                {
                    existing = i;
                    break;
                }
            }

            if (existing < 0)
            {
                Items.Insert(index, new ProcessRowViewModel(summary, L, _times, _permissions, this));
                continue;
            }

            if (existing != index)
            {
                Items.Move(existing, index);
            }

            Items[index].Update(summary);
        }

        NotifyFreshness();
    }
}
