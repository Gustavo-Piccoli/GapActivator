using GapActivator.Ipc.Contracts;
using GapActivator.Service.Ipc;
using Microsoft.Extensions.Time.Testing;

namespace GapActivator.Service.Tests.Ipc;

public sealed class StateChangedCoalescerTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    private static (StateChangedCoalescer Coalescer, FakeTimeProvider Time, List<StateChangedDto> Sent) Create()
    {
        var time = new FakeTimeProvider();
        var sent = new List<StateChangedDto>();
        var coalescer = new StateChangedCoalescer(Interval, time, hint =>
        {
            lock (sent)
            {
                sent.Add(hint);
            }
        });
        return (coalescer, time, sent);
    }

    [Fact]
    public void Post_AfterQuiet_SendsAtOnce()
    {
        (StateChangedCoalescer coalescer, _, List<StateChangedDto> sent) = Create();
        using (coalescer)
        {
            var hint = new StateChangedDto(Guid.NewGuid(), null);

            coalescer.Post(hint);

            sent.ShouldBe([hint]);
        }
    }

    [Fact]
    public void Post_BurstWithinTheInterval_SendsOneMoreAtTheIntervalEnd()
    {
        (StateChangedCoalescer coalescer, FakeTimeProvider time, List<StateChangedDto> sent) = Create();
        using (coalescer)
        {
            for (int i = 0; i < 10; i++)
            {
                coalescer.Post(new StateChangedDto(null, null));
                time.Advance(TimeSpan.FromMilliseconds(10));
            }

            sent.Count.ShouldBe(1);

            time.Advance(TimeSpan.FromMilliseconds(149));
            sent.Count.ShouldBe(1);

            time.Advance(TimeSpan.FromMilliseconds(1));
            sent.Count.ShouldBe(2);

            time.Advance(TimeSpan.FromSeconds(1));
            sent.Count.ShouldBe(2);
        }
    }

    [Fact]
    public void Post_MergedHintsWithDifferentIds_SendNullIds()
    {
        (StateChangedCoalescer coalescer, FakeTimeProvider time, List<StateChangedDto> sent) = Create();
        using (coalescer)
        {
            coalescer.Post(new StateChangedDto(null, null));
            coalescer.Post(new StateChangedDto(Guid.NewGuid(), Guid.NewGuid()));
            coalescer.Post(new StateChangedDto(Guid.NewGuid(), Guid.NewGuid()));

            time.Advance(Interval);

            sent.Count.ShouldBe(2);
            sent[1].ShouldBe(new StateChangedDto(null, null));
        }
    }

    [Fact]
    public void Post_MergedHintsWithTheSameIds_KeepThem()
    {
        (StateChangedCoalescer coalescer, FakeTimeProvider time, List<StateChangedDto> sent) = Create();
        using (coalescer)
        {
            var hint = new StateChangedDto(Guid.NewGuid(), Guid.NewGuid());
            coalescer.Post(new StateChangedDto(null, null));
            coalescer.Post(hint);
            coalescer.Post(hint);

            time.Advance(Interval);

            sent.Count.ShouldBe(2);
            sent[1].ShouldBe(hint);
        }
    }

    [Fact]
    public void Dispose_CancelsAPendingSend()
    {
        (StateChangedCoalescer coalescer, FakeTimeProvider time, List<StateChangedDto> sent) = Create();
        coalescer.Post(new StateChangedDto(null, null));
        coalescer.Post(new StateChangedDto(null, null));

        coalescer.Dispose();
        time.Advance(TimeSpan.FromSeconds(1));
        coalescer.Post(new StateChangedDto(null, null));

        sent.Count.ShouldBe(1);
    }
}
