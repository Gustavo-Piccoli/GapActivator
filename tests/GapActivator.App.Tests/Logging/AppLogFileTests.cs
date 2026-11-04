using GapActivator.App.Logging;
using GapActivator.App.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace GapActivator.App.Tests.Logging;

public class AppLogFileTests
{
    private static FakeTimeProvider NewTime(DateTimeOffset now)
    {
        var time = new FakeTimeProvider(now);
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        return time;
    }

    private static string Format(string state, Exception? error) => state;

    [Fact]
    public void Append_WritesTodaysFileWithTheLineFormat()
    {
        using var dir = new TempDirectory();
        var at = new DateTimeOffset(2026, 11, 4, 9, 8, 7, 6, TimeSpan.Zero);
        var file = new AppLogFile(dir.Path, NewTime(at));

        file.Append(at, LogLevel.Warning, "Gateway", "first", null);
        file.Append(at, LogLevel.Critical, "Gateway", "second", new InvalidOperationException("boom"));

        var lines = File.ReadAllLines(Path.Combine(dir.Path, "app-20261104.log"));
        lines[0].ShouldBe("2026-11-04 09:08:07.006 [WRN] Gateway: first");
        lines[1].ShouldBe("2026-11-04 09:08:07.006 [CRT] Gateway: second");
        lines[2].ShouldContain("System.InvalidOperationException: boom");
    }

    [Theory]
    [InlineData(LogLevel.Debug, "DBG")]
    [InlineData(LogLevel.Information, "INF")]
    [InlineData(LogLevel.Warning, "WRN")]
    [InlineData(LogLevel.Error, "ERR")]
    [InlineData(LogLevel.Critical, "CRT")]
    public void Append_EachLevel_UsesItsThreeLetterCode(LogLevel level, string code)
    {
        using var dir = new TempDirectory();
        var at = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

        new AppLogFile(dir.Path, NewTime(at)).Append(at, level, "C", "m", null);

        File.ReadAllText(Path.Combine(dir.Path, "app-20261104.log")).ShouldContain($"[{code}] C: m");
    }

    [Fact]
    public void Append_DeletesFilesOlderThan14DaysOncePerDay()
    {
        using var dir = new TempDirectory();
        var now = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        var time = NewTime(now);
        var file = new AppLogFile(dir.Path, time);
        string In(string name) => Path.Combine(dir.Path, name);
        File.WriteAllText(In("app-20261105.log"), "old");
        File.WriteAllText(In("app-20261106.log"), "edge");
        File.WriteAllText(In("other.log"), "keep");

        file.Append(now, LogLevel.Information, "C", "m", null);

        File.Exists(In("app-20261105.log")).ShouldBeFalse();
        File.Exists(In("app-20261106.log")).ShouldBeTrue();
        File.Exists(In("other.log")).ShouldBeTrue();

        // Same day: a file that is old by now is not swept again.
        File.WriteAllText(In("app-20261001.log"), "old");
        file.Append(now, LogLevel.Information, "C", "m", null);
        File.Exists(In("app-20261001.log")).ShouldBeTrue();

        // Next day: swept again.
        time.Advance(TimeSpan.FromDays(1));
        file.Append(time.GetUtcNow(), LogLevel.Information, "C", "m", null);
        File.Exists(In("app-20261001.log")).ShouldBeFalse();
        File.Exists(In("app-20261106.log")).ShouldBeFalse();
    }

    [Fact]
    public void Append_RetentionCannotDeleteAnOldFile_StillWritesTheLine()
    {
        using var dir = new TempDirectory();
        var now = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        var locked = Path.Combine(dir.Path, "app-20261001.log");
        File.WriteAllText(locked, "old");
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);

        new AppLogFile(dir.Path, NewTime(now)).Append(now, LogLevel.Error, "C", "kept", null);

        File.ReadAllText(Path.Combine(dir.Path, "app-20261120.log")).ShouldContain("[ERR] C: kept");
    }

    [Fact]
    public void Append_UnwritableDirectory_DoesNotThrow()
    {
        using var dir = new TempDirectory();
        var blocker = Path.Combine(dir.Path, "blocker");
        File.WriteAllText(blocker, "a file where the directory should be");
        var at = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);
        var file = new AppLogFile(Path.Combine(blocker, "logs"), NewTime(at));

        Should.NotThrow(() => file.Append(at, LogLevel.Error, "C", "m", new InvalidOperationException("x")));
    }

    [Fact]
    public void Logger_BelowInformation_IsNotWritten()
    {
        using var dir = new TempDirectory();
        var at = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);
        var time = NewTime(at);
        var logger = new AppLogger<AppLogFileTests>(new AppLogFile(dir.Path, time), time);

        logger.Log(LogLevel.Debug, default, "hidden", null, Format);
        logger.Log(LogLevel.Trace, default, "hidden", null, Format);
        logger.IsEnabled(LogLevel.Debug).ShouldBeFalse();
        logger.IsEnabled(LogLevel.Information).ShouldBeTrue();
        Directory.GetFiles(dir.Path).ShouldBeEmpty();

        logger.Log(LogLevel.Information, default, "shown 7", null, Format);

        File.ReadAllText(Path.Combine(dir.Path, "app-20261104.log")).ShouldContain("[INF] AppLogFileTests: shown 7");
    }
}
