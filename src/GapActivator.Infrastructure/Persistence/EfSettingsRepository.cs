using GapActivator.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace GapActivator.Infrastructure.Persistence;

/// <summary>
/// The single settings row (Id 1), tracked. The <see cref="DatabaseInitializer"/> seeds it; this repository never
/// inserts it, so the ungated readers cannot race an insert.
/// </summary>
internal sealed class EfSettingsRepository(GapActivatorDbContext db) : ISettingsRepository
{
    private const int SettingsId = 1;

    public async Task<InstallationSettings> GetAsync(CancellationToken ct) =>
        await db.Settings.SingleOrDefaultAsync(s => s.Id == SettingsId, ct).ConfigureAwait(false)
        ?? throw new InvalidOperationException("The database is not initialized.");
}
