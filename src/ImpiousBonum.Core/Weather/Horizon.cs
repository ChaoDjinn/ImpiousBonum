namespace ImpiousBonum.Core.Weather;

/// <summary>Finds when the sun or the moon crosses the horizon, from how high it is over time.</summary>
internal static class Horizon
{
    /// <summary>How far apart it looks: short enough that neither can rise and set again in between.</summary>
    private static readonly TimeSpan Step = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Each time <paramref name="height"/> (above where it counts as risen) crosses 0 between <paramref name="from"/>
    /// and <paramref name="to"/>, in order, and whether it's rising then. Each is found to within a second.
    /// </summary>
    public static IEnumerable<(DateTimeOffset Time, bool Rising)> Crossings(Func<DateTimeOffset, double> height, DateTimeOffset from, DateTimeOffset to)
    {
        var start = from;
        var before = height(start);
        while (start < to)
        {
            var end = start + Step < to ? start + Step : to;
            var after = height(end);
            if ((before < 0) != (after < 0))
                yield return (Halve(height, start, end, before < 0), before < 0);
            (start, before) = (end, after);
        }
    }

    /// <summary>The first rise and the first set between <paramref name="from"/> and <paramref name="to"/>; either is null when there's none.</summary>
    public static (DateTimeOffset? Rise, DateTimeOffset? Set) FirstRiseAndSet(Func<DateTimeOffset, double> height, DateTimeOffset from, DateTimeOffset to)
    {
        DateTimeOffset? rise = null, set = null;
        foreach (var (time, rising) in Crossings(height, from, to))
        {
            if (rising)
                rise ??= time;
            else
                set ??= time;
            if (rise is not null && set is not null)
                break;
        }
        return (rise, set);
    }

    /// <summary>Narrows down when <paramref name="height"/> crosses 0 between <paramref name="start"/> and <paramref name="end"/>.</summary>
    private static DateTimeOffset Halve(Func<DateTimeOffset, double> height, DateTimeOffset start, DateTimeOffset end, bool rising)
    {
        while (end - start > TimeSpan.FromSeconds(1))
        {
            var middle = start + (end - start) / 2;
            if ((height(middle) < 0) == rising)
                start = middle;
            else
                end = middle;
        }
        return start + (end - start) / 2;
    }
}
