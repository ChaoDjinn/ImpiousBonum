namespace ImpiousBonum.Core.Energy;

/// <summary>
/// Adds the PC's power up over time into the energy it has used today and this month, in kWh. Only the time between two
/// readings close together counts, so time the app wasn't running, or the PC was asleep, isn't counted: there's nothing
/// to go on for it. Today's total starts again at midnight and the month's on the 1st, in the PC's time zone.
/// </summary>
/// <param name="zone">Whose midnight starts a new day: the PC's, unless a test chooses another.</param>
public sealed class EnergyMeter(TimeZoneInfo? zone = null)
{
    /// <summary>
    /// The longest time between two readings that counts. They come every second; a longer gap means the PC was asleep
    /// or the app was held up, and the power through it is anyone's guess.
    /// </summary>
    public static readonly TimeSpan LongestGap = TimeSpan.FromMinutes(1);

    private readonly TimeZoneInfo _zone = zone ?? TimeZoneInfo.Local;
    private DateTimeOffset? _last;
    private double? _lastWatts;

    /// <summary>What's been counted, or null before anything has (and nothing was kept from an earlier run).</summary>
    public EnergyTotals? Totals { get; private set; }

    /// <summary>Carries on from totals kept from an earlier run. Ones from an earlier day or month start again at the next reading.</summary>
    public void Restore(EnergyTotals? totals) => Totals = totals;

    /// <summary>
    /// Takes the power now, or null without a reading, and counts the energy since the last reading: the average of the
    /// two for the time between. The totals move on to a new day or month as it comes, with time across midnight split
    /// between the two days.
    /// </summary>
    public void Add(DateTimeOffset now, double? watts)
    {
        var day = DayOf(now);
        if (_last is { } last && _lastWatts is { } before && watts is { } after && now > last && now - last <= LongestGap)
        {
            var average = (Math.Max(0, before) + Math.Max(0, after)) / 2;
            var lastDay = DayOf(last);
            var midnight = lastDay == day ? last : StartOf(day, last, now);
            Count(lastDay, average, midnight - last);
            Count(day, average, now - midnight);
        }
        else
        {
            MoveTo(day);
        }
        _last = now;
        _lastWatts = watts;
    }

    /// <summary>The day it is at <paramref name="time"/> on the PC's clock.</summary>
    public DateOnly DayOf(DateTimeOffset time) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, _zone).DateTime);

    private void Count(DateOnly day, double watts, TimeSpan time)
    {
        MoveTo(day);
        var kilowattHours = watts * time.TotalHours / 1000;
        Totals = Totals is { } totals
            ? totals with { Today = totals.Today + kilowattHours, Month = totals.Month + kilowattHours }
            : new EnergyTotals(day, kilowattHours, kilowattHours);
    }

    /// <summary>A new day starts today's total again, and a new month the month's too.</summary>
    private void MoveTo(DateOnly day)
    {
        if (Totals is not { } totals || totals.Day == day)
            return;
        var sameMonth = totals.Day.Year == day.Year && totals.Day.Month == day.Month;
        Totals = new EnergyTotals(day, 0, sameMonth ? totals.Month : 0);
    }

    /// <summary>When <paramref name="day"/> began, kept between two readings either side of it.</summary>
    private DateTimeOffset StartOf(DateOnly day, DateTimeOffset after, DateTimeOffset before)
    {
        var midnight = day.ToDateTime(TimeOnly.MinValue);
        // Where the clocks go forward at midnight, it never happens: the time between is near enough all the new day's.
        if (_zone.IsInvalidTime(midnight))
            return after;
        var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(midnight, _zone), TimeSpan.Zero);
        return start < after ? after : start > before ? before : start;
    }
}
