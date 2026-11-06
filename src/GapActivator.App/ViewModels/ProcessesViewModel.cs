using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.ViewModels;

/// <summary>The process list: one row per registered process, ordered by name, with the actions of each.</summary>
public sealed partial class ProcessesViewModel : PageViewModel, IFreshnessNotes
{
    private readonly LiveState _live;
    private readonly Permissions _permissions;
    private readonly TimeDisplay _times;
    private readonly LiveFreshness _freshness;

    private readonly Func<ProcessEditorViewModel> _editors;

    public ProcessesViewModel(
        Localizer l,
        IServiceGateway gateway,
        LiveState live,
        Permissions permissions,
        ProcessActions actions,
        RunActions runs,
        INavigator navigator,
        TimeDisplay times,
        Func<ProcessEditorViewModel> editors)
        : base(l)
    {
        _live = live;
        _permissions = permissions;
        Actions = actions;
        Runs = runs;
        _editors = editors;
        _times = times;
        _freshness = new LiveFreshness(l, gateway, live);

        NewProcessCommand = new RelayCommand(OpenNewEditor, () => permissions.CanChange);
        navigator.Requested += (_, request) =>
        {
            if (request.Page == AppPage.Processes && request.Id is { } id)
            {
                OpeningEditor = OpenEditorAsync(id);
            }
        };

        live.Changed += (_, _) => Rebuild();
        gateway.ConnectionChanged += (_, _) => NotifyFreshness();
        permissions.PropertyChanged += (_, _) =>
        {
            NewProcessCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(NewDeniedReason));
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

            OnPropertyChanged(nameof(ProgressText));
            NotifyFreshness();
        };
        Rebuild();
    }

    public ObservableCollection<ProcessRowViewModel> Items { get; } = [];

    /// <summary>The editor shown instead of the list, or null.</summary>
    [ObservableProperty]
    public partial ProcessEditorViewModel? Editor { get; private set; }

    /// <summary>What the last saved editor wanted the user to know ("also used by"); shown above the list.</summary>
    [ObservableProperty]
    public partial string? NoticeText { get; private set; }

    /// <summary>The read of the process the last navigation request opened; tests await it, the screen does not need to.</summary>
    internal Task OpeningEditor { get; private set; } = Task.CompletedTask;

    public IRelayCommand NewProcessCommand { get; }

    public string? NewDeniedReason => _permissions.DeniedReason;

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

    public string WaitingText => L.WaitingForService;

    /// <summary>True when the service has reported and has no process; never before the first answer.</summary>
    public bool ShowEmpty => HasData && Items.Count == 0;

    // The name of the process being deleted while its run is stopped; the text is built from it, so it follows a language switch.
    private string? _stoppingName;

    /// <summary>Shown while a delete waits for the run to stop (up to a minute); null otherwise.</summary>
    public string? ProgressText => _stoppingName is null ? null : L.DeleteStopping(_stoppingName);

    internal Task<bool> RunAsync(Func<Task> action) => TryAsync(action);

    internal async Task<bool> DeleteAsync(ProcessRowViewModel row)
    {
        // The lambda only captures the result: TryAsync clears the error after a call that worked, so a text set inside it
        // (the same one a failed retry left on screen) would be wiped as "unchanged".
        var stillRunning = false;
        var ok = await TryAsync(async () =>
        {
            try
            {
                stillRunning = await Actions.DeleteAsync(row.Summary, () => SetStopping(row.Summary.Name)).ConfigureAwait(true) == DeleteResult.StillRunning;
            }
            finally
            {
                SetStopping(null);
            }
        }).ConfigureAwait(true);

        if (ok && stillRunning)
        {
            ErrorText = L.DeleteNotStopped;
        }

        return ok;
    }

    /// <summary>Opens the editor for the process and reads it from the service; the form is on screen while it loads.</summary>
    internal async Task OpenEditorAsync(Guid processId)
    {
        ProcessEditorViewModel editor = ShowEditor();
        await editor.LoadAsync(processId).ConfigureAwait(true);
    }

    private void OpenNewEditor() => ShowEditor().LoadNew();

    private ProcessEditorViewModel ShowEditor()
    {
        CloseEditor();
        NoticeText = null;
        ProcessEditorViewModel editor = _editors();
        editor.Closed += OnEditorClosed;
        Editor = editor;
        return editor;
    }

    private void CloseEditor()
    {
        ProcessEditorViewModel? editor = Editor;
        if (editor is null)
        {
            return;
        }

        editor.Closed -= OnEditorClosed;
        Editor = null;
        editor.Dispose();
    }

    private void OnEditorClosed(object? sender, bool saved)
    {
        if (sender is ProcessEditorViewModel editor)
        {
            NoticeText = saved ? editor.NoticeText : null;
        }

        CloseEditor();
    }

    private void SetStopping(string? name)
    {
        _stoppingName = name;
        OnPropertyChanged(nameof(ProgressText));
    }

    private void NotifyFreshness()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(IsStale));
        OnPropertyChanged(nameof(StaleText));
        OnPropertyChanged(nameof(HasRefreshError));
        OnPropertyChanged(nameof(RefreshErrorText));
        OnPropertyChanged(nameof(ShowWaiting));
        OnPropertyChanged(nameof(WaitingText));
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
