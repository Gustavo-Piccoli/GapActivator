using GapActivator.App.Localization;

namespace GapActivator.App.ViewModels;

/// <summary>
/// What the shared <c>FreshnessNotes</c> control shows above a page's data: the failed call, a refresh that failed, work in progress,
/// the wait for a first answer, and "last known state". The pages implement it and raise the change notifications themselves.
/// </summary>
public interface IFreshnessNotes
{
    Localizer L { get; }

    string? ErrorText { get; }

    bool HasRefreshError { get; }

    string RefreshErrorText { get; }

    /// <summary>A line for work the user started and is waiting for (a delete that waits for its run to stop); null for none.</summary>
    string? ProgressText => null;

    bool ShowWaiting { get; }

    string WaitingText { get; }

    bool IsStale { get; }

    string StaleText { get; }
}
