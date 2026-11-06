using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.ViewModels;

/// <summary>
/// One run in full: its events, exit code, duration and the captured output tail. While the run is active it re-reads itself every
/// <see cref="RefreshInterval"/>; the timer stops when the run completes, on <see cref="Close"/> and on <see cref="Dispose"/>.
/// The timer fires on a pool thread, so its callback only posts to the UI dispatcher.
/// </summary>
public sealed partial class RunDetailViewModel : PageViewModel, IDisposable
{
    private readonly IServiceGateway _gateway;
    private readonly TimeProvider _time;
    private readonly TimeDisplay _times;
    private readonly IClipboardService _clipboard;
    private readonly IUiDispatcher _ui;
    private ITimer? _timer;
    private Guid _runId;
    private RunDetailsDto? _current;
    private int _version;
    private bool _refreshing;
    private bool _closed;
    private string? _copyError;

    public RunDetailViewModel(Localizer l, IServiceGateway gateway, TimeProvider time, TimeDisplay times, IClipboardService clipboard, IUiDispatcher ui)
        : base(l)
    {
        _gateway = gateway;
        _time = time;
        _times = times;
        _clipboard = clipboard;
        _ui = ui;
        l.PropertyChanged += OnLanguageChanged;
    }

    public static TimeSpan RefreshInterval { get; } = TimeSpan.FromSeconds(1);

    public ObservableCollection<string> Events { get; } = [];

    [ObservableProperty]
    public partial string ProcessName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string TriggerText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLate { get; private set; }

    [ObservableProperty]
    public partial string? StartedText { get; private set; }

    [ObservableProperty]
    public partial string? EndedText { get; private set; }

    [ObservableProperty]
    public partial string? DurationText { get; private set; }

    [ObservableProperty]
    public partial string? ExitCodeText { get; private set; }

    /// <summary>"Error" for a failure, "Reason" otherwise (a cancelled run is not an error).</summary>
    [ObservableProperty]
    public partial string ErrorLabel { get; private set; } = string.Empty;

    /// <summary>What the service recorded as the run's error or reason; <see cref="PageViewModel.ErrorText"/> stays the text of a failed call.</summary>
    [ObservableProperty]
    public partial string? RunErrorText { get; private set; }

    /// <summary>The captured output tail, exactly as the service sent it (the re-adoption note line included).</summary>
    [ObservableProperty]
    public partial string Output { get; private set; } = string.Empty;

    /// <summary>
    /// <see cref="Output"/> split into lines, for a virtualized list: a mebibyte of tail in one text box is laid out again on every
    /// change. Rebuilt only when the output changed, so an unchanged refresh keeps the same list instance.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> OutputLines { get; private set; } = [];

    [ObservableProperty]
    public partial string? OutputNote { get; private set; }

    [ObservableProperty]
    public partial bool IsActive { get; private set; }

    /// <summary>True until the first read has answered (or failed).</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    public bool HasRunError => !string.IsNullOrEmpty(RunErrorText);

    public bool HasEvents => Events.Count > 0;

    public bool HasData => _current is not null;

