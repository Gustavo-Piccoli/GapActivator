using Microsoft.Extensions.Logging;

namespace GapActivator.App.Composition;

internal static partial class AppLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception on the UI thread")]
    public static partial void UnhandledUiException(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refreshing the live state failed.")]
    public static partial void RefreshingLiveStateFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "A live-state subscriber failed.")]
    public static partial void LiveStateSubscriberFailed(ILogger logger, Exception exception);
}
