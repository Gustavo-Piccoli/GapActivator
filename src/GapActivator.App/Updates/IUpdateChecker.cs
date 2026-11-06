namespace GapActivator.App.Updates;

/// <summary>Checks for a newer release, on demand and in the background.</summary>
public interface IUpdateChecker
{
    /// <summary>The outcome of the last check in this run, or null before the first one.</summary>
    UpdateCheckResult? Last { get; }

    /// <summary>Raised after every check, whatever the outcome and before <see cref="UpdateFound"/>; same threading as <see cref="UpdateFound"/>.</summary>
    event EventHandler<UpdateCheckResult>? Checked;

    /// <summary>Raised on whatever thread ran the check (a pool thread for the background check): consumers marshal to the UI.</summary>
    event EventHandler<UpdateCheckResult>? UpdateFound;

    Task<UpdateCheckResult> CheckNowAsync(CancellationToken ct);
}
