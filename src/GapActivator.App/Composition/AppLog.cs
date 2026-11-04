using Microsoft.Extensions.Logging;

namespace GapActivator.App.Composition;

internal static partial class AppLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception on the UI thread")]
    public static partial void UnhandledUiException(ILogger logger, Exception exception);
}
