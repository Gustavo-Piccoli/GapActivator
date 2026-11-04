using GapActivator.Application.Events;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace GapActivator.Infrastructure.Persistence;

/// <summary>
/// The SQLite model. Saving stamps the shadow columns: the upper-invariant name and executable keys of each added or
/// modified process, and the next <c>Version</c> of each modified process, run or settings row.
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
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is Process process && entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Property(ProcessConfiguration.NameKey).CurrentValue = process.Name.Value.ToUpperInvariant();
                entry.Property(ProcessConfiguration.ExecutableKey).CurrentValue =
                    process.Launch.Executable.Value.ToUpperInvariant();
            }

            if (entry.State == EntityState.Modified && entry.Entity is Process or Run or InstallationSettings)
            {
                var version = entry.Property(VersionProperty);
                version.CurrentValue = (int)version.OriginalValue! + 1;
            }
        }
    }
}
