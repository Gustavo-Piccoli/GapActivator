using GapActivator.App.Localization;
using GapActivator.App.Services;

namespace GapActivator.App.ViewModels;

/// <summary>
/// How much a page that shows <see cref="LiveState"/> can trust what it shows: nothing yet, the last known state because the
/// service is not connected, or possibly out of date because the last refresh failed. The pages that share it raise the change
/// notifications themselves, on a connection change, on every live-state change and on a language switch.
/// </summary>
public sealed class LiveFreshness(Localizer l, IServiceGateway gateway, LiveState live)
{
    public bool IsConnected => gateway.Status.IsConnected;

    /// <summary>False until the service has reported once; before that a list would claim "nothing" when the answer is "unknown".</summary>
    public bool HasData => live.Dashboard is not null;

    /// <summary>True while the page shows the last known state because the service is not connected; the page says so and dims the data.</summary>
    public bool IsStale => HasData && !IsConnected;

    public string StaleText => l.StaleNote;

    /// <summary>True when the last refresh failed, so the data may be out of date even though the connection is up.</summary>
    public bool HasRefreshError => live.RefreshFailed;

    public string RefreshErrorText => l.RefreshFailedText;

    /// <summary>The "waiting" line is only for a connected page with no answer yet; while disconnected the connection text speaks, and after a failure the error does.</summary>
    public bool ShowWaiting => !HasData && IsConnected && !live.RefreshFailed;
}
