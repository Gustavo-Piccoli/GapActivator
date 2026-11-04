using GapActivator.Domain.Settings;
using GapActivator.Infrastructure.Logging;
using Microsoft.EntityFrameworkCore;

namespace GapActivator.Infrastructure.Persistence;

/// <summary>
/// Prepares the data root at startup: folders, schema, WAL journal, the settings row and the runtime log level.
/// Idempotent.
/// </summary>
public sealed class DatabaseInitializer(GapActivatorDbContext db, GapActivatorPaths paths, LogLevelSwitch logLevel)
{
    private const int SettingsId = 1;

    public async Task InitializeAsync(CancellationToken ct)
    {
        paths.EnsureDirectories();
        await db.Database.MigrateAsync(ct).ConfigureAwait(false);

        // The journal mode is stored in the database file, so later connections open it in WAL mode too.
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct).ConfigureAwait(false);

        var settings = await db.Settings.SingleOrDefaultAsync(s => s.Id == SettingsId, ct).ConfigureAwait(false);
        if (settings is null)
        {
            settings = InstallationSettings.Default();
            db.Settings.Add(settings);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        logLevel.Set(settings.LogLevel);
    }
}
