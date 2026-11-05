using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.Ipc;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.ViewModels;

/// <summary>One entry of the type list; its text follows the language.</summary>
public sealed class KindOption(ProcessKind kind, Func<string> text) : ObservableObject
{
    public ProcessKind Kind { get; } = kind;

    public string Text => text();

    internal void Refresh() => OnPropertyChanged(nameof(Text));
}

/// <summary>
/// Creates or edits one process. Whatever fails (a refused save, a lost connection, a process deleted elsewhere) the typed values
/// stay where they are: the error is shown and saving works again as soon as the service answers. The editor owns the schedule
/// editors it opens and disposes them when the schedule is applied, discarded or removed and when the editor goes away.
/// </summary>
public sealed partial class ProcessEditorViewModel : PageViewModel, IDisposable
{
    private readonly IServiceGateway _gateway;
    private readonly Permissions _permissions;
    private readonly IDialogService _dialogs;
    private readonly FailureAcknowledgements _seen;
    private readonly TimeDisplay _times;
    private readonly TimeZoneInfo _zone;
    private readonly Func<ScheduleEditorViewModel> _scheduleEditors;
    private readonly IFilePicker _files;

    private Guid? _processId;
    private string _loadedName = string.Empty;
    private string _originalExecutable = string.Empty;
    private bool _sensitiveHidden;
    private ScheduleItemViewModel? _editingSchedule;
    private bool _disposed;

