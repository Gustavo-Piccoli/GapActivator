using GapActivator.Application;
using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Scheduling;
using GapActivator.Domain.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GapActivator.Testing;

/// <summary>
/// The application layer wired to in-memory fakes and a fake clock. <see cref="Send{TCommand, TResult}"/> and
/// <see cref="Query{TQuery, TResult}"/> go through the real dispatchers.
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

        services.AddSingleton<LogRecorder>();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(RecordingLogger<>));

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

    public LogRecorder Log => Services.GetRequiredService<LogRecorder>();

    /// <summary>Sends a command through the real dispatcher. A null <paramref name="caller"/> means <see cref="Caller.System"/>.</summary>
    public Task<TResult> Send<TCommand, TResult>(TCommand command, Caller? caller = null, CancellationToken ct = default)
        where TCommand : ICommand<TResult> =>
        Services.GetRequiredService<ICommandDispatcher>().SendAsync<TCommand, TResult>(command, caller ?? Caller.System, ct);

    /// <summary>Runs a query through the real dispatcher. A null <paramref name="caller"/> means <see cref="Caller.System"/>.</summary>
    public Task<TResult> Query<TQuery, TResult>(TQuery query, Caller? caller = null, CancellationToken ct = default)
        where TQuery : IQuery<TResult> =>
        Services.GetRequiredService<IQueryDispatcher>().QueryAsync<TQuery, TResult>(query, caller ?? Caller.System, ct);

    public IServiceScope CreateScope() => Services.CreateScope();

    public ValueTask DisposeAsync() => ((ServiceProvider)Services).DisposeAsync();
}
