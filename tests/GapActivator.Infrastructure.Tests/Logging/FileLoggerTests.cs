#pragma warning disable CA1848, CA1873 // Test code logs through the extension methods on purpose.
using GapActivator.Infrastructure.Logging;
using GapActivator.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using DomainLogLevel = GapActivator.Domain.Settings.LogLevel;

namespace GapActivator.Infrastructure.Tests.Logging;

[Trait("Category", "Integration")]
public sealed class FileLoggerTests : IAsyncLifetime
{
    private readonly TempDataRoot _root = new();
    private readonly LogLevelSwitch _level = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2030, 1, 1, 20, 0, 0, TimeSpan.Zero));

    public FileLoggerTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Plus9", TimeSpan.FromHours(9), "Plus9", "Plus9"));
    }

    private GapActivatorPaths Paths => new(_root.Path);

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _root.DisposeAsync();

    // BI-084
    [Fact]
    public void Log_Information_WritesFileNamedByLocalDateInTheLineFormat()
    {
        using var provider = new DailyFileLoggerProvider(Paths, _level, _time);
        provider.CreateLogger("Sample.Category").LogInformation("Hello {Name}", "world");

        string file = Path.Combine(Paths.LogsDirectory, "gapactivator-20300102.log");
        File.Exists(file).ShouldBeTrue();
        ReadLines(file).ShouldBe(["2030-01-02 05:00:00.000 +09:00 [INF] Sample.Category: Hello world"]);
    }

    [Theory]
    [InlineData(LogLevel.Debug, "DBG")]
    [InlineData(LogLevel.Warning, "WRN")]
    [InlineData(LogLevel.Error, "ERR")]
    [InlineData(LogLevel.Critical, "CRT")]
    public void FileLogger_LevelAbbreviation_MatchesTheFormat(LogLevel logLevel, string abbreviation)
    {
        _level.Set(DomainLogLevel.Debug);
        using var provider = new DailyFileLoggerProvider(Paths, _level, _time);
        provider.CreateLogger("C").Log(logLevel, "x");

        ReadLines(Path.Combine(Paths.LogsDirectory, "gapactivator-20300102.log")).Single().ShouldContain($"[{abbreviation}] C: x");
    }

    [Fact]
    public void FileLogger_DayChanges_RollsToANewFile()
    {
        using var provider = new DailyFileLoggerProvider(Paths, _level, _time);
        ILogger logger = provider.CreateLogger("C");
        logger.LogInformation("first");
        _time.Advance(TimeSpan.FromHours(20));
        logger.LogInformation("second");

        ReadLines(Path.Combine(Paths.LogsDirectory, "gapactivator-20300102.log")).Single().ShouldEndWith("first");
        ReadLines(Path.Combine(Paths.LogsDirectory, "gapactivator-20300103.log")).Single().ShouldEndWith("second");
    }

    [Fact]
    public void FileLogger_BelowSwitchLevel_NotWrittenAndSwitchChangeAppliesAtOnce()
    {
        using var provider = new DailyFileLoggerProvider(Paths, _level, _time);
        ILogger logger = provider.CreateLogger("C");
        logger.LogDebug("hidden");
        logger.LogInformation("shown");
        _level.Set(DomainLogLevel.Debug);
        logger.LogDebug("now visible");
        _level.Set(DomainLogLevel.Error);
        logger.LogWarning("hidden again");
        logger.IsEnabled(LogLevel.None).ShouldBeFalse();

        string[] lines = ReadLines(Path.Combine(Paths.LogsDirectory, "gapactivator-20300102.log"));
        lines.Length.ShouldBe(2);
        lines[0].ShouldEndWith("[INF] C: shown");
        lines[1].ShouldEndWith("[DBG] C: now visible");
    }

    // BI-085
    [Fact]
    public void FileLogger_FolderUnwritable_DoesNotThrow()
    {
        File.WriteAllText(Path.Combine(_root.Path, "logs"), "a file where the logs folder should be");
        using var provider = new DailyFileLoggerProvider(Paths, _level, _time);
        ILogger logger = provider.CreateLogger("C");

        Should.NotThrow(() => logger.LogError(new InvalidOperationException("boom"), "failed"));
        Should.NotThrow(() => logger.LogInformation("again"));
    }

    [Fact]
    public void FileLogger_Exception_WritesIndentedStackLines()
    {
        using var provider = new DailyFileLoggerProvider(Paths, _level, _time);
        Exception exception;
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (InvalidOperationException e)
        {
            exception = e;
        }

        provider.CreateLogger("C").LogError(exception, "failed");

        string[] lines = ReadLines(Path.Combine(Paths.LogsDirectory, "gapactivator-20300102.log"));
        lines[0].ShouldEndWith("[ERR] C: failed");
        lines.Length.ShouldBeGreaterThan(1);
        lines.Skip(1).ShouldAllBe(line => line.StartsWith("    ", StringComparison.Ordinal));
        lines[1].ShouldContain("System.InvalidOperationException: boom");
        lines.ShouldContain(line => line.Contains("   at ", StringComparison.Ordinal));
    }

    // C11: the live daily file is held without FileShare.Delete, so retention skips it.
    [Fact]
    public void FileLogger_LiveFile_CannotBeDeletedWhileOpen()
    {
        var provider = new DailyFileLoggerProvider(Paths, _level, _time);
        provider.CreateLogger("C").LogInformation("x");
        string file = Path.Combine(Paths.LogsDirectory, "gapactivator-20300102.log");

        Should.Throw<IOException>(() => File.Delete(file));

        provider.Dispose();
        Should.NotThrow(() => File.Delete(file));
    }

    // BI-085, BI-090
    [Fact]
    public void SafeEventLog_InnerThrows_IsSwallowed()
    {
        var provider = new SafeEventLogLoggerProvider(new ThrowingProvider());
        ILogger logger = provider.CreateLogger("C");

        Should.NotThrow(() => logger.LogError("x"));
        Should.NotThrow(() => logger.IsEnabled(LogLevel.Error));
        Should.NotThrow(() => logger.BeginScope("s"));
        Should.NotThrow(provider.Dispose);
    }

    private static string[] ReadLines(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    private sealed class ThrowingProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ThrowingLogger();

        public void Dispose() => throw new InvalidOperationException("dispose");

        private sealed class ThrowingLogger : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => throw new InvalidOperationException("scope");

            public bool IsEnabled(LogLevel logLevel) => throw new InvalidOperationException("enabled");

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                throw new InvalidOperationException("log");
        }
    }
}
