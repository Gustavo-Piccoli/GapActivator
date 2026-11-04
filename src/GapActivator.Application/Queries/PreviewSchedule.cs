using GapActivator.Application.Messaging;
using GapActivator.Domain.Common;
using GapActivator.Domain.Scheduling;

namespace GapActivator.Application.Queries;

public sealed record PreviewSchedule(string Rule, DateTimeOffset Anchor, int Count) : IQuery<IReadOnlyList<DateTimeOffset>>;

internal sealed class PreviewScheduleHandler(IOccurrenceCalculator calculator, TimeProvider time)
    : IQueryHandler<PreviewSchedule, IReadOnlyList<DateTimeOffset>>
{
    private const int MaxCount = 50;

    public Task<IReadOnlyList<DateTimeOffset>> HandleAsync(PreviewSchedule query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Count is < 1 or > MaxCount)
        {
            throw new DomainException($"Count must be between 1 and {MaxCount}.");
        }

        RecurrenceRule rule = RecurrenceRule.Parse(query.Rule);
        DateTimeOffset now = time.GetUtcNow();

        // Q-13: the domain decides the first occurrence at or after save time; COUNT still counts from the anchor.
        DateTimeOffset notBefore = query.Anchor > now ? query.Anchor : now;
        DateTimeOffset? next = RecurringSchedule.Create(rule, query.Anchor, notBefore, calculator).NextOccurrenceAt;

        var result = new List<DateTimeOffset>(query.Count);
        while (next is { } occurrence && result.Count < query.Count)
        {
            result.Add(occurrence);
            next = calculator.NextOccurrence(rule, query.Anchor, occurrence);
        }

        return Task.FromResult<IReadOnlyList<DateTimeOffset>>(result);
    }
}
