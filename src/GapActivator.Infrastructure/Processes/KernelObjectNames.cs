namespace GapActivator.Infrastructure.Processes;

/// <summary>Names of the per-child kernel objects of the child contract (spec section 7).</summary>
internal static class KernelObjectNames
{
    /// <summary>The manual-reset event a child waits on to stop gracefully.</summary>
    public static string CancelEvent(int pid) => $@"Local\GapActivator.Cancel.{pid}";

    /// <summary>The job object that holds the child and its descendants, for the tree kill.</summary>
    public static string Job(int pid) => $@"Local\GapActivator.Job.{pid}";
}
