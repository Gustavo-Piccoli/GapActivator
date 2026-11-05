using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.ViewModels;

/// <summary>One recent failure on the dashboard. Opening it shows the run in History.</summary>
public sealed partial class FailureItemViewModel : ViewModelBase
{
    private readonly TimeDisplay _times;
    private readonly FailureAcknowledgements _seen;
    private RunRowDto _run;

    public FailureItemViewModel(RunRowDto run, Localizer l, TimeDisplay times, FailureAcknowledgements seen, INavigator navigator)
        : base(l)
    {
        _times = times;
        _seen = seen;
        _run = run;
        RunId = run.Id;
        OpenCommand = new RelayCommand(() => navigator.OpenRun(RunId));
        Update(run);
    }

    public Guid RunId { get; }

    public Guid ProcessId { get; private set; }

    [ObservableProperty]
    public partial string ProcessName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string OutcomeText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string EndedText { get; private set; } = string.Empty;

    /// <summary>True until the user has looked at the history or the process since this failure.</summary>
    [ObservableProperty]
    public partial bool IsUnseen { get; private set; }

    /// <summary>The row's accessible name: it carries everything the row shows.</summary>
    [ObservableProperty]
    public partial string AutomationName { get; private set; } = string.Empty;

    public IRelayCommand OpenCommand { get; }

    /// <summary>Recomputes every text; also the way to follow a language switch or a change of the acknowledgements.</summary>
    public void Update(RunRowDto run)
    {
        ArgumentNullException.ThrowIfNull(run);
        _run = run;
        ProcessId = run.ProcessId;
        ProcessName = run.ProcessName;
        OutcomeText = run.Outcome is { } outcome ? L.OutcomeText(outcome) : string.Empty;
        EndedText = _times.DateTime(run.EndedAt ?? run.CreatedAt);
        RefreshSeen();
        AutomationName = L.OpenRunFor(ProcessName, OutcomeText, EndedText);
    }

    /// <summary>Re-reads whether the failure is still unseen.</summary>
    public void RefreshSeen() => IsUnseen = _seen.IsUnseen(_run);
}
