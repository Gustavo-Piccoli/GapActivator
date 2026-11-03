using GapActivator.Domain.Common;

namespace GapActivator.Domain.Settings;

/// <summary>Single-row aggregate holding the installation-wide settings (Id is always 1).</summary>
public sealed class InstallationSettings : AggregateRoot<int>
{
    private const int SingletonId = 1;

    private InstallationSettings()
        : base(SingletonId)
    {
    }

    public TimeSpan CycleInterval { get; private set; }

    public int FailureLimit { get; private set; }

    public TimeSpan? DefaultTimeLimit { get; private set; }

    public TimeSpan DefaultGracePeriod { get; private set; }

    public int LogRetentionDays { get; private set; }

    public int HistoryRetentionDays { get; private set; }

    public LogLevel LogLevel { get; private set; }

    public EmailAlertSettings Email { get; private set; } = EmailAlertSettings.Disabled;

    public bool CheckForUpdates { get; private set; }

    public bool IsPaused { get; private set; }

    public static InstallationSettings Default() => new()
    {
        CycleInterval = TimeSpan.FromSeconds(60),
        FailureLimit = 3,
        DefaultTimeLimit = null,
        DefaultGracePeriod = TimeSpan.FromSeconds(30),
        LogRetentionDays = 30,
        HistoryRetentionDays = 90,
        LogLevel = LogLevel.Information,
        Email = EmailAlertSettings.Disabled,
        CheckForUpdates = true,
    };

    public void Change(
        TimeSpan cycleInterval,
        int failureLimit,
        TimeSpan? defaultTimeLimit,
        TimeSpan defaultGracePeriod,
        int logRetentionDays,
        int historyRetentionDays,
        LogLevel logLevel,
        bool checkForUpdates)
    {
        Require(cycleInterval >= TimeSpan.FromSeconds(5) && cycleInterval <= TimeSpan.FromHours(1),
            "Cycle interval must be between 5 seconds and 1 hour.");
        Require(failureLimit is >= 1 and <= 100, "Failure limit must be between 1 and 100.");
        Require(defaultTimeLimit is null || defaultTimeLimit > TimeSpan.Zero,
            "Default time limit must be greater than zero.");
        Require(defaultGracePeriod >= TimeSpan.FromSeconds(1) && defaultGracePeriod <= TimeSpan.FromMinutes(10),
            "Default grace period must be between 1 second and 10 minutes.");
        Require(logRetentionDays is >= 1 and <= 3650, "Log retention must be between 1 and 3650 days.");
        Require(historyRetentionDays is >= 1 and <= 3650, "History retention must be between 1 and 3650 days.");
        Require(Enum.IsDefined(logLevel), "Log level is not valid.");

        CycleInterval = cycleInterval;
        FailureLimit = failureLimit;
        DefaultTimeLimit = defaultTimeLimit;
        DefaultGracePeriod = defaultGracePeriod;
        LogRetentionDays = logRetentionDays;
        HistoryRetentionDays = historyRetentionDays;
        LogLevel = logLevel;
        CheckForUpdates = checkForUpdates;
    }

    public void ChangeEmail(EmailAlertSettings email)
    {
        ArgumentNullException.ThrowIfNull(email);
        Email = email;
    }

    public void Pause(DateTimeOffset now)
    {
        Require(!IsPaused, "The installation is already paused.");
        IsPaused = true;
        Raise(new InstallationPaused(now));
    }

    public void Resume(DateTimeOffset now)
    {
        Require(IsPaused, "The installation is not paused.");
        IsPaused = false;
        Raise(new InstallationResumed(now));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new DomainException(message);
        }
    }
}
