using GapActivator.Application;
using GapActivator.Application.Cycle;
using GapActivator.Application.Ports;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Scheduling;
using GapActivator.Domain.Settings;
using GapActivator.Infrastructure.Logging;
using GapActivator.Infrastructure.Persistence;
using GapActivator.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GapActivator.Infrastructure.Tests.Composition;

public class AddInfrastructureTests
{
    [Fact]
    public async Task AddInfrastructure_WithApplicationAndValidation_BuildsAndResolvesTheSingletons()
    {
        await using var root = new TempDataRoot();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(new InfrastructureOptions(new GapActivatorPaths(root.Path)));
        services.AddSingleton<IServiceControl, FakeServiceControl>();

        // A singleton that captures a scoped service fails here, either at build or when it is resolved.
        await using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        provider.GetRequiredService<IProcessLauncher>().ShouldNotBeNull();
        provider.GetRequiredService<IEmailSender>().ShouldNotBeNull();
        provider.GetRequiredService<IOccurrenceCalculator>().ShouldNotBeNull();
        provider.GetRequiredService<SchedulerLoop>().ShouldNotBeNull();
    }

    [Fact]
    public void AddInfrastructure_WhenASingletonCapturesAScopedPort_FailsScopeValidation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(new InfrastructureOptions(new GapActivatorPaths(Path.Combine(Path.GetTempPath(), "ga-captive"))));
        services.AddSingleton<CaptiveSingleton>();

        AggregateException error = Should.Throw<AggregateException>(() => services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }));
        error.ToString().ShouldContain(nameof(CaptiveSingleton));
    }

    [Fact]
    public void AddInfrastructure_LongLivedAdapters_AreSingletons()
    {
        ServiceCollection services = Compose();

        foreach (Type port in new[]
        {
            typeof(IProcessLauncher), typeof(ISecretProtector), typeof(ILogRetention), typeof(IRunOutputReader),
            typeof(IEmailSender), typeof(IAppNotifier), typeof(IOccurrenceCalculator),
        })
        {
            services.Single(d => d.ServiceType == port).Lifetime.ShouldBe(ServiceLifetime.Singleton, port.Name);
        }
    }

    [Fact]
    public void AddInfrastructure_PersistencePorts_AreScoped()
    {
        ServiceCollection services = Compose();

        foreach (Type port in new[]
        {
            typeof(IUnitOfWork), typeof(IProcessRepository), typeof(IRunRepository), typeof(ISettingsRepository),
            typeof(IHistoryWriter), typeof(IRunHistoryReader), typeof(IHistoryRetention), typeof(GapActivatorDbContext),
        })
        {
            services.Single(d => d.ServiceType == port).Lifetime.ShouldBe(ServiceLifetime.Scoped, port.Name);
        }
    }

    [Fact]
    public void AddInfrastructure_WhenTheHostRegisteredALogLevelSwitch_KeepsThatInstance()
    {
        var hostSwitch = new LogLevelSwitch();
        var services = new ServiceCollection();
        services.AddSingleton(hostSwitch);
        services.AddInfrastructure(new InfrastructureOptions(new GapActivatorPaths(Path.Combine(Path.GetTempPath(), "ga-switch"))));

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<LogLevelSwitch>().ShouldBeSameAs(hostSwitch);
    }

    private static ServiceCollection Compose()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(new InfrastructureOptions(new GapActivatorPaths(Path.Combine(Path.GetTempPath(), "ga-lifetimes"))));
        return services;
    }

    private sealed class CaptiveSingleton(IUnitOfWork unitOfWork)
    {
        public IUnitOfWork UnitOfWork { get; } = unitOfWork;
    }
}