    public ProcessEditorViewModel(
        Localizer l,
        IServiceGateway gateway,
        Permissions permissions,
        IDialogService dialogs,
        FailureAcknowledgements seen,
        TimeDisplay times,
        TimeZoneInfo zone,
        Func<ScheduleEditorViewModel> scheduleEditors,
        IFilePicker files)
        : base(l)
    {
        _gateway = gateway;
        _permissions = permissions;
        _dialogs = dialogs;
        _seen = seen;
        _times = times;
        _zone = zone;
        _scheduleEditors = scheduleEditors;
        _files = files;

        KindOptions =
        [
            new KindOption(ProcessKind.RunToCompletion, () => L.KindText(ProcessKind.RunToCompletion)),
            new KindOption(ProcessKind.Service, () => L.KindText(ProcessKind.Service)),
        ];
        IsNew = true;

        SaveCommand = new AsyncRelayCommand(SaveAsync, CanSave);
        CloseCommand = new RelayCommand(() => Closed?.Invoke(this, false));
        AddVariableCommand = new RelayCommand(AddVariable, CanEdit);
        RemoveVariableCommand = new RelayCommand<EnvironmentVariableRowViewModel>(RemoveVariable, _ => CanEdit());
        AddScheduleCommand = new RelayCommand(() => OpenScheduleEditor(null), CanEdit);
        EditScheduleCommand = new RelayCommand<ScheduleItemViewModel>(item => OpenScheduleEditor(item), _ => CanEdit());
        RemoveScheduleCommand = new RelayCommand<ScheduleItemViewModel>(RemoveSchedule, _ => CanEdit());
        ApplyScheduleCommand = new RelayCommand(ApplySchedule, () => ScheduleEditor?.IsValid == true);
        CancelScheduleCommand = new RelayCommand(DisposeScheduleEditor);
        BrowseExecutableCommand = new AsyncRelayCommand(BrowseAsync, CanEdit);
        RetryLoadCommand = new AsyncRelayCommand(RetryLoadAsync, () => LoadFailed);

        Environment.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoVariables));
        Schedules.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoSchedules));
        _permissions.PropertyChanged += OnPermissionsChanged;
        L.PropertyChanged += OnLanguageChanged;
    }

    /// <summary>Raised when the editor wants to go away; true after a successful save.</summary>
    public event EventHandler<bool>? Closed;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsServiceKind), nameof(ShowSchedules), nameof(TimeLimitApplies), nameof(SelectedKindOption))]
    public partial ProcessKind Kind { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowVariableHint))]
    public partial string Executable { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Arguments { get; set; } = string.Empty;

    /// <summary>True for a viewer: the service did not send the arguments and the box shows the word for hidden.</summary>
    [ObservableProperty]
    public partial bool ArgumentsHidden { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowVariableHint))]
    public partial string WorkingDirectory { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal? TimeLimitSeconds { get; set; }

    [ObservableProperty]
    public partial decimal? GracePeriodSeconds { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TitleText))]
    public partial bool IsNew { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BusyText), nameof(IsLocked), nameof(IsUnlocked))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BusyText))]
    public partial bool IsSaving { get; private set; }

    /// <summary>True when an existing process could not be read; the form is empty and "Try again" reads it again.</summary>
    [ObservableProperty]
    public partial bool LoadFailed { get; private set; }

    /// <summary>True after a save that the service or the connection refused: the entries are still in the form.</summary>
    [ObservableProperty]
    public partial bool ShowKeptInput { get; private set; }

    [ObservableProperty]
    public partial string? NoticeText { get; private set; }

    /// <summary>The service's "a one-time schedule must be in the future" message, shown with the schedules it is about.</summary>
    [ObservableProperty]
    public partial string? ScheduleErrorText { get; private set; }

    [ObservableProperty]
    public partial ScheduleEditorViewModel? ScheduleEditor { get; private set; }

    public ObservableCollection<EnvironmentVariableRowViewModel> Environment { get; } = [];

    public ObservableCollection<ScheduleItemViewModel> Schedules { get; } = [];

    public ObservableCollection<KindOption> KindOptions { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public IRelayCommand CloseCommand { get; }

    public IRelayCommand AddVariableCommand { get; }

    public IRelayCommand<EnvironmentVariableRowViewModel> RemoveVariableCommand { get; }

    public IRelayCommand AddScheduleCommand { get; }

    public IRelayCommand<ScheduleItemViewModel> EditScheduleCommand { get; }

    public IRelayCommand<ScheduleItemViewModel> RemoveScheduleCommand { get; }

    public IRelayCommand ApplyScheduleCommand { get; }

    public IRelayCommand CancelScheduleCommand { get; }

    public IAsyncRelayCommand BrowseExecutableCommand { get; }

    public IAsyncRelayCommand RetryLoadCommand { get; }

    public bool IsReadOnly => !_permissions.CanChange;

    /// <summary>The text fields do not accept typing: the user cannot change things, or the process is still loading.</summary>
    public bool IsLocked => IsReadOnly || IsLoading;

    public bool IsServiceKind => Kind == ProcessKind.Service;

    public bool ShowSchedules => !IsServiceKind;

    /// <summary>The time limit does not apply to a service-kind process.</summary>
    public bool TimeLimitApplies => !IsServiceKind;

    public bool IsUnlocked => !IsLocked;

    public bool HasNoVariables => Environment.Count == 0;

    public bool HasNoSchedules => Schedules.Count == 0;

    public bool ShowVariableHint => ExecutableInput.HasVariablePattern(Executable) || ExecutableInput.HasVariablePattern(WorkingDirectory);

    public string? SensitiveNote => _sensitiveHidden ? L.SensitiveHiddenNote : null;

    /// <summary>Why saving is off for this user (the reason is also the tooltip of the disabled button); null when they can change things.</summary>
    public string? SaveDeniedReason => IsReadOnly ? _permissions.DeniedReason : null;

    public string TitleText => IsNew ? L.NewProcess : L.EditProcessTitle(_loadedName);

    /// <summary>"Loading…" or "Saving…" while the service is being asked; null otherwise.</summary>
    public string? BusyText => IsLoading ? L.EditorLoading : IsSaving ? L.EditorSaving : null;

    public KindOption SelectedKindOption
    {
        get => KindOptions.First(o => o.Kind == Kind);
        set
        {
            if (value is not null)
            {
                Kind = value.Kind;
            }
        }
    }

    /// <summary>Starts an empty form for a new process.</summary>
    public void LoadNew()
    {
        DisposeScheduleEditor();
        _processId = null;
        _loadedName = string.Empty;
        _originalExecutable = string.Empty;
        _sensitiveHidden = false;
        IsNew = true;
        LoadFailed = false;
        ArgumentsHidden = false;
        Name = string.Empty;
        Description = string.Empty;
        Kind = ProcessKind.RunToCompletion;
        Executable = string.Empty;
        Arguments = string.Empty;
        WorkingDirectory = string.Empty;
        TimeLimitSeconds = null;
        GracePeriodSeconds = null;
        Environment.Clear();
        Schedules.Clear();
        ClearMessages();
        OnPropertyChanged(nameof(SensitiveNote));
        RaiseCommandStates();
    }

    /// <summary>Reads the process from the service into the form and marks its failures as seen.</summary>
    public async Task LoadAsync(Guid id)
    {
        _seen.MarkProcessSeen(id);
        DisposeScheduleEditor();
        _processId = id;
        IsNew = false;
        LoadFailed = false;
        ClearMessages();
        IsLoading = true;
        RaiseCommandStates();
        try
        {
            ProcessDetailsDto? details = null;
            var ok = await TryAsync(async () => details = await _gateway.GetProcessAsync(id).ConfigureAwait(true)).ConfigureAwait(true);
            if (ok && details is not null)
            {
                Apply(details);
            }
            else
            {
                LoadFailed = true;
            }
        }
        finally
        {
            IsLoading = false;
            RaiseCommandStates();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _permissions.PropertyChanged -= OnPermissionsChanged;
        L.PropertyChanged -= OnLanguageChanged;
        DisposeScheduleEditor();
    }

    private bool CanEdit() => !IsReadOnly && !IsLoading;

    private bool CanSave() => CanEdit() && !_sensitiveHidden && !LoadFailed;

    private void ClearMessages()
    {
        ErrorText = null;
        NoticeText = null;
        ScheduleErrorText = null;
        ShowKeptInput = false;
    }

    private void RaiseCommandStates()
    {
        SaveCommand.NotifyCanExecuteChanged();
        AddVariableCommand.NotifyCanExecuteChanged();
        RemoveVariableCommand.NotifyCanExecuteChanged();
        AddScheduleCommand.NotifyCanExecuteChanged();
        EditScheduleCommand.NotifyCanExecuteChanged();
        RemoveScheduleCommand.NotifyCanExecuteChanged();
        BrowseExecutableCommand.NotifyCanExecuteChanged();
        RetryLoadCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(IsUnlocked));
        SyncRows();
    }

    private void SyncRows()
    {
        foreach (EnvironmentVariableRowViewModel row in Environment)
        {
            row.IsLocked = IsLocked;
        }
    }

    private void Apply(ProcessDetailsDto details)
    {
        _sensitiveHidden = details.SensitiveHidden;
        _loadedName = details.Summary.Name;
        _originalExecutable = details.Executable;
        Name = details.Summary.Name;
        Description = details.Description;
        Kind = details.Summary.Kind;
        Executable = details.Executable;
        ArgumentsHidden = details.SensitiveHidden;
        Arguments = details.SensitiveHidden ? L.ValueHidden : details.Arguments ?? string.Empty;
        WorkingDirectory = details.WorkingDirectory ?? string.Empty;
        TimeLimitSeconds = details.TimeLimit is { } limit ? (decimal)limit.TotalSeconds : null;
        GracePeriodSeconds = details.GracePeriod is { } grace ? (decimal)grace.TotalSeconds : null;

        Environment.Clear();
        foreach (EnvironmentVariableViewDto variable in details.Environment)
        {
            Environment.Add(details.SensitiveHidden
                ? new EnvironmentVariableRowViewModel(L, variable.Name, L.ValueHidden, variable.IsSecret, false, true)
                : variable.IsSecret && variable.Value is null
                    ? new EnvironmentVariableRowViewModel(L, variable.Name, string.Empty, true, true, false)
                    : new EnvironmentVariableRowViewModel(L, variable.Name, variable.Value ?? string.Empty, variable.IsSecret, false, false));
        }

        Schedules.Clear();
        foreach (ScheduleViewDto schedule in details.Schedules)
        {
            Schedules.Add(new ScheduleItemViewModel(schedule, ScheduleItemViewModel.ToInput(schedule), L, _times, _zone));
        }

        SyncRows();
        OnPropertyChanged(nameof(TitleText));
        OnPropertyChanged(nameof(SensitiveNote));
    }

    private void AddVariable() => Environment.Add(new EnvironmentVariableRowViewModel(L, string.Empty, string.Empty, false, false, false) { IsLocked = IsLocked });

    private void RemoveVariable(EnvironmentVariableRowViewModel? row)
    {
        if (row is not null)
        {
            Environment.Remove(row);
        }
    }

    private async Task BrowseAsync()
    {
        var picked = await _files.PickExecutableAsync().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(picked))
        {
            Executable = picked;
        }
    }

    private Task RetryLoadAsync() => _processId is { } id ? LoadAsync(id) : Task.CompletedTask;

    // ----- schedules

    private void OpenScheduleEditor(ScheduleItemViewModel? item)
    {
        DisposeScheduleEditor();
        ScheduleErrorText = null;
        ScheduleEditorViewModel editor = _scheduleEditors();
        editor.Load(item?.AsView());
        editor.PropertyChanged += OnScheduleEditorChanged;
        _editingSchedule = item;
        ScheduleEditor = editor;
        ApplyScheduleCommand.NotifyCanExecuteChanged();
    }

    private void ApplySchedule()
    {
        if (ScheduleEditor is not { IsValid: true } editor)
        {
            return;
        }

        ScheduleInputDto input = editor.Build(_editingSchedule?.Input.Id);
        var item = new ScheduleItemViewModel(_editingSchedule?.Original, input, L, _times, _zone);
        var index = _editingSchedule is null ? -1 : Schedules.IndexOf(_editingSchedule);
        if (index >= 0)
        {
            Schedules[index] = item;
        }
        else
        {
            Schedules.Add(item);
        }

        ScheduleErrorText = null;
        DisposeScheduleEditor();
    }

    private void RemoveSchedule(ScheduleItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (ReferenceEquals(item, _editingSchedule))
        {
            DisposeScheduleEditor();
        }

        Schedules.Remove(item);
        ScheduleErrorText = null;
    }

    private void DisposeScheduleEditor()
    {
        _editingSchedule = null;
        ScheduleEditorViewModel? editor = ScheduleEditor;
        if (editor is null)
        {
            return;
        }

        editor.PropertyChanged -= OnScheduleEditorChanged;
        ScheduleEditor = null;
        editor.Dispose();
        ApplyScheduleCommand.NotifyCanExecuteChanged();
    }

    private void OnScheduleEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ScheduleEditorViewModel.IsValid) or null or "")
        {
            ApplyScheduleCommand.NotifyCanExecuteChanged();
        }
    }

    // ----- save

    private string? Validate()
    {
        if (Name.Trim().Length == 0)
        {
            return L.RequiredName;
        }

        if (ExecutableInput.Normalize(Executable).Length == 0)
        {
            return L.RequiredExecutable;
        }

        foreach (EnvironmentVariableRowViewModel row in Environment)
        {
            if (row.Name.Trim().Length == 0)
            {
                return L.RequiredVariableName;
            }

            if (row.NeedsValue)
            {
                return L.RequiredVariableValue(row.Name.Trim());
            }
        }

        return null;
    }

    private async Task SaveAsync()
    {
        ClearMessages();
        if (ScheduleEditor is not null)
        {
            ErrorText = L.FinishScheduleFirst;
            return;
        }

        if (Validate() is { } problem)
        {
            ErrorText = problem;
            return;
        }

        var executable = ExecutableInput.Normalize(Executable);
        Executable = executable;
        IsSaving = true;
        try
        {
            ProcessSavedDto? saved = null;
            IReadOnlyList<string> shown = [];
            var scheduleInPast = false;
            var ok = await TryAsync(async () =>
            {
                if (IsNew || !string.Equals(executable, _originalExecutable, StringComparison.Ordinal))
                {
                    shown = await FindSharersAsync(executable).ConfigureAwait(true);
                    if (shown.Count > 0
                        && !await _dialogs.ConfirmAsync(L.SharedExecutableTitle, L.SharedExecutableQuestion(string.Join(", ", shown)), L.Save).ConfigureAwait(true))
                    {
                        return;
                    }
                }

                ProcessDefinitionDto definition = BuildDefinition(executable);
                try
                {
                    saved = _processId is { } id
                        ? await _gateway.UpdateProcessAsync(id, definition).ConfigureAwait(true)
                        : await _gateway.RegisterProcessAsync(definition).ConfigureAwait(true);
                }
                catch (GapActivatorRequestException ex) when (ex.Code == IpcErrorCodes.ScheduleInPast)
                {
                    scheduleInPast = true;
                    throw;
                }
            }).ConfigureAwait(true);

            if (!ok)
            {
                ShowKeptInput = true;
                if (scheduleInPast)
                {
                    ScheduleErrorText = ErrorText;
                    ErrorText = null;
                }

                return;
            }

            if (saved is null)
            {
                return;
            }

            var unseen = saved.ProcessesSharingExecutable.Where(n => !shown.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
            if (unseen.Count > 0)
            {
                NoticeText = L.SharedExecutableNote(string.Join(", ", unseen));
            }

            Closed?.Invoke(this, true);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task<IReadOnlyList<string>> FindSharersAsync(string executable)
    {
        List<string> names = [];
        IReadOnlyList<ProcessSummaryDto> all = await _gateway.ListProcessesAsync().ConfigureAwait(true);
        foreach (ProcessSummaryDto other in all)
        {
            if (other.Id == _processId)
            {
                continue;
            }

            ProcessDetailsDto details;
            try
            {
                details = await _gateway.GetProcessAsync(other.Id).ConfigureAwait(true);
            }
            catch (GapActivatorRequestException ex) when (ex.Code == IpcErrorCodes.NotFound)
            {
                // Deleted since the list was read: it shares nothing any more.
                continue;
            }

            if (string.Equals(details.Executable, executable, StringComparison.OrdinalIgnoreCase))
            {
                names.Add(other.Name);
            }
        }

        return names;
    }

    private ProcessDefinitionDto BuildDefinition(string executable) =>
        new(
            Name.Trim(),
            Description,
            Kind,
            executable,
            Arguments,
            string.IsNullOrWhiteSpace(WorkingDirectory) ? null : WorkingDirectory.Trim(),
            [.. Environment.Select(e => e.ToInput())],
            IsServiceKind ? null : Seconds(TimeLimitSeconds),
            Seconds(GracePeriodSeconds),
            IsServiceKind ? [] : [.. Schedules.Select(s => s.Input)]);

    private static TimeSpan? Seconds(decimal? value) => value is { } seconds ? TimeSpan.FromSeconds((double)seconds) : null;

    // ----- following the outside world

    private void OnPermissionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsReadOnly));
        OnPropertyChanged(nameof(SaveDeniedReason));
        RaiseCommandStates();
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ArgumentsHidden)
        {
            Arguments = L.ValueHidden;
        }

        foreach (EnvironmentVariableRowViewModel row in Environment)
        {
            row.Refresh();
        }

        foreach (ScheduleItemViewModel schedule in Schedules)
        {
            schedule.Refresh();
        }

        foreach (KindOption option in KindOptions)
        {
            option.Refresh();
        }

        OnPropertyChanged(nameof(TitleText));
        OnPropertyChanged(nameof(SensitiveNote));
        OnPropertyChanged(nameof(BusyText));
        OnPropertyChanged(nameof(SaveDeniedReason));
        OnPropertyChanged(string.Empty);
    }
}
