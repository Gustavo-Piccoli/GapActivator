using GapActivator.Domain.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GapActivator.Infrastructure.Persistence;

/// <summary>
/// The <c>Runs</c> table. <c>ProcessId</c> has no foreign key, because history outlives the process; a unique filtered
/// index allows at most one active run per process.
/// </summary>
internal sealed class RunConfiguration : IEntityTypeConfiguration<Run>
{
    public void Configure(EntityTypeBuilder<Run> builder)
    {
        builder.ToTable("Runs");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Ignore(r => r.DomainEvents);

        builder.Property(r => r.ProcessId);
        builder.Property(r => r.ProcessNameSnapshot).HasColumnName("ProcessName").IsRequired();
        builder.Property(r => r.Kind);
        builder.Property(r => r.Trigger);
        builder.Property(r => r.ScheduledFor);
        builder.Property(r => r.IsLate);
        builder.Property(r => r.State);
        builder.Property(r => r.StopReason);
        builder.Property(r => r.Pid);
        builder.Property(r => r.ProcessStartTime);
        builder.Property(r => r.CreatedAt);
        builder.Property(r => r.StartedAt);
        builder.Property(r => r.EndedAt);
        builder.Property(r => r.Outcome);
        builder.Property(r => r.ExitCode);
        builder.Property(r => r.Killed);
        builder.Property(r => r.Error);
        builder.Property<int>(GapActivatorDbContext.VersionProperty).IsConcurrencyToken();

        builder.HasIndex(r => r.ProcessId).IsUnique().HasFilter("\"State\" <> 'Completed'");
        builder.HasIndex(r => r.CreatedAt);
        builder.HasIndex(r => new { r.ProcessId, r.CreatedAt });
        builder.HasIndex(r => r.EndedAt);
    }
}
