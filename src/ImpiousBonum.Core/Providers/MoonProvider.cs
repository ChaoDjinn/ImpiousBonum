using System.Globalization;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// The moon's phase, how much of it is lit, how old it is and when the next full and new moons are, worked out on the PC
/// once a minute (see <see cref="Moon"/>), so nothing is fetched and it works without a location. With the weather's
/// location it adds when the moon rises and sets there, and today's sunrise and sunset for the sun and moon widget (a
/// <see cref="SkyReport"/>, as an attachment). Times are in the PC's time.
/// </summary>
/// <param name="place">The weather's location, or null without one. Read on every sample, so a new one applies within a minute.</param>
public sealed class MoonProvider(Func<WeatherRequest?> place, TimeProvider? time = null) : IMetricProvider
{
    public const string Phase = "moon.phase";
    public const string Illumination = "moon.illumination";
    public const string Age = "moon.age";
    public const string NextFull = "moon.next.full";
    public const string NextNew = "moon.next.new";
    public const string Rise = "moon.rise";
    public const string Set = "moon.set";

    /// <summary>The latest <see cref="SkyReport"/>, as a <see cref="MetricStore"/> attachment, for the sun and moon widget.</summary>
    public const string Sky = "moon.sky";

    private const string Category = "Moon";

    /// <summary>How dates show, e.g. Sat 24 Oct.</summary>
    public const string DateFormat = "ddd d MMM";

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>The sun's day last worked out, kept until the day or the place changes.</summary>
    private SunDay? _sun;
    private string? _sunFor;

    /// <summary>The moon's time in the sky last worked out, kept until it rises or sets or the place changes.</summary>
    private (DateTimeOffset? Rise, DateTimeOffset? Set) _moonUp;
    private string? _moonUpFor;
    private DateTimeOffset _moonUpUntil;

    public TimeSpan Interval => TimeSpan.FromMinutes(1);

    public void Initialize(MetricStore store)
    {
        store.Register(new MetricDefinition(Phase, "Moon: phase, e.g. Waxing gibbous", Category, MetricUnit.Text));
        store.Register(new MetricDefinition(Illumination, "Moon: how much of it is lit", Category, MetricUnit.Percent, 100, Format: "0"));
        store.Register(new MetricDefinition(Age, "Moon: days since the new moon", Category, MetricUnit.None, Moon.SynodicMonth, Format: "0.0"));
        store.Register(new MetricDefinition(NextFull, "Moon: next full moon, e.g. Sat 24 Oct", Category, MetricUnit.Text));
        store.Register(new MetricDefinition(NextNew, "Moon: next new moon, e.g. Sun 8 Nov", Category, MetricUnit.Text));
        store.Register(new MetricDefinition(Rise, "Moon: moonrise, or when it rose while it's up", Category, MetricUnit.Text));
        store.Register(new MetricDefinition(Set, "Moon: moonset", Category, MetricUnit.Text));
    }

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        Publish(store, Work(place(), _time.GetUtcNow()));
        return ValueTask.CompletedTask;
    }

    /// <summary>The sun and moon at <paramref name="now"/>, seen from <paramref name="where"/> (or without a place, just the moon).</summary>
    internal SkyReport Work(WeatherRequest? where, DateTimeOffset now)
    {
        var zone = _time.LocalTimeZone;
        var (start, end) = Today(now, zone);
        var moon = new MoonReport(
            Moon.Illumination(now),
            Moon.Elongation(now) < 180,
            Moon.Age(now),
            Moon.PhaseName(now, start, end),
            Moon.Next(MoonPhase.Full, now),
            Moon.Next(MoonPhase.New, now));
        if (where is null)
        {
            _sunFor = _moonUpFor = null;
            return new SkyReport(now, moon, null, null);
        }

        var key = FormattableString.Invariant($"{where.Latitude:0.####},{where.Longitude:0.####}");
        if (_sunFor != key || _sun?.Start != start)
        {
            _sun = SunDay.At(where.Latitude, where.Longitude, start, end);
            _sunFor = key;
        }
        // Worked out again once it rises or sets (or after an hour, when neither is soon), rather than every minute.
        if (_moonUpFor != key || now >= _moonUpUntil)
        {
            _moonUp = Moon.RiseAndSetAround(where.Latitude, where.Longitude, now);
            _moonUpFor = key;
            var next = _moonUp.Rise > now ? _moonUp.Rise : _moonUp.Set;
            _moonUpUntil = next is { } soon && soon < now.AddHours(1) ? soon : now.AddHours(1);
        }
        return new SkyReport(now, moon with { Rise = _moonUp.Rise, Set = _moonUp.Set }, _sun, where.Latitude);
    }

    /// <summary>Writes <paramref name="sky"/>'s readings, and attaches it for the sun and moon widget.</summary>
    public void Publish(MetricStore store, SkyReport sky)
    {
        var zone = _time.LocalTimeZone;
        var moon = sky.Moon;
        store.SetText(Phase, moon.Phase);
        store.Set(Illumination, Math.Round(moon.Illumination * 100, 1));
        store.Set(Age, moon.Age);
        store.SetText(NextFull, Date(moon.NextFull, zone));
        store.SetText(NextNew, Date(moon.NextNew, zone));
        store.SetText(Rise, moon.Rise is { } rise ? TimeOfDay(rise, zone) : null);
        store.SetText(Set, moon.Set is { } set ? TimeOfDay(set, zone) : null);
        store.SetAttachment(Sky, sky);
    }

    /// <summary>"Sat 24 Oct", in the PC's time.</summary>
    public static string Date(DateTimeOffset time, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(time, zone).ToString(DateFormat, CultureInfo.CurrentCulture);

    /// <summary>"21:47", or as Windows' region has times, in the PC's time.</summary>
    public static string TimeOfDay(DateTimeOffset time, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(time, zone).ToString("t", CultureInfo.CurrentCulture);

    /// <summary>Midnight at the start and end of <paramref name="now"/>'s day in <paramref name="zone"/>.</summary>
    internal static (DateTimeOffset Start, DateTimeOffset End) Today(DateTimeOffset now, TimeZoneInfo zone)
    {
        var date = TimeZoneInfo.ConvertTime(now, zone).Date;
        return (Midnight(date, zone), Midnight(date.AddDays(1), zone));

        static DateTimeOffset Midnight(DateTime date, TimeZoneInfo zone) => new(date, zone.GetUtcOffset(date));
    }

    public void Dispose()
    {
    }
}
