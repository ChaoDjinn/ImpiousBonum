using System.Globalization;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// The weather where you've said you are, from Open-Meteo, every 15 minutes: conditions now and today's range as metrics,
/// and the whole forecast (a <see cref="WeatherReport"/>) as an attachment for the weather widget. Nothing is fetched
/// until a location is set, and nothing has a reading after that until the first forecast arrives. The sun's height is
/// worked out here on every sample rather than fetched, so it keeps up between forecasts. Other places the user has
/// saved get their forecasts too, for the widget to move between (see <see cref="Places"/>).
/// </summary>
/// <param name="request">Where and in which units, or null when no location is set. Read on every sample, so changes apply quickly.</param>
/// <param name="fetch">Gets a forecast; Open-Meteo unless a test swaps it.</param>
/// <param name="airQuality">
/// Air quality and pollen for the same place, sampled after the weather on this loop: they're off by default, and a
/// loop of their own would wake every 30 seconds whether they're on or not.
/// </param>
/// <param name="warnings">Severe weather warnings for the same place, sampled on this loop too, for the same reason.</param>
/// <param name="places">
/// More places besides the main one, in their order: none unless the user saves some. Their forecasts come on this loop
/// and the same schedule as the main place's, but only as the <see cref="Places"/> attachment: the metrics, air quality
/// and warnings are the main place's alone. They're only fetched along with a main place.
/// </param>
public sealed class WeatherProvider(
    Func<WeatherRequest?> request,
    Func<WeatherRequest, CancellationToken, Task<WeatherReport>>? fetch = null,
    TimeProvider? time = null,
    AirQualityProvider? airQuality = null,
    WeatherWarningsProvider? warnings = null,
    Func<IReadOnlyList<WeatherRequest>>? places = null) : IMetricProvider
{
    public const string Temperature = "weather.temp";
    public const string FeelsLike = "weather.feels";
    public const string High = "weather.high";
    public const string Low = "weather.low";
    public const string Condition = "weather.condition";
    public const string Humidity = "weather.humidity";
    public const string Wind = "weather.wind";
    public const string WindDirection = "weather.wind.direction";
    public const string RainChance = "weather.rain";
    public const string Sunrise = "weather.sunrise";
    public const string Sunset = "weather.sunset";
    public const string TomorrowHigh = "weather.tomorrow.high";
    public const string TomorrowLow = "weather.tomorrow.low";
    public const string TomorrowCondition = "weather.tomorrow.condition";
    public const string Location = "weather.location";
    public const string Gusts = "weather.gusts";
    public const string WindDegrees = "weather.wind.degrees";
    public const string Pressure = "weather.pressure";
    public const string PressureTrend = "weather.pressure.trend";
    public const string DewPoint = "weather.dewpoint";
    public const string Visibility = "weather.visibility";
    public const string Clouds = "weather.clouds";
    public const string Precipitation = "weather.precipitation";
    public const string PrecipitationToday = "weather.precipitation.today";
    public const string RainChanceHour = "weather.rain.hour";
    public const string TomorrowRainChance = "weather.tomorrow.rain";
    public const string Uv = "weather.uv";
    public const string UvMax = "weather.uv.max";
    public const string UvLevel = "weather.uv.level";
    public const string SunElevation = "weather.sun.elevation";
    public const string Daylight = "weather.daylight";

    /// <summary>The latest <see cref="WeatherReport"/>, as a <see cref="MetricStore"/> attachment.</summary>
    public const string Forecast = "weather.forecast";

    /// <summary>
    /// While there are more places than the main one, every place's forecast as a <see cref="MetricStore"/> attachment:
    /// an <c>IReadOnlyList&lt;PlaceForecast&gt;</c> of the main place (the same report as <see cref="Forecast"/>), then
    /// the others in their order. None without more places.
    /// </summary>
    public const string Places = "weather.places";

    public static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(15);

    /// <summary>After a failed fetch (offline, say), how long until trying again.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(2);

    /// <summary>How long a forecast stays on screen when new ones can't be fetched.</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromHours(3);

    private readonly Func<WeatherRequest, CancellationToken, Task<WeatherReport>> _fetch = fetch ?? OpenMeteo.ForecastAsync;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private string? _key;
    private WeatherReport? _report;
    private DateTimeOffset _fetched;
    private DateTimeOffset _nextTry;
    private WeatherUnits? _registeredUnits;

    /// <summary>The more places' forecasts and schedules, by <see cref="WeatherRequest.Key"/>; empty without any.</summary>
    private Dictionary<string, PlaceState> _places = [];

    /// <summary>What <see cref="Places"/> holds, so it's only replaced when something in it changes.</summary>
    private List<PlaceForecast>? _published;

    public TimeSpan Interval => TimeSpan.FromSeconds(30);

    public void Initialize(MetricStore store)
    {
        Register(store, WeatherUnits.Metric);
        airQuality?.Initialize(store);
        warnings?.Initialize(store);
    }

    private void Register(MetricStore store, WeatherUnits units)
    {
        if (_registeredUnits == units)
            return;
        _registeredUnits = units;
        var imperial = units == WeatherUnits.Imperial;
        var temperature = imperial ? MetricUnit.Fahrenheit : MetricUnit.Celsius;
        var speed = imperial ? MetricUnit.MilesPerHour : MetricUnit.KilometresPerHour;
        var pressure = imperial ? MetricUnit.InchesOfMercury : MetricUnit.Hectopascals;
        var distance = imperial ? MetricUnit.Miles : MetricUnit.Kilometres;
        var precipitation = imperial ? MetricUnit.Inches : MetricUnit.Millimetres;

        store.Register(new MetricDefinition(Temperature, "Weather: temperature", "Weather", temperature, Format: "0"));
        store.Register(new MetricDefinition(FeelsLike, "Weather: feels like", "Weather", temperature, Format: "0"));
        store.Register(new MetricDefinition(High, "Weather: today's high", "Weather", temperature, Format: "0"));
        store.Register(new MetricDefinition(Low, "Weather: today's low", "Weather", temperature, Format: "0"));
        store.Register(new MetricDefinition(Condition, "Weather: conditions, e.g. Light rain", "Weather", MetricUnit.Text));
        store.Register(new MetricDefinition(Humidity, "Weather: humidity", "Weather", MetricUnit.Percent, 100, Format: "0"));
        store.Register(new MetricDefinition(Wind, "Weather: wind speed", "Weather", speed));
        store.Register(new MetricDefinition(WindDirection, "Weather: wind direction, e.g. SW", "Weather", MetricUnit.Text));
        store.Register(new MetricDefinition(RainChance, "Weather: today's chance of rain", "Weather", MetricUnit.Percent, 100, Format: "0"));
        store.Register(new MetricDefinition(Sunrise, "Weather: sunrise", "Weather", MetricUnit.Text));
        store.Register(new MetricDefinition(Sunset, "Weather: sunset", "Weather", MetricUnit.Text));
        store.Register(new MetricDefinition(TomorrowHigh, "Weather: tomorrow's high", "Weather", temperature, Format: "0"));
        store.Register(new MetricDefinition(TomorrowLow, "Weather: tomorrow's low", "Weather", temperature, Format: "0"));
        store.Register(new MetricDefinition(TomorrowCondition, "Weather: tomorrow's conditions", "Weather", MetricUnit.Text));
        store.Register(new MetricDefinition(Location, "Weather: location", "Weather", MetricUnit.Text));
        store.Register(new MetricDefinition(Gusts, "Weather: wind gusts", "Weather", speed));
        store.Register(new MetricDefinition(WindDegrees, "Weather: wind direction in degrees, e.g. 225", "Weather", MetricUnit.Degrees, 360));
        store.Register(new MetricDefinition(Pressure, "Weather: air pressure at sea level", "Weather", pressure));
        store.Register(new MetricDefinition(PressureTrend, "Weather: pressure over the last 3 hours, e.g. Falling", "Weather", MetricUnit.Text));
        store.Register(new MetricDefinition(DewPoint, "Weather: dew point", "Weather", temperature, Format: "0"));
        store.Register(new MetricDefinition(Visibility, "Weather: visibility", "Weather", distance));
        store.Register(new MetricDefinition(Clouds, "Weather: cloud cover", "Weather", MetricUnit.Percent, 100, Format: "0"));
        store.Register(new MetricDefinition(Precipitation, "Weather: rain or snow in the last 15 minutes", "Weather", precipitation));
        store.Register(new MetricDefinition(PrecipitationToday, "Weather: today's rain or snow", "Weather", precipitation));
        store.Register(new MetricDefinition(RainChanceHour, "Weather: chance of rain this hour", "Weather", MetricUnit.Percent, 100, Format: "0"));
        store.Register(new MetricDefinition(TomorrowRainChance, "Weather: tomorrow's chance of rain", "Weather", MetricUnit.Percent, 100, Format: "0"));
        store.Register(new MetricDefinition(Uv, "Weather: UV index", "Weather", MetricUnit.None, 11, Format: "0"));
        store.Register(new MetricDefinition(UvMax, "Weather: today's highest UV index", "Weather", MetricUnit.None, 11, Format: "0"));
        store.Register(new MetricDefinition(UvLevel, "Weather: UV level now, e.g. Moderate", "Weather", MetricUnit.Text));
        store.Register(new MetricDefinition(SunElevation, "Weather: how high the sun is (below 0 at night)", "Weather", MetricUnit.Degrees, 90));
        store.Register(new MetricDefinition(Daylight, "Weather: today's daylight, e.g. 10 h 49 min", "Weather", MetricUnit.Text));
    }

    public async ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        var wanted = request();
        var fetched = await SampleWeatherAsync(store, wanted, cancellationToken).ConfigureAwait(false);
        // Without a main place the weather is off, so the others are too.
        var others = wanted is null || places is null ? [] : places();
        await SamplePlacesAsync(store, wanted, others, fetched, cancellationToken).ConfigureAwait(false);
        if (airQuality is not null)
            await airQuality.SampleAsync(store, cancellationToken).ConfigureAwait(false);
        if (warnings is not null)
            await warnings.SampleAsync(store, cancellationToken).ConfigureAwait(false);
    }

    /// <returns>Whether it asked for a forecast, so the other places can wait for the next sample.</returns>
    private async ValueTask<bool> SampleWeatherAsync(MetricStore store, WeatherRequest? wanted, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        if (wanted is null)
        {
            _key = null;
            _report = null;
            Publish(store, null, null, now);
            return false;
        }

        // A new place or units: fetch now, and stop showing the old place's weather meanwhile.
        if (wanted.Key != _key)
        {
            _key = wanted.Key;
            _report = null;
            _nextTry = now;
        }

        var due = now >= _nextTry;
        if (due)
        {
            try
            {
                _report = await _fetch(wanted, cancellationToken).ConfigureAwait(false);
                _fetched = now;
                _nextTry = now + RefreshEvery;
            }
            catch (Exception ex) when (IsFetchFailure(ex))
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
                _nextTry = now + RetryAfter;
            }
        }

        if (_report is not null && now - _fetched > KeepFor)
            _report = null;
        Publish(store, _report, wanted, now);
        return due;
    }

    /// <summary>
    /// Fetches the other places' forecasts when they're due, on the main place's schedule, and publishes them all as
    /// <see cref="Places"/>. At most one is fetched a sample, and none in a sample the main place's was, so a few places
    /// don't all go at once. Without other places there's nothing to do at all.
    /// </summary>
    /// <param name="main">The main place, which comes first in the list.</param>
    /// <param name="wanted">The other places, in their order.</param>
    /// <param name="busy">Whether the main place's forecast was asked for this sample.</param>
    private async ValueTask SamplePlacesAsync(MetricStore store, WeatherRequest? main, IReadOnlyList<WeatherRequest> wanted, bool busy, CancellationToken cancellationToken)
    {
        if (main is null || wanted.Count == 0)
        {
            // None, or none any more: their forecasts go, and after that there's nothing to do.
            if (_published is not null)
            {
                _places = [];
                _published = null;
                store.SetAttachment(Places, null);
            }
            return;
        }

        var now = _time.GetUtcNow();
        // Places still there keep their forecasts and schedules, in their new order, and a new one is due now. A place
        // removed is forgotten, so it's never fetched again.
        var places = new Dictionary<string, PlaceState>();
        foreach (var place in wanted)
            places.TryAdd(place.Key, _places.GetValueOrDefault(place.Key) ?? new PlaceState { NextTry = now });
        _places = places;

        // The one that's waited longest, or the first in the list.
        var next = busy ? null : wanted.Where(p => now >= places[p.Key].NextTry).MinBy(p => places[p.Key].NextTry);
        if (next is not null)
        {
            var state = places[next.Key];
            try
            {
                state.Report = await _fetch(next, cancellationToken).ConfigureAwait(false);
                state.Fetched = now;
                state.NextTry = now + RefreshEvery;
            }
            catch (Exception ex) when (IsFetchFailure(ex))
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
                state.NextTry = now + RetryAfter;
            }
        }

        foreach (var state in places.Values)
        {
            if (state.Report is not null && now - state.Fetched > KeepFor)
                state.Report = null;
        }

        // Replaced only when a place or a forecast changes, so the widget can tell nothing has.
        List<PlaceForecast> forecasts = [new(main.Name, _report), .. wanted.Select(p => new PlaceForecast(p.Name, places[p.Key].Report))];
        if (_published is not null && forecasts.SequenceEqual(_published))
            return;
        _published = forecasts;
        store.SetAttachment(Places, forecasts);
    }

    /// <summary>What a forecast that couldn't be fetched (offline, say, or a garbled answer) throws.</summary>
    private static bool IsFetchFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or FormatException or KeyNotFoundException or InvalidOperationException;

    /// <param name="place">Where the forecast is for: its name, and where the sun is seen from.</param>
    /// <param name="now">For the readings that move on between forecasts: the sun, and which hour it is at the place.</param>
    internal void Publish(MetricStore store, WeatherReport? report, WeatherRequest? place, DateTimeOffset now)
    {
        if (report is not null)
            Register(store, report.Units);

        store.Set(Temperature, report?.Temperature);
        store.Set(FeelsLike, report?.FeelsLike);
        store.Set(High, report?.Today?.Temperature);
        store.Set(Low, report?.Today?.Low);
        store.SetText(Condition, report is null ? null : WeatherCodes.Describe(report.Code));
        store.Set(Humidity, NullIfNaN(report?.Humidity));
        store.Set(Wind, NullIfNaN(report?.WindSpeed));
        store.SetText(WindDirection, report is null ? null : NullIfEmpty(WeatherCodes.Compass(report.WindDirection)));
        store.Set(RainChance, report?.Today?.RainChance);
        store.SetText(Sunrise, report?.Sunrise is { } rise ? rise.ToString("t", CultureInfo.CurrentCulture) : null);
        store.SetText(Sunset, report?.Sunset is { } set ? set.ToString("t", CultureInfo.CurrentCulture) : null);
        store.Set(TomorrowHigh, report?.Tomorrow?.Temperature);
        store.Set(TomorrowLow, report?.Tomorrow?.Low);
        store.SetText(TomorrowCondition, report?.Tomorrow is { } tomorrow ? WeatherCodes.Describe(tomorrow.Code) : null);
        store.SetText(Location, report is null ? null : place?.Name);
        store.Set(Gusts, report?.Gusts);
        store.Set(WindDegrees, NullIfNaN(report?.WindDirection));
        store.Set(Pressure, report?.Pressure is { } hectopascals ? WeatherReadings.Pressure(hectopascals, report.Units) : null);
        store.SetText(PressureTrend, report is null ? null : WeatherReadings.Trend(report.Hours, report.LocalTime(now))?.ToString());
        store.Set(DewPoint, report?.DewPoint);
        store.Set(Visibility, report?.Visibility is { } metres ? WeatherReadings.Distance(metres, report.Units) : null);
        store.Set(Clouds, report?.Clouds);
        store.Set(Precipitation, report?.Precipitation);
        store.Set(PrecipitationToday, report?.Today?.Precipitation);
        store.Set(RainChanceHour, report?.HourEndingAfter(report.LocalTime(now))?.RainChance);
        store.Set(TomorrowRainChance, report?.Tomorrow?.RainChance);
        store.Set(Uv, report?.Uv);
        store.Set(UvMax, report?.Today?.Uv);
        store.SetText(UvLevel, WeatherReadings.UvLevel(report?.Uv));
        store.Set(SunElevation, report is not null && place is not null ? SunPosition.Elevation(place.Latitude, place.Longitude, now) : null);
        store.SetText(Daylight, report?.Today?.Daylight is { } daylight ? WeatherReadings.Daylight(daylight) : null);
        store.SetAttachment(Forecast, report);
    }

    private static double? NullIfNaN(double? value) => value is double v && !double.IsNaN(v) ? v : null;

    private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;

    public void Dispose()
    {
    }

    /// <summary>One of the other places: its last forecast, when that was fetched, and when to try next.</summary>
    private sealed class PlaceState
    {
        public WeatherReport? Report { get; set; }

        public DateTimeOffset Fetched { get; set; }

        public DateTimeOffset NextTry { get; set; }
    }
}
