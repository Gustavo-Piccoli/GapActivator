using DomainLogLevel = GapActivator.Domain.Settings.LogLevel;
using MelLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace GapActivator.Infrastructure.Logging;

/// <summary>The runtime minimum log level, which follows the installation settings.</summary>
public sealed class LogLevelSwitch
{
    private volatile int _minimum = (int)MelLogLevel.Information;

    public MelLogLevel Minimum => (MelLogLevel)_minimum;

    public void Set(DomainLogLevel level) =>
        _minimum = (int)(level switch
        {
            DomainLogLevel.Debug => MelLogLevel.Debug,
            DomainLogLevel.Information => MelLogLevel.Information,
            DomainLogLevel.Warning => MelLogLevel.Warning,
            DomainLogLevel.Error => MelLogLevel.Error,
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Not a defined log level."),
        });
}
