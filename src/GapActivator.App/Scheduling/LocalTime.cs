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
            offset = zone.GetUtcOffset(wall.AddHours(-1));
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
}
