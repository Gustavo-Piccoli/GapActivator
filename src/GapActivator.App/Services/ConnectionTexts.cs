using GapActivator.App.Localization;
using GapActivator.Ipc;

namespace GapActivator.App.Services;

/// <summary>The one mapping from a connection state to the text that explains it; the banner and the dashboard share it.</summary>
internal static class ConnectionTexts
{
    /// <summary>The explanation of a state that is not working, or null while connected.</summary>
    public static string? Banner(GatewayStatus status, Localizer l)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(l);
        return status.State switch
        {
            ConnectionState.Connecting => l.BannerConnecting,
            ConnectionState.ServiceStopped => l.BannerStopped,
            ConnectionState.ServiceNotInstalled => l.BannerNotInstalled,
            ConnectionState.Refused => ServiceErrors.Describe(status.ErrorCode ?? IpcErrorCodes.Disconnected, null, l),
            _ => null,
        };
    }
}
