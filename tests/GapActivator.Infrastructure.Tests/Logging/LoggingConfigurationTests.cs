#pragma warning disable CA1848, CA1873 // Test code logs through the extension methods on purpose.
using GapActivator.Infrastructure.Logging;
using GapActivator.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GapActivator.Infrastructure.Tests.Logging;

public sealed class LoggingConfigurationTests
{
    // BI-090
    [Fact]
    public void Logging_EventLogRules_CriticalByDefaultAndErrorOnlyForCuratedCategories()
    {
        using ServiceProvider provider = BuildProvider();

        IList<LoggerFilterRule> rules = provider.GetRequiredService<IOptions<LoggerFilterOptions>>().Value.Rules.ToList();
        string eventLog = typeof(SafeEventLogLoggerProvider).FullName!;

        rules.ShouldContain(r => r.ProviderName == eventLog && r.CategoryName == null && r.LogLevel == LogLevel.Critical);
        foreach (string category in GapActivatorLogging.EventLogErrorCategories)
        {
            rules.ShouldContain(r => r.ProviderName == eventLog && r.CategoryName == category && r.LogLevel == LogLevel.Error);
        }

        rules.ShouldNotContain(r => r.CategoryName != null && r.CategoryName.Contains("LoggingAppNotifier", StringComparison.Ordinal));
        rules.ShouldContain(r => r.ProviderName == null && r.CategoryName == "Microsoft" && r.LogLevel == LogLevel.Warning);
        rules.ShouldContain(r => r.ProviderName == null && r.CategoryName == "Microsoft.Hosting.Lifetime" && r.LogLevel == LogLevel.Information);
        rules.ShouldContain(r => r.ProviderName == null && r.CategoryName == "Microsoft.EntityFrameworkCore" && r.LogLevel == LogLevel.Warning);
    }

    [Fact]
    public void Logging_Configured_AddsTheFileProviderAndNoConsole()
    {
        using ServiceProvider provider = BuildProvider();

        string[] names = provider.GetServices<ILoggerProvider>().Select(p => p.GetType().Name).ToArray();
        names.ShouldContain(nameof(DailyFileLoggerProvider));
        names.ShouldNotContain(n => n.Contains("Console", StringComparison.Ordinal));
    }

    // BI-090: behaviour through a real logger factory, with a capturing stand-in for the Event Log provider.
    [Fact]
    public void Logging_EventLogSide_ReceivesCriticalAndCuratedErrorsOnly()
    {
        var captured = new CapturingProvider();
        var paths = new GapActivatorPaths(Path.Combine(Path.GetTempPath(), "GapActivator.Tests", "rules-only"));
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddGapActivatorLogging(paths, new LogLevelSwitch(), new FakeTimeProvider(), () => captured));
        using ServiceProvider provider = services.BuildServiceProvider();
        ILoggerFactory factory = provider.GetRequiredService<ILoggerFactory>();

        ILogger sample = factory.CreateLogger("Sample.Category");
        sample.Log(LogLevel.Warning, "a warning");
        sample.Log(LogLevel.Error, "an error");
        sample.Log(LogLevel.Critical, "a critical");
        factory.CreateLogger("GapActivator.Application.Cycle.SchedulerLoop").Log(LogLevel.Error, "a loop error");
        factory.CreateLogger("GapActivator.Application.Cycle.CycleRunner").Log(LogLevel.Error, "a cycle error");

