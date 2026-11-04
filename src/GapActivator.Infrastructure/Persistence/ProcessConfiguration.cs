using GapActivator.Domain.Processes;
using GapActivator.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GapActivator.Infrastructure.Persistence;

/// <summary>The <c>Processes</c> table and its <c>Schedules</c> (one table, TPH).</summary>
internal sealed class ProcessConfiguration
    : IEntityTypeConfiguration<Process>, IEntityTypeConfiguration<Schedule>,
      IEntityTypeConfiguration<OneTimeSchedule>, IEntityTypeConfiguration<RecurringSchedule>
{
    public const string NameKey = "NameKey";
    public const string ExecutableKey = "ExecutableKey";
    public const string ProcessIdColumn = "ProcessId";

    public void Configure(EntityTypeBuilder<Process> builder)
    {
        builder.ToTable("Processes");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Ignore(p => p.DomainEvents);

        builder.Property(p => p.Name).IsRequired();
        builder.Property<string>(NameKey).IsRequired();
        builder.HasIndex(NameKey).IsUnique();
        builder.Property(p => p.Description).IsRequired();
        builder.Property(p => p.Kind);

        builder.ComplexProperty(p => p.Launch, launch =>
        {
            launch.Property(l => l.Executable).HasColumnName("Executable").IsRequired();
            launch.Property(l => l.Arguments).HasColumnName("Arguments").IsRequired();
            launch.Property(l => l.WorkingDirectory).HasColumnName("WorkingDirectory");
            launch.Property(l => l.Environment).HasColumnName("Environment").IsRequired();
        });
        builder.Property<string>(ExecutableKey).IsRequired();
        builder.HasIndex(ExecutableKey);

        builder.ComplexProperty(p => p.Limits, limits =>
        {
            limits.Property(l => l.TimeLimit).HasColumnName("TimeLimit");
            limits.Property(l => l.GracePeriod).HasColumnName("GracePeriod");
        });

        builder.Property(p => p.Lifecycle);
        builder.Property(p => p.ConsecutiveFailures);
        builder.Property(p => p.RetryPending);
        builder.Property(p => p.CreatedAt);
        builder.Property(p => p.UpdatedAt);
        builder.Property<int>(GapActivatorDbContext.VersionProperty).IsConcurrencyToken();

        builder.HasMany(p => p.Schedules)
            .WithOne()
            .HasForeignKey(ProcessIdColumn)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Schedules)
            .HasField("_schedules")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();
    }

    public void Configure(EntityTypeBuilder<Schedule> builder)
    {
        builder.ToTable("Schedules");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.HasDiscriminator<string>("Kind")
            .HasValue<OneTimeSchedule>("OneTime")
            .HasValue<RecurringSchedule>("Recurring");
        builder.Property(s => s.NextOccurrenceAt);
    }

    public void Configure(EntityTypeBuilder<OneTimeSchedule> builder)
    {
        builder.Property(s => s.At).HasColumnName("At");
        builder.Property(s => s.FulfilledAt).HasColumnName("FulfilledAt");
    }

    public void Configure(EntityTypeBuilder<RecurringSchedule> builder)
    {
        builder.Property(s => s.Rule).HasColumnName("Rule");
        builder.Property(s => s.Anchor).HasColumnName("Anchor");
    }
}
