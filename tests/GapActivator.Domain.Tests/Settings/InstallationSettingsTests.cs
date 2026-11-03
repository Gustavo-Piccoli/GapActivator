using GapActivator.Domain.Common;
using GapActivator.Domain.Settings;

namespace GapActivator.Domain.Tests.Settings;

public class InstallationSettingsTests
{
    private static void Apply(
        InstallationSettings s,
        TimeSpan? cycle = null,
        int failureLimit = 3,
        TimeSpan? timeLimit = null,
        TimeSpan? grace = null,
        int logDays = 30,
        int historyDays = 90) =>
        s.Change(cycle ?? TimeSpan.FromSeconds(60), failureLimit, timeLimit,
            grace ?? TimeSpan.FromSeconds(30), logDays, historyDays, LogLevel.Warning, false);

    [Fact]
    public void Default_Always_ReturnsPlanDefaults()
    {
        var s = InstallationSettings.Default();

        s.Id.ShouldBe(1);
        s.CycleInterval.ShouldBe(TimeSpan.FromSeconds(60));
        s.FailureLimit.ShouldBe(3);
        s.DefaultTimeLimit.ShouldBeNull();
        s.DefaultGracePeriod.ShouldBe(TimeSpan.FromSeconds(30));
        s.LogRetentionDays.ShouldBe(30);
        s.HistoryRetentionDays.ShouldBe(90);
        s.LogLevel.ShouldBe(LogLevel.Information);
        s.Email.ShouldBe(EmailAlertSettings.Disabled);
        s.CheckForUpdates.ShouldBeTrue();
    }

    [Fact]
    public void Change_ValidValues_UpdatesAll()
    {
        var s = InstallationSettings.Default();

        s.Change(TimeSpan.FromSeconds(10), 5, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(45),
            7, 365, LogLevel.Debug, false);

        s.CycleInterval.ShouldBe(TimeSpan.FromSeconds(10));
        s.FailureLimit.ShouldBe(5);
        s.DefaultTimeLimit.ShouldBe(TimeSpan.FromMinutes(2));
        s.DefaultGracePeriod.ShouldBe(TimeSpan.FromSeconds(45));
        s.LogRetentionDays.ShouldBe(7);
        s.HistoryRetentionDays.ShouldBe(365);
        s.LogLevel.ShouldBe(LogLevel.Debug);
        s.CheckForUpdates.ShouldBeFalse();
    }

    [Theory]
    [InlineData(5)]
    [InlineData(3600)]
    public void Change_CycleIntervalOnBoundary_Succeeds(int seconds) =>
        Should.NotThrow(() => Apply(InstallationSettings.Default(), cycle: TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(4)]
    [InlineData(3601)]
    public void Change_CycleIntervalOutOfRange_Throws(int seconds) =>
        Should.Throw<DomainException>(() => Apply(InstallationSettings.Default(), cycle: TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Change_FailureLimitOnBoundary_Succeeds(int limit) =>
        Should.NotThrow(() => Apply(InstallationSettings.Default(), failureLimit: limit));

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Change_FailureLimitOutOfRange_Throws(int limit) =>
        Should.Throw<DomainException>(() => Apply(InstallationSettings.Default(), failureLimit: limit));

    [Fact]
    public void Change_PositiveDefaultTimeLimit_Succeeds() =>
        Should.NotThrow(() => Apply(InstallationSettings.Default(), timeLimit: TimeSpan.FromTicks(1)));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Change_NonPositiveDefaultTimeLimit_Throws(int seconds) =>
        Should.Throw<DomainException>(() => Apply(InstallationSettings.Default(), timeLimit: TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(1)]
    [InlineData(600)]
    public void Change_GracePeriodOnBoundary_Succeeds(int seconds) =>
        Should.NotThrow(() => Apply(InstallationSettings.Default(), grace: TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(0)]
    [InlineData(601)]
    public void Change_GracePeriodOutOfRange_Throws(int seconds) =>
        Should.Throw<DomainException>(() => Apply(InstallationSettings.Default(), grace: TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(1)]
    [InlineData(3650)]
    public void Change_RetentionOnBoundary_Succeeds(int days)
    {
        Should.NotThrow(() => Apply(InstallationSettings.Default(), logDays: days));
        Should.NotThrow(() => Apply(InstallationSettings.Default(), historyDays: days));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3651)]
    public void Change_RetentionOutOfRange_Throws(int days)
    {
        Should.Throw<DomainException>(() => Apply(InstallationSettings.Default(), logDays: days));
        Should.Throw<DomainException>(() => Apply(InstallationSettings.Default(), historyDays: days));
    }

    [Fact]
    public void Change_Invalid_LeavesStateUnchanged()
    {
        var s = InstallationSettings.Default();

        Should.Throw<DomainException>(() => Apply(s, cycle: TimeSpan.FromSeconds(10), historyDays: 0));

        s.CycleInterval.ShouldBe(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void ChangeEmail_ValidSettings_ReplacesEmail()
    {
        var s = InstallationSettings.Default();
        var email = EmailAlertSettings.Create(true, "smtp.example.com", 25, TlsMode.None, null, null,
            "activator@example.com", ["ops@example.com"], [AlertKind.RunFailed]);

        s.ChangeEmail(email);

        s.Email.ShouldBe(email);
    }

    [Fact]
    public void ChangeEmail_Null_Throws() =>
        Should.Throw<ArgumentNullException>(() => InstallationSettings.Default().ChangeEmail(null!));
}
