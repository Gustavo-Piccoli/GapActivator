using GapActivator.Domain.Common;

namespace GapActivator.Domain.Tests.Common;

public class AggregateRootTests
{
    private sealed record TestEvent(int Number, DateTimeOffset OccurredAt) : IDomainEvent;

    private sealed class TestAggregate(Guid id) : AggregateRoot<Guid>(id)
    {
        public void Do(int number) => Raise(new TestEvent(number, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Raise_TwoEvents_KeepsOrder()
    {
        var aggregate = new TestAggregate(Guid.NewGuid());

        aggregate.Do(1);
        aggregate.Do(2);

        aggregate.DomainEvents.Cast<TestEvent>().Select(e => e.Number).ShouldBe([1, 2]);
    }

    [Fact]
    public void ClearDomainEvents_EmptiesList()
    {
        var aggregate = new TestAggregate(Guid.NewGuid());
        aggregate.Do(1);

        aggregate.ClearDomainEvents();

        aggregate.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Equality_SameId_IsEqual()
    {
        var id = Guid.NewGuid();

        new TestAggregate(id).ShouldBe(new TestAggregate(id));
        new TestAggregate(id).ShouldNotBe(new TestAggregate(Guid.NewGuid()));
    }
}
