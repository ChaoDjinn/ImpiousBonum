namespace ImpiousBonum.Core.Metrics;

/// <summary>How far back a graph goes when it's longer than the readings a second apart that every metric keeps.</summary>
public enum LongSpan
{
    /// <summary>15 minutes, a point every 10 seconds.</summary>
    FifteenMinutes,

    /// <summary>An hour, a point every 10 seconds.</summary>
    Hour,

    /// <summary>6 hours, a point a minute.</summary>
    SixHours,

    /// <summary>24 hours, a point every 5 minutes.</summary>
    Day,
}

public static class LongSpans
{
    /// <summary>How many points a graph over <paramref name="span"/> has: one a step.</summary>
    public static int Points(this LongSpan span) => AveragedTier.Of(span).Points;
}

/// <summary>
/// A metric's history over a <see cref="LongSpan"/>, oldest first: each point is the average of the readings in its
/// step, with the lowest and highest of them, so a spike shorter than a step still shows. A step with no readings is a
/// gap (<see cref="double.NaN"/> in all three). The last point is the step under way, while it has readings.
/// </summary>
public sealed record AveragedHistory(double[] Average, double[] Low, double[] High, TimeSpan Step)
{
    public static AveragedHistory Empty(TimeSpan step) => new([], [], [], step);
}

/// <summary>
/// One step size of averaged history, kept as a ring of steps closed so far plus the one under way. Steps follow the
/// clock (10:00:00 to 10:00:10, and so on), not the readings, so steps without readings stay as gaps. Not thread-safe:
/// <see cref="MetricStore"/> holds its lock.
/// </summary>
internal sealed class AveragedTier(TimeSpan step, int capacity)
{
    /// <summary>The step sizes kept, and how many steps of each: an hour of 10 s, 6 hours of a minute, 24 hours of 5 minutes.</summary>
    internal static readonly (TimeSpan Step, int Capacity)[] Sizes =
    [
        (TimeSpan.FromSeconds(10), 360),
        (TimeSpan.FromMinutes(1), 360),
        (TimeSpan.FromMinutes(5), 288),
    ];

    /// <summary>Which of <see cref="Sizes"/> a span reads, and how many of its steps.</summary>
    internal static (int Size, int Points) Of(LongSpan span) => span switch
    {
        LongSpan.FifteenMinutes => (0, 90),
        LongSpan.Hour => (0, 360),
        LongSpan.SixHours => (1, 360),
        LongSpan.Day => (2, 288),
        _ => throw new ArgumentOutOfRangeException(nameof(span)),
    };

    private readonly RingBuffer _average = new(capacity);
    private readonly RingBuffer _low = new(capacity);
    private readonly RingBuffer _high = new(capacity);
    private long _current = -1;
    private double _sum;
    private int _count;
    private double _lowest;
    private double _highest;

    public TimeSpan Step => step;

    public void Add(DateTimeOffset now, double value)
    {
        MoveTo(now);
        _sum += value;
        _lowest = _count == 0 ? value : Math.Min(_lowest, value);
        _highest = _count == 0 ? value : Math.Max(_highest, value);
        _count++;
    }

    /// <summary>For made-up readings: these steps as the ones closed up to now, oldest first, with none under way.</summary>
    public void Seed(DateTimeOffset now, IEnumerable<(double Average, double Low, double High)> steps)
    {
        foreach (var (average, low, high) in steps)
        {
            _average.Add(average);
            _low.Add(low);
            _high.Add(high);
        }
        _current = now.UtcTicks / step.Ticks;
        _count = 0;
    }

    /// <summary>The latest <paramref name="points"/> steps, oldest first, ending with the one under way if it has readings.</summary>
    public AveragedHistory Latest(DateTimeOffset now, int points)
    {
        MoveTo(now);
        var closed = Math.Min(_count > 0 ? points - 1 : points, _average.Count);
        var average = _average.Latest(closed);
        var low = _low.Latest(closed);
        var high = _high.Latest(closed);
        if (_count > 0)
        {
            average = [.. average, _sum / _count];
            low = [.. low, _lowest];
            high = [.. high, _highest];
        }
        return new AveragedHistory(average, low, high, step);
    }

    /// <summary>Once the clock is past the step under way, closes it, and any steps since with no readings, as gaps.</summary>
    private void MoveTo(DateTimeOffset now)
    {
        var index = now.UtcTicks / step.Ticks;
        if (_current < 0)
        {
            _current = index;
            return;
        }
        // Within the step, or the clock went back: carry on with the step under way.
        if (index <= _current)
            return;

        if (_count > 0)
        {
            _average.Add(_sum / _count);
            _low.Add(_lowest);
            _high.Add(_highest);
        }
        else
            AddGap();
        // Steps that went by with no readings at all, say while the PC slept, up to a ringful.
        for (var gap = Math.Min(index - _current - 1, capacity); gap > 0; gap--)
            AddGap();
        _current = index;
        _sum = 0;
        _count = 0;
    }

    private void AddGap()
    {
        _average.Add(double.NaN);
        _low.Add(double.NaN);
        _high.Add(double.NaN);
    }
}
