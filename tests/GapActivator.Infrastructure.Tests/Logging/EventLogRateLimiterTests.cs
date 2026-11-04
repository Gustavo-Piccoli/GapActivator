using GapActivator.Infrastructure.Logging;
using Microsoft.Extensions.Time.Testing;

namespace GapActivator.Infrastructure.Tests.Logging;

public sealed class EventLogRateLimiterTests
{
    // BI-090
    [Fact]
    public void TryAcquire_SameKeyWithinTheWindow_IsAdmittedOnce()
    {
        var time = new FakeTimeProvider();
        var limiter = new EventLogRateLimiter(time);

        limiter.TryAcquire("C", "m").ShouldBeTrue();
        limiter.TryAcquire("C", "m").ShouldBeFalse();
        limiter.TryAcquire("C", "other").ShouldBeTrue();
        limiter.TryAcquire("D", "m").ShouldBeTrue();
    }

    // BI-090
    [Fact]
    public void TryAcquire_AfterTheWindow_AdmitsTheKeyAgain()
    {
        var time = new FakeTimeProvider();
        var limiter = new EventLogRateLimiter(time);
        limiter.TryAcquire("C", "m").ShouldBeTrue();

        time.Advance(EventLogRateLimiter.Window - TimeSpan.FromMilliseconds(1));
        limiter.TryAcquire("C", "m").ShouldBeFalse();

        time.Advance(TimeSpan.FromMilliseconds(1));
        limiter.TryAcquire("C", "m").ShouldBeTrue();
    }

    // BI-090
    [Fact]
    public void RateLimiter_BeyondCapacity_StaysBoundedAndAdmitsNewKeys()
    {
        var time = new FakeTimeProvider();
        var limiter = new EventLogRateLimiter(time);

        for (var i = 0; i <= EventLogRateLimiter.Capacity; i++)
        {
            limiter.TryAcquire("C", "m" + i).ShouldBeTrue();
            time.Advance(TimeSpan.FromSeconds(1));
        }

        limiter.TryAcquire("C", "new").ShouldBeTrue();
        limiter.Count.ShouldBeLessThanOrEqualTo(EventLogRateLimiter.Capacity);
        limiter.TryAcquire("C", "m0").ShouldBeTrue();
    }

    [Fact]
    public void TryAcquire_FullAndAllKeysExpired_RemovesTheExpiredOnes()
    {
        var time = new FakeTimeProvider();
        var limiter = new EventLogRateLimiter(time);
        for (var i = 0; i < EventLogRateLimiter.Capacity; i++)
        {
            limiter.TryAcquire("C", "m" + i).ShouldBeTrue();
        }

        time.Advance(EventLogRateLimiter.Window);
        limiter.TryAcquire("C", "fresh").ShouldBeTrue();

        limiter.Count.ShouldBe(1);
    }
}
