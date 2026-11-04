using GapActivator.Domain.Processes;
using GapActivator.Domain.Settings;

namespace GapActivator.Application.Runs;

/// <summary>
/// The time limit and grace period a run of the process gets: the process's own values, else the current installation
/// defaults. A Service-kind run has no time limit.
/// </summary>
internal readonly record struct RunLimits(TimeSpan? TimeLimit, TimeSpan GracePeriod)
{
    public static RunLimits For(Process process, InstallationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(settings);
        return new(process.EffectiveTimeLimit(settings.DefaultTimeLimit), process.EffectiveGracePeriod(settings.DefaultGracePeriod));
    }
}
