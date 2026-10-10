namespace ImpiousBonum.Core.Metrics;

/// <summary>Short, readable durations for text metrics: "<1m", "43m", "2h 14m", "3d 5h".</summary>
public static class DurationText
{
    public static string Format(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1))
            return "<1m";
        if (span < TimeSpan.FromHours(1))
            return $"{(int)span.TotalMinutes}m";
        if (span < TimeSpan.FromDays(1))
            return $"{(int)span.TotalHours}h {span.Minutes}m";
        return $"{(int)span.TotalDays}d {span.Hours}h";
    }
}
