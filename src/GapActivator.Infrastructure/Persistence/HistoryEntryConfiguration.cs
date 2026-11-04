using GapActivator.Application.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GapActivator.Infrastructure.Persistence;

/// <summary>
/// The <c>HistoryEntries</c> table. The shadow <c>Ordinal</c> keeps the insertion order of entries that share an
/// instant, because version 7 GUIDs are not monotonic within one millisecond.
/// </summary>
internal sealed class HistoryEntryConfiguration : IEntityTypeConfiguration<HistoryEntry>
{
    public const string OrdinalProperty = "Ordinal";

    public void Configure(EntityTypeBuilder<HistoryEntry> builder)
    {
        builder.ToTable("HistoryEntries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.OccurredAt);
        builder.Property<int>(OrdinalProperty);
        builder.Property(e => e.Kind);
        builder.Property(e => e.ProcessId);
        builder.Property(e => e.RunId);
        builder.Property(e => e.Outcome);
        builder.Property(e => e.ExitCode);
        builder.Property(e => e.Message);

        builder.HasIndex(e => e.RunId);
        builder.HasIndex(e => e.OccurredAt);
    }
}
