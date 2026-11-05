using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GapActivator.App.Localization;
using GapActivator.App.Scheduling;
using GapActivator.App.Services;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.ViewModels;

/// <summary>An entry of the process filter; <see cref="Id"/> is null for "All processes".</summary>
public sealed record ProcessOption(Guid? Id, string Name);

/// <summary>An entry of the outcome filter; <see cref="Outcomes"/> is null for "All outcomes".</summary>
public sealed record OutcomeOption(string Name, IReadOnlyList<RunOutcome>? Outcomes);

/// <summary>
/// The run history: filters for process, outcome and dates, fifty runs per page (newest first, as the service orders them), and the
/// detail of one run in place of the list. Paging and refreshes use the filter that was last applied, not what is typed in the fields.
/// </summary>
public sealed partial class HistoryViewModel : PageViewModel, IFreshnessNotes
{
    public const int PageSize = 50;

    private readonly IServiceGateway _gateway;
    private readonly LiveState _live;
    private readonly FailureAcknowledgements _seen;
    private readonly TimeDisplay _times;
    private readonly TimeZoneInfo _zone;
    private readonly Func<RunDetailViewModel> _details;

    private AppliedFilter _applied = new(null, null, null, null);
    private ProcessOption _selectedProcess = null!;
    private OutcomeOption _selectedOutcome = null!;
    private bool _rebuilding;
    private bool _opened;
    private bool _userLoad;
    private int _loadVersion;
    private int _totalCount;

    public HistoryViewModel(
        Localizer l,
        IServiceGateway gateway,
        LiveState live,
        FailureAcknowledgements seen,
        INavigator navigator,
        TimeDisplay times,
        TimeZoneInfo zone,
        Func<RunDetailViewModel> details)
        : base(l)
    {
        _gateway = gateway;
        _live = live;
        _seen = seen;
        _times = times;
        _zone = zone;
        _details = details;

        RebuildOutcomeOptions();
        RebuildProcessOptions();

        live.Changed += (_, _) => RebuildProcessOptions();
        gateway.ConnectionChanged += (_, status) =>
        {
            NotifyFreshness();
            if (status.IsConnected && _opened)
            {
                Refreshing = LoadAsync(Page, userInitiated: false);
            }
        };
        gateway.StateChanged += (_, _) =>
        {
            if (HasLoaded && Page == 1)
            {
                Refreshing = LoadAsync(1, userInitiated: false);
            }
        };
        navigator.Requested += (_, request) => OnRequested(request);

        // Cached texts (option names, rows, the page text) are rebuilt when the language changes.
        l.PropertyChanged += (_, _) =>
        {
            RebuildOutcomeOptions();
            RebuildProcessOptions();
            foreach (RunRowItemViewModel row in Items)
            {
                row.Refresh();
            }

            OnPropertyChanged(nameof(PageText));
            NotifyFreshness();
        };
    }

    public ObservableCollection<ProcessOption> ProcessOptions { get; } = [];

    public ObservableCollection<OutcomeOption> OutcomeOptions { get; } = [];

    public ObservableCollection<RunRowItemViewModel> Items { get; } = [];

    public ProcessOption SelectedProcess
    {
        get => _selectedProcess;
        set
        {
            // A combo box deselects while its items change; keep the selection and push it back.
            if (value is null || _rebuilding || ReferenceEquals(value, _selectedProcess))
            {
                OnPropertyChanged();
                return;
            }

            _ = SetProperty(ref _selectedProcess, value);
        }
    }

    public OutcomeOption SelectedOutcome
    {
        get => _selectedOutcome;
        set
        {
            if (value is null || _rebuilding || ReferenceEquals(value, _selectedOutcome))
            {
                OnPropertyChanged();
                return;
            }

            _ = SetProperty(ref _selectedOutcome, value);
        }
    }

    [ObservableProperty]
    public partial DateTime? FromDate { get; set; }

    [ObservableProperty]
    public partial DateTime? ToDate { get; set; }

