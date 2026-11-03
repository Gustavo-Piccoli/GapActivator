using GapActivator.Domain.Scheduling;

namespace GapActivator.Testing;

/// <summary>Deterministic calculator: occurrences are <c>anchor + k * step</c>; the rule content is ignored.</summary>
public sealed class FakeOccurrenceCalculator(TimeSpan step, int? count = null) : IOccurrenceCalculator
{
    public DateTimeOffset? FirstOccurrence(RecurrenceRule rule, DateTimeOffset anchor) =>
        count is null or > 0 ? anchor : null;

    public DateTimeOffset? NextOccurrence(RecurrenceRule rule, DateTimeOffset anchor, DateTimeOffset after)
    {
        var index = after < anchor ? 0 : (long)((after - anchor) / step) + 1;
        return count is { } limit && index >= limit ? null : anchor + (index * step);
    }
}
