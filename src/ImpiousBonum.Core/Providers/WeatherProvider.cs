using System.Globalization;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// The weather where you've said you are, from Open-Meteo, every 15 minutes: conditions now and today's range as metrics,
/// and the whole forecast (a <see cref="WeatherReport"/>) as an attachment for the weather widget. Nothing is fetched
/// until a location is set, and nothing has a reading after that until the first forecast arrives.
/// </summary>
/// <param name="request">Where and in which units, or null when no location is set. Read on every sample, so changes apply quickly.</param>
/// <param name="fetch">Gets a forecast; Open-Meteo unless a test swaps it.</param>
public sealed class WeatherProvider(
    Func<WeatherRequest?> request,
    Func<WeatherRequest, CancellationToken, Task<WeatherReport>>? fetch = null,
    TimeProvider? time = null) : IMetricProvider
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

    /// <summary>The latest <see cref="WeatherReport"/>, as a <see cref="MetricStore"/> attachment.</summary>
    public const string Forecast = "weather.forecast";

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

    public TimeSpan Interval => TimeSpan.FromSeconds(30);

    public void Initialize(MetricStore store) => Register(store, WeatherUnits.Metric);

    private void Register(MetricStore store, WeatherUnits units)
    {
        if (_registeredUnits == units)
            return;
        _registeredUnits = units;
        var (temperature, speed) = units == WeatherUnits.Imperial
            ? (MetricUnit.Fahrenheit, MetricUnit.MilesPerHour)
            : (MetricUnit.Celsius, MetricUnit.KilometresPerHour);

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
    }

    public async ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var wanted = request();
        if (wanted is null)
        {
            _key = null;
            _report = null;
            Publish(store, null, null);
            return;
        }

        // A new place or units: fetch now, and stop showing the old place's weather meanwhile.
        if (wanted.Key != _key)
        {
            _key = wanted.Key;
            _report = null;
            _nextTry = now;
        }

        if (now >= _nextTry)
        {
            try
            {
                _report = await _fetch(wanted, cancellationToken).ConfigureAwait(false);
                _fetched = now;
                _nextTry = now + RefreshEvery;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;
                _nextTry = now + RetryAfter;
            }
        }

        if (_report is not null && now - _fetched > KeepFor)
            _report = null;
        Publish(store, _report, wanted.Name);
    }

    internal void Publish(MetricStore store, WeatherReport? report, string? location)
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
        store.SetText(Location, report is null ? null : location);
        store.SetAttachment(Forecast, report);
    }

    private static double? NullIfNaN(double? value) => value is double v && !double.IsNaN(v) ? v : null;

    private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;

    public void Dispose()
    {
    }
}