    [ObservableProperty]
    public partial int Page { get; private set; } = 1;

    [ObservableProperty]
    public partial int PageCount { get; private set; } = 1;

    /// <summary>The run shown in place of the list, or null.</summary>
    [ObservableProperty]
    public partial RunDetailViewModel? Detail { get; private set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>True after the first page has been read; before that the list would claim "no runs" when the answer is "not asked yet".</summary>
    [ObservableProperty]
    public partial bool HasLoaded { get; private set; }

    public string PageText => L.PageOf(Page, PageCount);

    public bool IsConnected => _gateway.Status.IsConnected;

    public bool HasData => HasLoaded;

    /// <summary>True while the page shows the last runs it read because the service is not connected.</summary>
    public bool IsStale => HasLoaded && !IsConnected;

    public string StaleText => L.StaleNote;

    /// <summary>The service-state refresh is the dashboard's; a failed page read shows as <see cref="PageViewModel.ErrorText"/>.</summary>
    public bool HasRefreshError => false;

    public string RefreshErrorText => L.RefreshFailedText;

    /// <summary>Shown while the first page is being read.</summary>
    public bool ShowWaiting => !HasLoaded && IsConnected && ErrorText is null;

    public string WaitingText => L.LoadingRuns;

    /// <summary>Shown while a filter or a page change the user asked for is being read, so old rows are not mistaken for the answer.</summary>
    public string? ProgressText => IsLoading && HasLoaded && _userLoad ? L.LoadingRuns : null;

    public bool ShowEmpty => HasLoaded && Items.Count == 0;

    public bool ShowPaging => HasLoaded && _totalCount > 0;

    /// <summary>The page read that the last navigation started; tests await it, the screen does not need to.</summary>
    internal Task Refreshing { get; private set; } = Task.CompletedTask;

    /// <summary>The read of the run the last navigation request opened.</summary>
    internal Task OpeningDetail { get; private set; } = Task.CompletedTask;

    private bool CanPrevious => Page > 1;

    private bool CanNext => Page < PageCount;

    [RelayCommand]
    private async Task ApplyAsync()
    {
        DateTime? from = FromDate?.Date;
        DateTime? to = ToDate?.Date;
        if (from is { } f && to is { } t && f > t)
        {
            ErrorText = L.FilterDatesInverted;
            return;
        }

        _applied = new AppliedFilter(
            SelectedProcess.Id,
            SelectedOutcome.Outcomes,
            from is { } start ? LocalTime.ToOffset(start, _zone) : null,
            to is { } end ? LocalTime.ToOffset(end.AddDays(1), _zone) : null);
        await LoadAsync(1, userInitiated: true).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanNext))]
    private Task NextPageAsync() => LoadAsync(Page + 1, userInitiated: true);

    [RelayCommand(CanExecute = nameof(CanPrevious))]
    private Task PreviousPageAsync() => LoadAsync(Page - 1, userInitiated: true);

    [RelayCommand]
    private Task OpenRunAsync(RunRowItemViewModel? row) => row is null ? Task.CompletedTask : OpenDetailAsync(row.Id);

    [RelayCommand]
    private void CloseDetail()
    {
        RunDetailViewModel? detail = Detail;
        if (detail is null)
        {
            return;
        }

        Detail = null;
        detail.Dispose();
    }

    private void OnRequested(NavigationRequest request)
    {
        if (request.Page != AppPage.History)
        {
            // Leaving the page ends the live read of an open run.
            CloseDetail();
            return;
        }

        _opened = true;

        // Opening the history counts as having seen the failures that came before.
        _seen.MarkHistorySeen();
        if (_gateway.Status.IsConnected)
        {
            Refreshing = LoadAsync(Page, userInitiated: false);
        }

        if (request.Id is { } runId)
        {
            OpeningDetail = OpenDetailAsync(runId);
        }
    }

    private async Task OpenDetailAsync(Guid runId)
    {
        CloseDetail();
        RunDetailViewModel detail = _details();
        Detail = detail;
        await detail.LoadAsync(runId).ConfigureAwait(true);
    }

    private async Task LoadAsync(int page, bool userInitiated)
    {
        var version = ++_loadVersion;
        _userLoad = userInitiated;
        IsLoading = true;
        NotifyFreshness();

        RunRowPageDto? result = null;
        var filter = new RunFilterDto(_applied.ProcessId, _applied.Outcomes, _applied.From, _applied.To, page, PageSize);
        var ok = await TryAsync(async () => result = await _gateway.ListRunsAsync(filter).ConfigureAwait(true)).ConfigureAwait(true);

        // Only the latest read counts: an older answer that arrives late must not replace a newer one.
        if (version != _loadVersion)
        {
            return;
        }

        IsLoading = false;
        if (ok && result is not null)
        {
            _totalCount = result.TotalCount;
            PageCount = Math.Max(1, (int)Math.Ceiling(result.TotalCount / (double)PageSize));
            Page = page;
            Reconcile(result.Items);
            HasLoaded = true;
            OnPropertyChanged(nameof(PageText));
            PreviousPageCommand.NotifyCanExecuteChanged();
            NextPageCommand.NotifyCanExecuteChanged();
        }

        NotifyFreshness();
    }

    /// <summary>Brings <see cref="Items"/> in line with the page, keeping the instance of every run that stays so a focused row keeps its focus.</summary>
    private void Reconcile(IReadOnlyList<RunRowDto> runs)
    {
        var wanted = runs.Select(r => r.Id).ToHashSet();
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(Items[i].Id))
            {
                Items.RemoveAt(i);
            }
        }

        for (var index = 0; index < runs.Count; index++)
        {
            RunRowDto run = runs[index];
            var existing = -1;
            for (var i = index; i < Items.Count; i++)
            {
                if (Items[i].Id == run.Id)
                {
                    existing = i;
                    break;
                }
            }

            if (existing < 0)
            {
                Items.Insert(index, new RunRowItemViewModel(run, L, _times, row => _ = OpenRunCommand.ExecuteAsync(row)));
                continue;
            }

            if (existing != index)
            {
                Items.Move(existing, index);
            }

            Items[index].Update(run);
        }
    }

    private void RebuildProcessOptions()
    {
        Guid? keep = _selectedProcess?.Id;
        _rebuilding = true;
        try
        {
            ProcessOptions.Clear();
            ProcessOptions.Add(new ProcessOption(null, L.FilterAllProcesses));
            foreach (ProcessSummaryDto process in _live.Processes.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                ProcessOptions.Add(new ProcessOption(process.Id, process.Name));
            }

            _selectedProcess = ProcessOptions.FirstOrDefault(o => o.Id == keep) ?? ProcessOptions[0];
        }
        finally
        {
            _rebuilding = false;
        }

        OnPropertyChanged(nameof(SelectedProcess));
    }

    private void RebuildOutcomeOptions()
    {
        var keep = _selectedOutcome is null ? 0 : Math.Max(0, OutcomeOptions.IndexOf(_selectedOutcome));
        _rebuilding = true;
        try
        {
            OutcomeOptions.Clear();
            OutcomeOptions.Add(new OutcomeOption(L.FilterAllOutcomes, null));
            OutcomeOptions.Add(new OutcomeOption(L.FilterFailures, RunOutcomes.Failures));
            foreach (RunOutcome outcome in Enum.GetValues<RunOutcome>())
            {
                OutcomeOptions.Add(new OutcomeOption(L.OutcomeText(outcome), [outcome]));
            }

            _selectedOutcome = OutcomeOptions[keep];
        }
        finally
        {
            _rebuilding = false;
        }

        OnPropertyChanged(nameof(SelectedOutcome));
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
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(ShowPaging));
    }

    private sealed record AppliedFilter(Guid? ProcessId, IReadOnlyList<RunOutcome>? Outcomes, DateTimeOffset? From, DateTimeOffset? To);
}
