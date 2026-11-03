using GapActivator.Application;
using GapActivator.Application.Ports;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Scheduling;
using GapActivator.Domain.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace GapActivator.Testing;

/// <summary>
/// The application layer wired to in-memory fakes and a fake clock. Dispatching helpers are added
/// together with the dispatchers.
/// </summary>
public sealed class ApplicationTestHost : IAsyncDisposable
{
    public static readonly DateTimeOffset StartTime = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <param name="configure">Applied after the defaults, so it can replace any registration.</param>
    public ApplicationTestHost(Action<IServiceCollection>? configure = null)
    {
        var time = new FakeTimeProvider(StartTime);
        time.SetLocalTimeZone(TimeZoneInfo.Utc);

        var services = new ServiceCollection();
        services.AddSingleton(time);
        services.AddSingleton<TimeProvider>(time);
        services.AddApplication();

        services.AddSingleton<IOccurrenceCalculator>(new FakeOccurrenceCalculator(TimeSpan.FromHours(1)));
        services.AddSingleton<InMemoryStore>();
        services.AddScoped<InMemoryUnitOfWork>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<InMemoryUnitOfWork>());
        services.AddScoped<IProcessRepository, InMemoryProcessRepository>();
        services.AddScoped<IRunRepository, InMemoryRunRepository>();
        services.AddScoped<ISettingsRepository, InMemorySettingsRepository>();
        services.AddScoped<InMemoryHistory>();
        services.AddScoped<IHistoryWriter>(sp => sp.GetRequiredService<InMemoryHistory>());
        services.AddScoped<IRunHistoryReader>(sp => sp.GetRequiredService<InMemoryHistory>());
        services.AddScoped<IHistoryRetention>(sp => sp.GetRequiredService<InMemoryHistory>());

        services.AddSingleton<FakeProcessLauncher>();
        services.AddSingleton<IProcessLauncher>(sp => sp.GetRequiredService<FakeProcessLauncher>());
        services.AddSingleton<ISecretProtector, FakeSecretProtector>();
        services.AddSingleton<RecordingEmailSender>();
        services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<RecordingEmailSender>());
        services.AddSingleton<RecordingAppNotifier>();
        services.AddSingleton<IAppNotifier>(sp => sp.GetRequiredService<RecordingAppNotifier>());
        services.AddSingleton<FakeServiceControl>();
        services.AddSingleton<IServiceControl>(sp => sp.GetRequiredService<FakeServiceControl>());
        services.AddSingleton<FakeLogRetention>();
        services.AddSingleton<ILogRetention>(sp => sp.GetRequiredService<FakeLogRetention>());
        services.AddSingleton<FakeRunOutputReader>();
        services.AddSingleton<IRunOutputReader>(sp => sp.GetRequiredService<FakeRunOutputReader>());

        configure?.Invoke(services);

        Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public IServiceProvider Services { get; }

    public InMemoryStore Store => Services.GetRequiredService<InMemoryStore>();

    public FakeProcessLauncher Launcher => Services.GetRequiredService<FakeProcessLauncher>();

    public FakeTimeProvider Time => Services.GetRequiredService<FakeTimeProvider>();

    public RecordingEmailSender Email => Services.GetRequiredService<RecordingEmailSender>();

    public RecordingAppNotifier AppNotifier => Services.GetRequiredService<RecordingAppNotifier>();

    public IServiceScope CreateScope() => Services.CreateScope();

    public ValueTask DisposeAsync() => ((ServiceProvider)Services).DisposeAsync();
}
