using GapActivator.Application.Ports;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Settings;
using GapActivator.Infrastructure.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GapActivator.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQLite persistence under <paramref name="paths"/>: the context, the unit of work, the repositories,
    /// the history writer and the <see cref="DatabaseInitializer"/>, all scoped. The paths and the
    /// <see cref="LogLevelSwitch"/> are added only if the host has not registered its own.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="paths">Where the database file is.</param>
    /// <param name="configure">Extra options applied after <c>UseSqlite</c>, for example an interceptor in a test.</param>
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        GapActivatorPaths paths,
        Action<DbContextOptionsBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(paths);

        services.TryAddSingleton(paths);
        services.TryAddSingleton<LogLevelSwitch>();

        string connectionString = GapActivatorDatabase.ConnectionString(paths);
        services.AddDbContext<GapActivatorDbContext>(options =>
        {
            options.UseSqlite(connectionString);
            configure?.Invoke(options);
        });

        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IProcessRepository, EfProcessRepository>();
        services.AddScoped<IRunRepository, EfRunRepository>();
        services.AddScoped<ISettingsRepository, EfSettingsRepository>();
        services.AddScoped<IHistoryWriter, EfHistoryWriter>();
        services.AddScoped<DatabaseInitializer>();
        return services;
    }
}
