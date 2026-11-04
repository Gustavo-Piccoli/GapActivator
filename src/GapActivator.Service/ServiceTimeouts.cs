using GapActivator.Domain.Processes;

namespace GapActivator.Service;

public static class ServiceTimeouts
{
    /// <summary>
    /// The host shutdown timeout and the SCM stop wait hint (addition a, BI-075): the largest possible grace period,
    /// plus the 15 seconds <c>SchedulerLoop</c> adds on shutdown, plus 30 seconds for the host to finish. 10 min 45 s.
    /// </summary>
    public static readonly TimeSpan Shutdown = ExecutionLimits.MaxGracePeriod + TimeSpan.FromSeconds(15) + TimeSpan.FromSeconds(30);
}
