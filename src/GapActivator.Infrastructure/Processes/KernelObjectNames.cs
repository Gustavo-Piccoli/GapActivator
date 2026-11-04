using System.Globalization;

namespace GapActivator.Infrastructure.Processes;

/// <summary>Names of the per-child kernel objects (spec section 7).</summary>
internal static class KernelObjectNames
{
    /// <summary>The manual-reset event a child waits on to stop gracefully; part of the child contract.</summary>
    public static string CancelEvent(int pid) => $@"Local\GapActivator.Cancel.{pid}";

    /// <summary>
    /// The job object that holds the child and its descendants, for the tree kill; internal to GapActivator. The start
    /// time in the name means a job left behind for an earlier process with the same PID is never joined (ruling C24).
    /// </summary>
    public static string Job(int pid, DateTimeOffset startTime) =>
        string.Create(CultureInfo.InvariantCulture, $@"Local\GapActivator.Job.{pid}.{startTime.UtcTicks}");
}