    public async Task LoadAsync(Guid runId)
    {
        StopTimer();
        _closed = false;
        _runId = runId;
        var version = ++_version;
        IsLoading = true;
        try
        {
            _ = await TryAsync(async () => Apply(await _gateway.GetRunAsync(runId).ConfigureAwait(true), version)).ConfigureAwait(true);
        }
        finally
        {
            if (version == _version)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>Stops the refresh; the view model keeps what it shows.</summary>
    public void Close()
    {
        _closed = true;
        _version++;
        StopTimer();
    }

    public void Dispose()
    {
        Close();
        L.PropertyChanged -= OnLanguageChanged;
    }

    private void Apply(RunDetailsDto details, int version)
    {
        // A result that arrives after Close, or after another run was loaded, is dropped.
        if (_closed || version != _version)
        {
            return;
        }

        _current = details;
        Rebuild(details);
        OnPropertyChanged(nameof(HasData));
        if (IsActive)
        {
            EnsureTimer();
        }
        else
        {
            StopTimer();
        }
    }

    private void Rebuild(RunDetailsDto details)
    {
        RunRowDto run = details.Run;
        ProcessName = run.ProcessName;
        IsActive = run.State != RunState.Completed;
        StatusText = !IsActive && run.Outcome is { } outcome ? L.OutcomeText(outcome) : L.StateText(run.State);
        TriggerText = L.TriggerText(run.Trigger);
        IsLate = run.IsLate;
        StartedText = run.StartedAt is { } started ? _times.DateTime(started) : null;
        EndedText = run.EndedAt is { } ended ? _times.DateTime(ended) : null;
        DurationText = run.StartedAt is { } start
            ? Durations.Format((run.EndedAt ?? _time.GetUtcNow()) - start, L)
            : null;
        ExitCodeText = run.ExitCode?.ToString(L.Culture);

        var failure = run.Outcome is { } o && (RunOutcomes.Failures.Contains(o) || o == RunOutcome.FailedToStart);
        ErrorLabel = failure ? L.RunError : L.RunReason;
        RunErrorText = details.Error;
        OnPropertyChanged(nameof(HasRunError));

        SetEvents([.. details.Entries.Select(EntryText)]);

        // The tail can be a mebibyte and this runs every second: assigning the same text would still re-measure and re-lay it out.
        if (!string.Equals(Output, details.Output.Text, StringComparison.Ordinal))
        {
            Output = details.Output.Text;
            OutputLines = SplitLines(details.Output.Text);
            CopyOutputCommand.NotifyCanExecuteChanged();
        }

        OutputNote = !details.Output.Exists ? L.NoOutput : details.Output.Truncated ? L.OutputTruncated : null;
    }

    /// <summary>Splits on CRLF or LF; a final line break does not add an empty line.</summary>
    internal static string[] SplitLines(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var lines = text.Split('\n');
        var count = lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;
        var result = new string[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = lines[i].EndsWith('\r') ? lines[i][..^1] : lines[i];
        }

        return result;
    }

    private bool CanCopyOutput => Output.Length > 0;

    [RelayCommand(CanExecute = nameof(CanCopyOutput))]
    private async Task CopyOutputAsync()
    {
        try
        {
            await _clipboard.SetTextAsync(Output).ConfigureAwait(true);

            // Only the copy failure is the copy's to clear; a poll error that is on screen stays.
            if (_copyError is not null && string.Equals(ErrorText, _copyError, StringComparison.Ordinal))
            {
                ErrorText = null;
            }

            _copyError = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _copyError = L.ErrorUnexpected(ex.Message);
            ErrorText = _copyError;
        }
    }

    private string EntryText(HistoryEntryDto entry)
    {
        var text = $"{_times.DateTime(entry.OccurredAt)}  {L.EventText(entry.Kind)}";
        return string.IsNullOrEmpty(entry.Message) ? text : $"{text} — {entry.Message}";
    }

    private void SetEvents(List<string> events)
    {
        if (!Events.SequenceEqual(events, StringComparer.Ordinal))
        {
            Events.Clear();
            foreach (var text in events)
            {
                Events.Add(text);
            }
        }

        OnPropertyChanged(nameof(HasEvents));
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_current is { } current)
        {
            Rebuild(current);
        }
    }

    private void EnsureTimer() => _timer ??= _time.CreateTimer(OnTick, null, RefreshInterval, RefreshInterval);

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    // Runs on a timer thread: touch nothing but the dispatcher here.
    private void OnTick(object? state) => _ui.Post(() => _ = RefreshAsync());

    private async Task RefreshAsync()
    {
        if (_closed || !IsActive || _refreshing)
        {
            return;
        }

        _refreshing = true;
        var version = _version;
        try
        {
            _ = await TryAsync(async () => Apply(await _gateway.GetRunAsync(_runId).ConfigureAwait(true), version)).ConfigureAwait(true);
        }
        finally
        {
            _refreshing = false;
        }
    }
}
