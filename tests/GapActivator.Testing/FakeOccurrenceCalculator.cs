using GapActivator.Domain.Scheduling;

namespace GapActivator.Testing;

/// <summary>Deterministic calculator: occurrences are <c>anchor + k * step</c>; the rule content is ignored.</summary>
public sealed class FakeOccurrenceCalculator : IOccurrenceCalculator
{
    private readonly TimeSpan _step;
    private readonly int? _count;

    public FakeOccurrenceCalculator(TimeSpan step, int? count = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, TimeSpan.Zero);
        _step = step;
        _count = count;
    }

    public DateTimeOffset? FirstOccurrence(RecurrenceRule rule, DateTimeOffset anchor) =>
        _count is null or > 0 ? anchor : null;

    public DateTimeOffset? NextOccurrence(RecurrenceRule rule, DateTimeOffset anchor, DateTimeOffset after)
    {
        var index = after < anchor ? 0 : (long)((after - anchor) / _step) + 1;
        return _count is { } limit && index >= limit ? null : anchor + (index * _step);
    }
}
