using GapActivator.App.Services;
using GapActivator.App.Tests.Fakes;
using GapActivator.Ipc.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace GapActivator.App.Tests.Services;

public class FailureAcknowledgementsTests
{
    private static readonly DateTimeOffset Start = Dtos.Epoch;

    // Minutes after the start: when the history was seen, when this process was seen, and when the failure ended.
    [Theory]
    [InlineData(null, null, 5, true)]
    [InlineData(10, null, 5, false)]
    [InlineData(10, null, 15, true)]
    [InlineData(null, 10, 5, false)]
    [InlineData(null, 10, 15, true)]
    [InlineData(10, 20, 15, false)]
    [InlineData(20, 10, 15, false)]
    [InlineData(10, 20, 25, true)]
    public void IsUnseen_FollowsHistoryAndProcessMarks(int? historyAt, int? processAt, int endedAt, bool expected)
    {
        var time = new FakeTimeProvider(Start);
        var seen = new FailureAcknowledgements(time);
        var processId = Guid.NewGuid();
        RunRowDto failure = Dtos.RunRow(processId: processId, outcome: RunOutcome.Failed, endedAt: Start.AddMinutes(endedAt));

        // The marks happen in the order of their times.
        foreach ((int at, Action mark) in new (int?, Action)[] { (historyAt, seen.MarkHistorySeen), (processAt, () => seen.MarkProcessSeen(processId)) }
                     .Where(m => m.Item1 is not null).OrderBy(m => m.Item1).Select(m => (m.Item1!.Value, m.Item2)))
        {
            time.SetUtcNow(Start.AddMinutes(at));
            mark();
        }

        seen.IsUnseen(failure).ShouldBe(expected);
    }

    [Fact]
    public void IsUnseen_AnotherProcessMark_DoesNotCount()
    {
        var seen = new FailureAcknowledgements(new FakeTimeProvider(Start.AddHours(1)));
        seen.MarkProcessSeen(Guid.NewGuid());

        seen.IsUnseen(Dtos.RunRow(outcome: RunOutcome.Failed, endedAt: Start)).ShouldBeTrue();
    }

    [Fact]
    public void IsUnseen_FailureWithoutAnEnd_UsesTheCreationTime()
    {
        var seen = new FailureAcknowledgements(new FakeTimeProvider(Start.AddMinutes(1)));
        seen.MarkHistorySeen();

        // CreatedAt is the epoch, before the mark.
        RunRowDto failure = Dtos.RunRow(state: RunState.Running, outcome: RunOutcome.Failed) with { EndedAt = null };

        seen.IsUnseen(failure).ShouldBeFalse();
    }

    [Fact]
    public void Marks_RaiseChanged()
    {
        var seen = new FailureAcknowledgements(new FakeTimeProvider(Start));
        var raised = 0;
        seen.Changed += (_, _) => raised++;

        seen.MarkHistorySeen();
        seen.MarkProcessSeen(Guid.NewGuid());

        raised.ShouldBe(2);
    }
}
