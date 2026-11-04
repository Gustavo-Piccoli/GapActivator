using System.Diagnostics;
using System.Globalization;

namespace GapActivator.Infrastructure.Processes;

/// <summary>
/// The identity of this service process that every child receives, so a child can tell whether its parent is still the
/// same process (the start time guards against PID reuse).
/// </summary>
internal sealed record ParentIdentity(int Pid, DateTime StartTimeUtc)
{
    public static ParentIdentity Current { get; } = ReadCurrent();

    /// <summary>The start time as UTC round-trip text, for example <c>2030-01-01T12:00:00.1234567Z</c>.</summary>
    public string FormattedStartTime => StartTimeUtc.ToString("O", CultureInfo.InvariantCulture);

    private static ParentIdentity ReadCurrent()
    {
        using Process current = Process.GetCurrentProcess();
        return new ParentIdentity(Environment.ProcessId, current.StartTime.ToUniversalTime());
    }
}
