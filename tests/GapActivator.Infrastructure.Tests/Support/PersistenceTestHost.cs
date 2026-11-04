using GapActivator.Application;
using GapActivator.Application.Events;
using GapActivator.Application.Messaging;
using GapActivator.Application.Ports;
using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Scheduling;
using GapActivator.Infrastructure.Logging;
using GapActivator.Infrastructure.Persistence;
using GapActivator.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GapActivator.Infrastructure.Tests.Support;

/// <summary>
/// The application layer over the real SQLite persistence in a <see cref="TempDataRoot"/>, with every Plan 2 fake
/// that is not persistence and a fake clock at <see cref="ApplicationTestHost.StartTime"/>.
/// <see cref="Send{TCommand, TResult}"/> and <see cref="Query{TQuery, TResult}"/> go through the real dispatchers.
/// Call <see cref="InitializeAsync"/> (or use <see cref="StartAsync"/>) before the first command.
/// </summary>
public sealed class PersistenceTestHost : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly TempDataRoot? _ownedRoot;

    /// <param name="configure">Applied after the defaults, so it can replace any registration.</param>
    /// <param name="configureDb">Extra options of every context, for example an interceptor.</param>
    /// <param name="existingRoot">
    /// A data root to open instead of a new one. The caller owns it: disposing the host does not delete it, so a second
    /// host can open the same database afterwards.
    /// </param>
    public PersistenceTestHost(
        Action<IServiceCollection>? configure = null,
        Action<DbContextOptionsBuilder>? configureDb = null,
        TempDataRoot? existingRoot = null)
    {
        _ownedRoot = existingRoot is null ? new TempDataRoot() : null;
        Paths = new GapActivatorPaths((existingRoot ?? _ownedRoot!).Path);

        var time = new TrackingTimeProvider(ApplicationTestHost.StartTime);
        time.SetLocalTimeZone(TimeZoneInfo.Utc);

        var services = new ServiceCollection();
        services.AddSingleton(time);
        services.AddSingleton<FakeTimeProvider>(time);
        services.AddSingleton<TimeProvider>(time);
        services.AddApplication();
        services.AddPersistence(Paths, configureDb);

        services.AddSingleton<LogRecorder>();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(RecordingLogger<>));

        services.AddSingleton<IOccurrenceCalculator>(new FakeOccurrenceCalculator(TimeSpan.FromHours(1)));
        services.AddSingleton<FakeProcessLauncher>();
        services.AddSingleton<IProcessLauncher>(sp => sp.GetRequiredService<FakeProcessLauncher>());
        services.AddSingleton<ISecretProtector, FakeSecretProtector>();
        services.AddSingleton<RecordingEmailSender>();
        services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<RecordingEmailSender>());
        services.AddSingleton<RecordingAppNotifier>();
        services.AddSingleton<IAppNotifier>(sp => sp.GetRequiredService<RecordingAppNotifier>());
        services.AddSingleton<FakeServiceControl>();
        services.AddSingleton<IServiceControl>(sp => sp.GetRequiredService<FakeServiceControl>());

        // The log retention fake records into the in-memory store; nothing else of that store is registered.
        services.AddSingleton<InMemoryStore>();
        services.AddSingleton<FakeLogRetention>();
        services.AddSingleton<ILogRetention>(sp => sp.GetRequiredService<FakeLogRetention>());
        services.AddSingleton<FakeRunOutputReader>();
        services.AddSingleton<IRunOutputReader>(sp => sp.GetRequiredService<FakeRunOutputReader>());

        services.AddSingleton<RecordingNotificationHandler>();
        services.AddSingleton<INotificationHandler>(sp => sp.GetRequiredService<RecordingNotificationHandler>());

        configure?.Invoke(services);

        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public IServiceProvider Services => _services;

    public GapActivatorPaths Paths { get; }

    public FakeProcessLauncher Launcher => _services.GetRequiredService<FakeProcessLauncher>();

    public TrackingTimeProvider Time => _services.GetRequiredService<TrackingTimeProvider>();

    public RecordingAppNotifier AppNotifier => _services.GetRequiredService<RecordingAppNotifier>();

    /// <summary>Every event the dispatcher published, in order.</summary>
    public RecordingNotificationHandler Published => _services.GetRequiredService<RecordingNotificationHandler>();

    public LogRecorder Log => _services.GetRequiredService<LogRecorder>();

    public LogLevelSwitch LogLevel => _services.GetRequiredService<LogLevelSwitch>();

    /// <summary>Creates a host and runs the <see cref="DatabaseInitializer"/>.</summary>
    public static async Task<PersistenceTestHost> StartAsync(
        Action<IServiceCollection>? configure = null,
        Action<DbContextOptionsBuilder>? configureDb = null,
        TempDataRoot? existingRoot = null)
    {
        var host = new PersistenceTestHost(configure, configureDb, existingRoot);
        try
        {
            await host.InitializeAsync();
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = CreateScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(ct);
    }

    /// <summary>Sends a command through the real dispatcher. A null <paramref name="caller"/> means <see cref="Caller.System"/>.</summary>
    public Task<TResult> Send<TCommand, TResult>(TCommand command, Caller? caller = null, CancellationToken ct = default)
        where TCommand : ICommand<TResult> =>
        _services.GetRequiredService<ICommandDispatcher>().SendAsync<TCommand, TResult>(command, caller ?? Caller.System, ct);

    /// <summary>Runs a query through the real dispatcher. A null <paramref name="caller"/> means <see cref="Caller.System"/>.</summary>
    public Task<TResult> Query<TQuery, TResult>(TQuery query, Caller? caller = null, CancellationToken ct = default)
        where TQuery : IQuery<TResult> =>
        _services.GetRequiredService<IQueryDispatcher>().QueryAsync<TQuery, TResult>(query, caller ?? Caller.System, ct);

    /// <summary>A scope of its own: its repositories and unit of work share one context, as a command's do.</summary>
    public AsyncServiceScope CreateScope() => _services.CreateAsyncScope();

    /// <summary>A context of its own, with no interceptor, for raw checks. The caller disposes it.</summary>
    public GapActivatorDbContext NewScopeDb() =>
        new(new DbContextOptionsBuilder<GapActivatorDbContext>()
            .UseSqlite(GapActivatorDatabase.ConnectionString(Paths))
            .Options);

    /// <summary>Stages whatever <paramref name="stage"/> adds through the scope's ports, then commits the unit of work.</summary>
    public async Task SeedAsync(Action<IServiceProvider> stage, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stage);
        await using AsyncServiceScope scope = CreateScope();
        stage(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(ct);
    }

    /// <summary>Saves the processes with their schedules, bypassing the commands.</summary>
    public Task SeedAsync(params Process[] processes) =>
        SeedAsync(sp =>
        {
            IProcessRepository repository = sp.GetRequiredService<IProcessRepository>();
            foreach (Process process in processes)
            {
                ClearEvents(process);
                repository.Add(process);
            }
        });

    /// <summary>Saves the runs as they are, bypassing the commands.</summary>
    public Task SeedAsync(params Run[] runs) =>
        SeedAsync(sp =>
        {
            IRunRepository repository = sp.GetRequiredService<IRunRepository>();
            foreach (Run run in runs)
            {
                ClearEvents(run);
                repository.Add(run);
            }
        });

    /// <summary>Saves the history entries in this order, through the history writer of one scope.</summary>
    public Task SeedAsync(params HistoryEntry[] entries) =>
        SeedAsync(sp =>
        {
            IHistoryWriter writer = sp.GetRequiredService<IHistoryWriter>();
            foreach (HistoryEntry entry in entries)
            {
                writer.Add(entry);
            }
        });

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        GapActivatorDatabase.ReleasePooledConnections();
        if (_ownedRoot is not null)
        {
            await _ownedRoot.DisposeAsync();
        }
    }

    private static void ClearEvents<TId>(AggregateRoot<TId> aggregate)
        where TId : notnull => aggregate.ClearDomainEvents();
}

/// <summary>Records every event the dispatcher publishes.</summary>
public sealed class RecordingNotificationHandler : INotificationHandler
{
    private readonly List<IDomainEvent> _events = [];
    private readonly Lock _sync = new();

    public IReadOnlyList<IDomainEvent> Events
    {
        get
        {
            lock (_sync)
            {
                return [.. _events];
            }
        }
    }

    public Task HandleAsync(IDomainEvent domainEvent, CancellationToken ct)
    {
        lock (_sync)
        {
            _events.Add(domainEvent);
        }

        return Task.CompletedTask;
    }
}
