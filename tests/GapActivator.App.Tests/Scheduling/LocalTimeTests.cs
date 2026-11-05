using GapActivator.App.Scheduling;
using GapActivator.App.Tests.Fakes;

namespace GapActivator.App.Tests.Scheduling;

public class LocalTimeTests
{
    private static DateTime Local(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, DateTimeKind.Unspecified);

    [Fact]
    public void ToOffset_NonexistentLocalTime_UsesThePreGapOffset()
    {
        // 2030-11-03 is the first Sunday of November: 00:00 jumps to 01:00, so 00:30 does not exist.
        DateTime local = Local(2030, 11, 3, 0, 30);
        Zones.SouthernLike.IsInvalidTime(local).ShouldBeTrue();

        DateTimeOffset at = LocalTime.ToOffset(local, Zones.SouthernLike);

        at.Offset.ShouldBe(TimeSpan.FromHours(-3));
    }

    [Fact]
    public void ToOffset_AmbiguousLocalTime_UsesTheFirstPass()
    {
        // 2030-03-03 is the first Sunday of March: 00:00 falls back to 23:00, so Saturday 23:30 happens twice.
        DateTime local = Local(2030, 3, 2, 23, 30);
        Zones.SouthernLike.IsAmbiguousTime(local).ShouldBeTrue();

        DateTimeOffset at = LocalTime.ToOffset(local, Zones.SouthernLike);

        at.Offset.ShouldBe(TimeSpan.FromHours(-2));
    }

    [Theory]
    [InlineData(2030, 1, 15, -2)]
    [InlineData(2030, 7, 15, -3)]
    public void ToOffset_OrdinaryLocalTime_UsesTheZoneOffset(int y, int mo, int d, int hours)
    {
        // January is summer in the southern-shaped zone.
        DateTimeOffset at = LocalTime.ToOffset(Local(y, mo, d, 12, 0), Zones.SouthernLike);

        at.Offset.ShouldBe(TimeSpan.FromHours(hours));
        at.DateTime.ShouldBe(Local(y, mo, d, 12, 0));
    }

    [Fact]
    public void ToOffset_NorthernSpringForward_UsesTheOffsetBeforeTheGap()
    {
        DateTimeOffset at = LocalTime.ToOffset(Local(2030, 3, 10, 2, 30), Zones.NorthernLike);

        at.Offset.ShouldBe(TimeSpan.FromHours(-5));
    }

    [Fact]
    public void ToOffset_LocalKind_IsReadAsTheWallClockOfTheZone()
    {
        DateTime local = new(2030, 7, 15, 12, 0, 0, DateTimeKind.Local);

        LocalTime.ToOffset(local, Zones.Fixed(TimeSpan.FromHours(-3))).Offset.ShouldBe(TimeSpan.FromHours(-3));
    }
}
