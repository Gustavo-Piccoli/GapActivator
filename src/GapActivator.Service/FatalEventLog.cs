using System.Diagnostics;
using GapActivator.Infrastructure.Logging;

namespace GapActivator.Service;

/// <summary>
/// Best-effort Event Log entry for a failure that happens before the service logging exists. It never throws, and it
/// writes nothing when the event source is not registered (creating it needs administrator rights; the installer does).
/// </summary>
internal static class FatalEventLog
{
    // The Event Log limit is about 31 839 characters per entry.
    private const int MaxMessageLength = 30_000;

    public static void Write(Exception exception) => Write(exception, GapActivatorLogging.EventSource);

    internal static void Write(Exception exception, string source)
    {
        try
        {
            if (!EventLog.SourceExists(source))
            {
                return;
            }

            string message = "GapActivator could not start or run and stops." + Environment.NewLine + exception;
            if (message.Length > MaxMessageLength)
            {
                message = message[..MaxMessageLength];
            }

            EventLog.WriteEntry(source, message, EventLogEntryType.Error);
        }
        catch (Exception)
        {
            // Nothing more can be done: the process ends with the fatal exit code either way.
        }
    }
}
