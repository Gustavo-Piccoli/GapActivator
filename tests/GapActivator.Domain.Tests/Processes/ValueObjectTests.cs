using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Settings;

namespace GapActivator.Domain.Tests.Processes;

public class ValueObjectTests
{
    [Fact]
    public void ProcessId_New_ReturnsDistinctNonEmptyIds()
    {
        var first = ProcessId.New();
        var second = ProcessId.New();

        first.Value.ShouldNotBe(Guid.Empty);
        first.ShouldNotBe(second);
    }

    [Fact]
    public void ProcessName_Create_TrimsValue()
    {
        ProcessName.Create("  Nightly backup  ").Value.ShouldBe("Nightly backup");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\tb")]
    public void ProcessName_Create_InvalidValue_Throws(string value)
    {
        Should.Throw<DomainException>(() => ProcessName.Create(value));
    }

    [Fact]
    public void ProcessName_Create_TooLong_Throws()
    {
        Should.Throw<DomainException>(() => ProcessName.Create(new string('a', 101)));
        ProcessName.Create(new string('a', 100)).Value.Length.ShouldBe(100);
    }

    [Fact]
    public void ProcessName_Equality_IsCaseInsensitive()
    {
        var lower = ProcessName.Create("Backup");
        var upper = ProcessName.Create("BACKUP");

        lower.ShouldBe(upper);
        (lower == upper).ShouldBeTrue();
        lower.GetHashCode().ShouldBe(upper.GetHashCode());
        lower.ShouldNotBe(ProcessName.Create("Other"));
    }

    [Fact]
    public void ExecutablePath_Create_FullyQualified_Succeeds()
    {
        ExecutablePath.Create(@"  C:\Tools\backup.exe ").Value.ShouldBe(@"C:\Tools\backup.exe");
    }

    [Theory]
    [InlineData("")]
    [InlineData("backup.exe")]
    [InlineData("\"C:\\Tools\\a.exe\"")]
    [InlineData("C:\\Tools\\a|b.exe")]
    public void ExecutablePath_Create_InvalidValue_Throws(string value)
    {
        Should.Throw<DomainException>(() => ExecutablePath.Create(value));
    }

    [Fact]
    public void ExecutablePath_Create_TooLong_Throws()
    {
        Should.Throw<DomainException>(() => ExecutablePath.Create(@"C:\" + new string('a', 32_770)));
    }

    [Fact]
    public void ExecutablePath_Equality_IsCaseInsensitive()
    {
        var lower = ExecutablePath.Create(@"C:\Tools\backup.exe");
        var upper = ExecutablePath.Create(@"C:\TOOLS\BACKUP.EXE");

        lower.ShouldBe(upper);
        lower.GetHashCode().ShouldBe(upper.GetHashCode());
        lower.ShouldNotBe(ExecutablePath.Create(@"C:\Tools\other.exe"));
    }

    [Fact]
    public void ProtectedSecret_ToString_IsMasked()
    {
        new ProtectedSecret("abc").ToString().ShouldBe("***");
    }

    [Fact]
    public void ProtectedSecret_Empty_Throws()
    {
        Should.Throw<DomainException>(() => new ProtectedSecret(""));
    }

    [Fact]
    public void EnvironmentVariable_PlainAndSecret_SetFlags()
    {
        var plain = EnvironmentVariable.Plain("MODE", "fast");
        var secret = EnvironmentVariable.Secret("TOKEN", new ProtectedSecret("cipher"));

        plain.IsSecret.ShouldBeFalse();
        plain.PlainValue.ShouldBe("fast");
        secret.IsSecret.ShouldBeTrue();
        secret.SecretValue.ShouldNotBeNull();
        secret.PlainValue.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("A=B")]
    [InlineData("A\0B")]
    [InlineData("GAPACTIVATOR_PARENT_PID")]
    [InlineData("gapactivator_x")]
    public void EnvironmentVariable_Plain_InvalidName_Throws(string name)
    {
        Should.Throw<DomainException>(() => EnvironmentVariable.Plain(name, "1"));
    }

    [Fact]
    public void EnvironmentVariables_Create_DuplicateNamesIgnoringCase_Throws()
    {
        Should.Throw<DomainException>(() => EnvironmentVariables.Create(
            [EnvironmentVariable.Plain("Path_Extra", "1"), EnvironmentVariable.Plain("PATH_EXTRA", "2")]));
    }

    [Fact]
    public void EnvironmentVariables_Create_KeepsOrder_AndEmptyIsEmpty()
    {
        var vars = EnvironmentVariables.Create(
            [EnvironmentVariable.Plain("B", "1"), EnvironmentVariable.Plain("A", "2")]);

        vars.Items.Select(v => v.Name).ShouldBe(["B", "A"]);
        EnvironmentVariables.Empty.Items.ShouldBeEmpty();
    }

    [Fact]
    public void LaunchOptions_Valid_Succeeds()
    {
        var options = new LaunchOptions(
            ExecutablePath.Create(@"C:\Tools\backup.exe"), "", null, EnvironmentVariables.Empty);

        options.Arguments.ShouldBeEmpty();
    }

    [Fact]
    public void LaunchOptions_ArgumentsTooLong_Throws()
    {
        Should.Throw<DomainException>(() => new LaunchOptions(
            ExecutablePath.Create(@"C:\Tools\backup.exe"), new string('x', 8_192), null, EnvironmentVariables.Empty));
    }

    [Fact]
    public void LaunchOptions_RelativeWorkingDirectory_Throws()
    {
        Should.Throw<DomainException>(() => new LaunchOptions(
            ExecutablePath.Create(@"C:\Tools\backup.exe"), "", "work", EnvironmentVariables.Empty));
    }

    [Fact]
    public void EffectiveWorkingDirectory_Unset_IsExecutableFolder()
    {
        new LaunchOptions(ExecutablePath.Create(@"C:\Tools\backup.exe"), "", null, EnvironmentVariables.Empty)
            .EffectiveWorkingDirectory.ShouldBe(@"C:\Tools");
    }

    [Fact]
    public void EffectiveWorkingDirectory_Set_IsThatFolder()
    {
        new LaunchOptions(ExecutablePath.Create(@"C:\Tools\backup.exe"), "", @"D:\Work", EnvironmentVariables.Empty)
            .EffectiveWorkingDirectory.ShouldBe(@"D:\Work");
    }

    [Fact]
    public void ExecutionLimits_PerProcessTimeLimit_WinsOverDefault()
    {
        new ExecutionLimits(TimeSpan.FromHours(2), null).EffectiveTimeLimit(TimeSpan.FromHours(8))
            .ShouldBe(TimeSpan.FromHours(2));
    }

    [Fact]
    public void ExecutionLimits_Unset_UsesDefaults()
    {
        var limits = new ExecutionLimits(null, null);

        limits.EffectiveTimeLimit(null).ShouldBeNull();
        limits.EffectiveTimeLimit(TimeSpan.FromHours(8)).ShouldBe(TimeSpan.FromHours(8));
        limits.EffectiveGracePeriod(TimeSpan.FromSeconds(30)).ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ExecutionLimits_PerProcessGracePeriod_WinsOverDefault()
    {
        new ExecutionLimits(null, TimeSpan.FromSeconds(5)).EffectiveGracePeriod(TimeSpan.FromSeconds(30))
            .ShouldBe(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ExecutionLimits_ZeroOrNegative_Throws()
    {
        Should.Throw<DomainException>(() => new ExecutionLimits(TimeSpan.Zero, null));
        Should.Throw<DomainException>(() => new ExecutionLimits(null, TimeSpan.Zero));
        Should.Throw<DomainException>(() => new ExecutionLimits(TimeSpan.FromSeconds(-1), null));
    }

    [Fact]
    public void ExecutionLimits_GracePeriodOverTenMinutes_Throws()
    {
        Should.Throw<DomainException>(() => new ExecutionLimits(null, TimeSpan.FromMinutes(10.1)));
        _ = new ExecutionLimits(null, TimeSpan.FromMinutes(10));
    }
}
