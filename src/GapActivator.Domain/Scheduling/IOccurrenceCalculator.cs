namespace GapActivator.Domain.Scheduling;

/// <summary>Port that expands a <see cref="RecurrenceRule"/> into concrete instants.</summary>
public interface IOccurrenceCalculator
{
    /// <summary>The first occurrence at or after <paramref name="anchor"/>, or null when there is none.</summary>
    DateTimeOffset? FirstOccurrence(RecurrenceRule rule, DateTimeOffset anchor);

    /// <summary>The first occurrence strictly after <paramref name="after"/>, or null when the rule is exhausted.</summary>
    DateTimeOffset? NextOccurrence(RecurrenceRule rule, DateTimeOffset anchor, DateTimeOffset after);
}
