using GapActivator.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GapActivator.Infrastructure.Persistence;

/// <summary>The single-row <c>Settings</c> table (Id 1); the e-mail settings are one JSON column.</summary>
internal sealed class SettingsConfiguration : IEntityTypeConfiguration<InstallationSettings>
{
    public void Configure(EntityTypeBuilder<InstallationSettings> builder)
    {
        builder.ToTable("Settings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Ignore(s => s.DomainEvents);

        builder.Property(s => s.CycleInterval);
        builder.Property(s => s.FailureLimit);
        builder.Property(s => s.DefaultTimeLimit);
        builder.Property(s => s.DefaultGracePeriod);
        builder.Property(s => s.LogRetentionDays);
        builder.Property(s => s.HistoryRetentionDays);
        builder.Property(s => s.LogLevel);
        builder.Property(s => s.Email).IsRequired();
        builder.Property(s => s.CheckForUpdates);
        builder.Property(s => s.IsPaused);
        builder.Property<int>(GapActivatorDbContext.VersionProperty).IsConcurrencyToken();
    }
}
