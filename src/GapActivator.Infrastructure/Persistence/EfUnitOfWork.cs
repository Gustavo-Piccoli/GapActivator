using GapActivator.Application.Ports;
using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Settings;
using GapActivator.Infrastructure.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GapActivator.Infrastructure.Persistence;

/// <summary>
/// The unit of work of one scope over its <see cref="GapActivatorDbContext"/>. A commit is one SaveChanges, so EF's
/// implicit transaction holds the aggregates and their history entries together. A
/// <see cref="DbUpdateConcurrencyException"/> propagates untranslated (BI-042): the dispatcher drops the scope and the
/// caller retries with a fresh one. After a commit that saved the settings, the runtime log level follows them.
/// </summary>
internal sealed class EfUnitOfWork(GapActivatorDbContext db, LogLevelSwitch logLevel) : IUnitOfWork
{
    public IReadOnlyList<IDomainEvent> DequeueDomainEvents()
    {
        var events = new List<IDomainEvent>();

        // Every state counts, Deleted included: the events of a removed aggregate are still published.
        foreach (EntityEntry entry in db.ChangeTracker.Entries().ToList())
        {
            switch (entry.Entity)
            {
                case Process process:
                    events.AddRange(process.DomainEvents);
                    process.ClearDomainEvents();
                    break;
                case Run run:
                    events.AddRange(run.DomainEvents);
                    run.ClearDomainEvents();
                    break;
                case InstallationSettings settings:
                    events.AddRange(settings.DomainEvents);
                    settings.ClearDomainEvents();
                    break;
            }
        }

        return events;
    }

    public async Task CommitAsync(CancellationToken ct)
    {
        db.ChangeTracker.DetectChanges();
        InstallationSettings? savedSettings = db.ChangeTracker.Entries<InstallationSettings>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .Select(e => e.Entity)
            .SingleOrDefault();

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (savedSettings is not null)
        {
            logLevel.Set(savedSettings.LogLevel);
        }
    }
}
