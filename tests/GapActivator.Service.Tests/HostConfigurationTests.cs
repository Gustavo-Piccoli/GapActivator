#pragma warning disable CA1848, CA1873 // Test code logs through the extension methods on purpose.
using GapActivator.Application.Ports;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Settings;
using GapActivator.Infrastructure;
using GapActivator.Infrastructure.Logging;
using GapActivator.Infrastructure.Persistence;
using GapActivator.Service.Tests.Support;
using GapActivator.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DomainLogLevel = GapActivator.Domain.Settings.LogLevel;
using LoggingLevel = Microsoft.Extensions.Logging.LogLevel;

namespace GapActivator.Service.Tests;

[Trait("Category", "Integration")]
public sealed class HostConfigurationTests
{
    // BI-090
    [Fact]
    public async Task ServiceHostFactory_EventLog_IsOffForEveryCategory()
    {
        await using var root = new TempDataRoot();
        using IHost host = ServiceHostFactory.CreateBuilder(root.Path, []).Build();

        IList<LoggerFilterRule> rules = host.Services.GetRequiredService<IOptions<LoggerFilterOptions>>().Value.Rules.ToList();
        string eventLog = typeof(SafeEventLogLoggerProvider).FullName!;

        rules.ShouldContain(r => r.ProviderName == eventLog && r.CategoryName == null && r.LogLevel == LoggingLevel.None);
        GapActivatorLogging.EventLogErrorCategories.ShouldNotBeEmpty();
        foreach (string category in GapActivatorLogging.EventLogErrorCategories)
        {
            // The last rule of equal specificity wins, so the None rule must come after the policy's Error rule.
            LoggerFilterRule last = rules.Last(r => r.ProviderName == eventLog && r.CategoryName == category);
            last.LogLevel.ShouldBe(LoggingLevel.None);
        }
    }

    // BI-075 (addition a)
    [Fact]
    public async Task HostOptions_Built_ShutdownTimeoutCoversTheLargestGracePeriodPlus15Seconds()
    {
        await using var root = new TempDataRoot();
        using IHost host = ServiceHostFactory.CreateBuilder(root.Path, []).Build();

        HostOptions options = host.Services.GetRequiredService<IOptions<HostOptions>>().Value;

        options.ShutdownTimeout.ShouldBeGreaterThanOrEqualTo(ExecutionLimits.MaxGracePeriod + TimeSpan.FromSeconds(15));
        options.ShutdownTimeout.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMinutes(10));
        options.ShutdownTimeout.ShouldBe(ServiceTimeouts.Shutdown);
        options.BackgroundServiceExceptionBehavior.ShouldBe(BackgroundServiceExceptionBehavior.StopHost);
        ServiceTimeouts.Shutdown.ShouldBe(TimeSpan.FromSeconds(645));
    }

    [Fact]
    public async Task Host_DataRootFromCommandLine_IsUsed()
    {
        await using var root = new TempDataRoot();
        using IHost host = ServiceHostFactory.CreateBuilder(root.Path, []).Build();

        host.Services.GetRequiredService<GapActivatorPaths>().DataRoot.ShouldBe(root.Path);
        host.Services.GetRequiredService<InfrastructureOptions>().Paths.DataRoot.ShouldBe(root.Path);
    }

    [Fact]
    public async Task Build_WithValidateOnBuildAndValidateScopes_Succeeds()
    {
        await using var root = new TempDataRoot();
        HostApplicationBuilder builder = ServiceHostFactory.CreateBuilder(root.Path, []);
        builder.ConfigureContainer(new DefaultServiceProviderFactory(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));

        using IHost host = builder.Build();

        host.Services.GetServices<IHostedService>().OfType<GapActivatorWorker>().Count().ShouldBe(1);
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IServiceControl>().ShouldBeOfType<ScopedServiceControl>();
    }

    [Fact]
    public async Task Host_NotAWindowsService_LogsToTheFileAndTheConsole()
    {
        await using var root = new TempDataRoot();
        using IHost host = GapActivatorHost.CreateBuilder(["--GapActivator:DataRoot=" + root.Path]).Build();

        string[] providers = [.. host.Services.GetServices<ILoggerProvider>().Select(p => p.GetType().Name)];

        providers.ShouldContain(nameof(DailyFileLoggerProvider));
        providers.ShouldContain("ConsoleLoggerProvider");
        providers.ShouldNotContain("EventLogLoggerProvider");
        host.Services.GetRequiredService<IHostLifetime>().ShouldNotBeOfType<GapActivatorServiceLifetime>();
    }

    // C3: the switch the unit of work updates after a settings commit is the one the file logger reads.
    [Fact]
    public async Task Host_LogLevelChangedThroughTheUnitOfWork_FileLoggerFollowsIt()
    {
        await using var root = new TempDataRoot();
        CancellationToken ct = TestContext.Current.CancellationToken;
        try
        {
            using (IHost host = ServiceHostFactory.CreateBuilder(root.Path, []).Build())
            {
                await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
                {
                    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(ct);
                    var db = scope.ServiceProvider.GetRequiredService<GapActivatorDbContext>();
                    InstallationSettings settings = await db.Settings.SingleAsync(ct);
                    settings.Change(
                        settings.CycleInterval, settings.FailureLimit, settings.DefaultTimeLimit, settings.DefaultGracePeriod,
                        settings.LogRetentionDays, settings.HistoryRetentionDays, DomainLogLevel.Debug, settings.CheckForUpdates);
                    await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(ct);
                }

                ILogger probe = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("GapActivator.Tests.Probe");
                probe.LogDebug("debug-after-settings-commit");
            }

            var paths = new GapActivatorPaths(root.Path);
            string log = string.Concat(Directory.EnumerateFiles(paths.LogsDirectory, "gapactivator-*.log").Select(File.ReadAllText));
            log.ShouldContain("debug-after-settings-commit");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }
}
