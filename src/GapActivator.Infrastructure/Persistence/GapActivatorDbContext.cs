using GapActivator.Application.Events;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Scheduling;
using GapActivator.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GapActivator.Infrastructure.Persistence;

/// <summary>
/// The SQLite model. Saving stamps the shadow columns: the upper-invariant name and executable keys of each added or
/// modified process, and the next <c>Version</c> of each modified process, run or settings row, and of each process
/// whose schedules alone were added, changed or removed.
/// </summary>
public sealed class GapActivatorDbContext(DbContextOptions<GapActivatorDbContext> options) : DbContext(options)
{
    internal const string VersionProperty = "Version";

    public DbSet<Process> Processes => Set<Process>();

    public DbSet<Run> Runs => Set<Run>();

    public DbSet<InstallationSettings> Settings => Set<InstallationSettings>();

    public DbSet<HistoryEntry> HistoryEntries => Set<HistoryEntry>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampShadowProperties();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampShadowProperties();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ValueConversions.Apply(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var processes = new ProcessConfiguration();
        modelBuilder.ApplyConfiguration<Process>(processes);
        modelBuilder.ApplyConfiguration<Domain.Scheduling.Schedule>(processes);
        modelBuilder.ApplyConfiguration<Domain.Scheduling.OneTimeSchedule>(processes);
        modelBuilder.ApplyConfiguration<Domain.Scheduling.RecurringSchedule>(processes);
        modelBuilder.ApplyConfiguration(new RunConfiguration());
        modelBuilder.ApplyConfiguration(new SettingsConfiguration());
        modelBuilder.ApplyConfiguration(new HistoryEntryConfiguration());
    }

    private void StampShadowProperties()
    {
        ChangeTracker.DetectChanges();

        // Materialized: bumping the version of an unchanged process moves its entry to Modified.
        EntityEntry[] entries = [.. ChangeTracker.Entries()];
        HashSet<ProcessId> scheduleChanged = [.. entries
            .Where(e => e.Entity is Schedule && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e =>
            {
                var owner = e.Property(ProcessConfiguration.ProcessIdColumn);
                return (ProcessId)(e.State == EntityState.Deleted ? owner.OriginalValue : owner.CurrentValue)!;
            })];

        foreach (var entry in entries)
        {
            if (entry.Entity is Process process && entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Property(ProcessConfiguration.NameKey).CurrentValue = process.Name.Value.ToUpperInvariant();
                entry.Property(ProcessConfiguration.ExecutableKey).CurrentValue =
                    process.Launch.Executable.Value.ToUpperInvariant();
            }

            // A change to the schedules alone still bumps the owning process, so its token guards them (BI-042).
            bool changed = entry.State == EntityState.Modified && entry.Entity is Process or Run or InstallationSettings;
            bool schedulesChanged = entry.State == EntityState.Unchanged
                && entry.Entity is Process owner
                && scheduleChanged.Contains(owner.Id);
            if (changed || schedulesChanged)
            {
                var version = entry.Property(VersionProperty);
                version.CurrentValue = (int)version.OriginalValue! + 1;
            }
        }
    }
}
