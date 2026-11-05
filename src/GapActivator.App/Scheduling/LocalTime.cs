namespace GapActivator.App.Scheduling;

internal static class LocalTime
{
    /// <summary>
    /// The instant of a wall-clock time in <paramref name="zone"/>, resolved as the service resolves it (spec 6.4): a time that does not
    /// exist (spring forward) takes the offset before the gap, and an ambiguous one (fall back) takes its first pass, the larger offset.
    /// </summary>
    public static DateTimeOffset ToOffset(DateTime local, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        DateTime wall = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        TimeSpan offset;
        if (zone.IsInvalidTime(wall))
        {
            offset = PreGapOffset(wall, zone);
        }
        else if (zone.IsAmbiguousTime(wall))
        {
            offset = zone.GetAmbiguousTimeOffsets(wall).Max();
        }
        else
        {
            offset = zone.GetUtcOffset(wall);
        }

        return new DateTimeOffset(wall, offset);
    }

    // The offset in effect just before a spring-forward gap, found as the service finds it (a minute at a time, so a gap of any length
    // works). It is not GetUtcOffset of the missing time: in a zone whose daylight delta is negative that returns the wrong side.
    private static TimeSpan PreGapOffset(DateTime nonexistent, TimeZoneInfo zone)
    {
        DateTime beforeGap = nonexistent;
        while (zone.IsInvalidTime(beforeGap))
        {
            beforeGap = beforeGap.AddMinutes(-1);
        }

        return zone.GetUtcOffset(beforeGap);
    }
}