        captured.Entries.ShouldBe(["Sample.Category|Critical", "GapActivator.Application.Cycle.SchedulerLoop|Error"]);
    }

    // Each container owns (and disposes) its own Event Log provider; two containers never share one.
    [Fact]
    public void Logging_TwoContainers_EachCreatesItsOwnEventLogProvider()
    {
        var created = new List<CapturingProvider>();
        var paths = new GapActivatorPaths(Path.Combine(Path.GetTempPath(), "GapActivator.Tests", "rules-only"));
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddGapActivatorLogging(paths, new LogLevelSwitch(), new FakeTimeProvider(), () =>
        {
            var provider = new CapturingProvider();
            created.Add(provider);
            return provider;
        }));

        using ServiceProvider first = services.BuildServiceProvider();
        using ServiceProvider second = services.BuildServiceProvider();
        first.GetRequiredService<ILoggerFactory>().CreateLogger("First").Log(LogLevel.Critical, "one");
        second.GetRequiredService<ILoggerFactory>().CreateLogger("Second").Log(LogLevel.Critical, "two");

        created.Count.ShouldBe(2);
        created[0].Entries.ShouldBe(["First|Critical"]);
        created[1].Entries.ShouldBe(["Second|Critical"]);
    }

    // BI-090
    [Fact]
    public void Logging_SameCategoryAndMessageTwiceWithinAnHour_ReachesTheEventLogOnce()
    {
        var captured = new CapturingProvider();
        var time = new FakeTimeProvider();
        using ServiceProvider provider = BuildCapturing(captured, time, new GapActivatorPaths(Path.Combine(Path.GetTempPath(), "GapActivator.Tests", "rules-only")));
        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Sample.Category");

        logger.Log(LogLevel.Critical, "disk gone");
        logger.Log(LogLevel.Critical, "disk gone");
        captured.Entries.ShouldBe(["Sample.Category|Critical"]);

        time.Advance(TimeSpan.FromHours(1));
        logger.Log(LogLevel.Critical, "disk gone");

        captured.Entries.ShouldBe(["Sample.Category|Critical", "Sample.Category|Critical"]);
    }

    // BI-090
    [Fact]
    public async Task Logging_RateLimitedEntries_StillReachTheFileLog()
    {
        await using var root = new TempDataRoot();
        var paths = new GapActivatorPaths(root.Path);
        var captured = new CapturingProvider();
        var time = new FakeTimeProvider(new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero));
        string file;
        using (ServiceProvider provider = BuildCapturing(captured, time, paths))
        {
            ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Sample.Category");
            for (var i = 0; i < 10; i++)
            {
                logger.Log(LogLevel.Critical, "disk gone");
            }

            file = paths.ServiceLogFile(DateOnly.FromDateTime(time.GetLocalNow().DateTime));
        }

        captured.Entries.Count.ShouldBe(1);
        File.ReadAllLines(file).Count(l => l.Contains("disk gone", StringComparison.Ordinal)).ShouldBe(10);
    }

    // BI-090
    [Fact]
    public void Logging_SameMessageDifferentException_IsOneEntry()
    {
        var captured = new CapturingProvider();
        using ServiceProvider provider = BuildCapturing(captured, new FakeTimeProvider(), new GapActivatorPaths(Path.Combine(Path.GetTempPath(), "GapActivator.Tests", "rules-only")));
        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Sample.Category");

        logger.Log(LogLevel.Critical, new InvalidOperationException("first"), "store unreachable");
        logger.Log(LogLevel.Critical, new IOException("second"), "store unreachable");

        captured.Entries.ShouldBe(["Sample.Category|Critical"]);
    }

    private static ServiceProvider BuildCapturing(CapturingProvider captured, FakeTimeProvider time, GapActivatorPaths paths)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddGapActivatorLogging(paths, new LogLevelSwitch(), time, () => captured));
        return services.BuildServiceProvider();
    }

    private sealed class CapturingProvider : ILoggerProvider
    {
        public List<string> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner.Entries)
                {
                    owner.Entries.Add($"{category}|{logLevel}");
                }
            }
        }
    }

    private static ServiceProvider BuildProvider()
    {
        var paths = new GapActivatorPaths(Path.Combine(Path.GetTempPath(), "GapActivator.Tests", "rules-only"));
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddGapActivatorLogging(paths, new LogLevelSwitch(), new FakeTimeProvider()));
        return services.BuildServiceProvider();
    }
}
