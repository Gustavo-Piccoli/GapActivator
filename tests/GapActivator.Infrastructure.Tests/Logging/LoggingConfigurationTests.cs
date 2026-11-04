using GapActivator.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GapActivator.Infrastructure.Tests.Logging;

public sealed class LoggingConfigurationTests
{
    // BI-090
    [Fact]
    public void Logging_EventLogRules_OnlyErrorAndAboveAndNeverAppNotifications()
    {
        using ServiceProvider provider = BuildProvider();

        IList<LoggerFilterRule> rules = provider.GetRequiredService<IOptions<LoggerFilterOptions>>().Value.Rules.ToList();
        string eventLog = typeof(SafeEventLogLoggerProvider).FullName!;

        rules.ShouldContain(r => r.ProviderName == eventLog && r.CategoryName == null && r.LogLevel == LogLevel.Error);
        rules.ShouldContain(r => r.ProviderName == eventLog
            && r.CategoryName == "GapActivator.Infrastructure.Notifications.LoggingAppNotifier"
            && r.LogLevel == LogLevel.None);
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

    private static ServiceProvider BuildProvider()
    {
        var paths = new GapActivatorPaths(Path.Combine(Path.GetTempPath(), "GapActivator.Tests", "rules-only"));
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddGapActivatorLogging(paths, new LogLevelSwitch(), new FakeTimeProvider()));
        return services.BuildServiceProvider();
    }
}
