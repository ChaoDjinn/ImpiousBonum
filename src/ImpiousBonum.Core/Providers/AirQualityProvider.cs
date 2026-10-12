using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// The air quality and pollen at the weather's place, from Open-Meteo (CAMS's forecast), every hour: an index on the
/// chosen scale with its level, particles and gases, and grass, tree and weed pollen with theirs. Off unless turned on,
/// and nothing is fetched while it's off or no location is set. Pollen is only counted in Europe, in season; elsewhere
/// those readings have no value.
/// </summary>
/// <param name="request">Where and on which index, or null when it's off or there's no location. Read on every sample, so changes apply quickly.</param>
/// <param name="fetch">Gets the readings; Open-Meteo unless a test swaps it.</param>
public sealed class AirQualityProvider(
    Func<AirQualityRequest?> request,
    Func<AirQualityRequest, CancellationToken, Task<AirQualityReport>>? fetch = null,
    TimeProvider? time = null) : IMetricProvider
{
    public const string Index = "weather.aqi";
    public const string Level = "weather.aqi.level";
    public const string European = "weather.aqi.european";
    public const string Us = "weather.aqi.us";
    public const string Pm25 = "weather.pm25";
    public const string Pm10 = "weather.pm10";
    public const string Ozone = "weather.ozone";
    public const string NitrogenDioxide = "weather.no2";
    public const string Pollen = "weather.pollen";
    public const string GrassPollen = "weather.pollen.grass";
    public const string GrassLevel = "weather.pollen.grass.level";
    public const string TreePollen = "weather.pollen.tree";
    public const string TreeLevel = "weather.pollen.tree.level";
    public const string WeedPollen = "weather.pollen.weed";
    public const string WeedLevel = "weather.pollen.weed.level";

    private const string Category = "Air quality";

    /// <summary>The readings are for the hour, so there's nothing newer to fetch any sooner.</summary>
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromHours(1);

    /// <summary>After a failed fetch (offline, say), how long until trying again.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);

    /// <summary>How long readings stay on screen when new ones can't be fetched.</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromHours(3);

    private static readonly (PollenGroup Group, string Count, string Level)[] Groups =
    [
        (PollenGroup.Grass, GrassPollen, GrassLevel),
        (PollenGroup.Tree, TreePollen, TreeLevel),
        (PollenGroup.Weed, WeedPollen, WeedLevel),
    ];

    private readonly Func<AirQualityRequest, CancellationToken, Task<AirQualityReport>> _fetch = fetch ?? OpenMeteo.AirQualityAsync;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private string? _key;
    private AirQualityReport? _report;
    private DateTimeOffset _fetched;
    private DateTimeOffset _nextTry;
    private AqiScale? _registeredScale;

    public TimeSpan Interval => TimeSpan.FromSeconds(30);

    public void Initialize(MetricStore store)
    {
        RegisterIndex(store, AqiScale.European);
        store.Register(new MetricDefinition(Level, "Air quality: level, e.g. Good", Category, MetricUnit.Text));
        store.Register(new MetricDefinition(European, "Air quality: European index, 0 to 100 and over", Category, MetricUnit.None, 100, Format: "0"));
        store.Register(new MetricDefinition(Us, "Air quality: US index, 0 to 500", Category, MetricUnit.None, 500, Format: "0"));
        store.Register(new MetricDefinition(Pm25, "Air quality: fine particles (PM2.5)", Category, MetricUnit.MicrogramsPerCubicMetre));
        store.Register(new MetricDefinition(Pm10, "Air quality: particles (PM10)", Category, MetricUnit.MicrogramsPerCubicMetre));
        store.Register(new MetricDefinition(Ozone, "Air quality: ozone", Category, MetricUnit.MicrogramsPerCubicMetre));
        store.Register(new MetricDefinition(NitrogenDioxide, "Air quality: nitrogen dioxide", Category, MetricUnit.MicrogramsPerCubicMetre));
        store.Register(new MetricDefinition(Pollen, "Pollen: the highest level now, e.g. Grass high", Category, MetricUnit.Text));
        store.Register(new MetricDefinition(GrassPollen, "Pollen: grass", Category, MetricUnit.GrainsPerCubicMetre));
        store.Register(new MetricDefinition(GrassLevel, "Pollen: grass level, e.g. Low", Category, MetricUnit.Text));
        store.Register(new MetricDefinition(TreePollen, "Pollen: trees (alder, birch and olive)", Category, MetricUnit.GrainsPerCubicMetre));
        store.Register(new MetricDefinition(TreeLevel, "Pollen: tree level, e.g. Moderate", Category, MetricUnit.Text));
        store.Register(new MetricDefinition(WeedPollen, "Pollen: weeds (mugwort and ragweed)", Category, MetricUnit.GrainsPerCubicMetre));
        store.Register(new MetricDefinition(WeedLevel, "Pollen: weed level, e.g. High", Category, MetricUnit.Text));
    }

    /// <summary>The index goes up to 100 on the European scale and 500 on the US one, for gauges and bars.</summary>
    private void RegisterIndex(MetricStore store, AqiScale scale)
    {
        if (_registeredScale == scale)
            return;
        _registeredScale = scale;
        store.Register(new MetricDefinition(Index, "Air quality: index on the chosen scale", Category, MetricUnit.None, scale == AqiScale.Us ? 500 : 100, Format: "0"));
    }

    public async ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        var wanted = request();
        if (wanted is null)
        {
            // Off, or no location: nothing to fetch. Readings from when it was on go; after that there's nothing to do.
            if (_key is not null)
            {
                _key = null;
                _report = null;
                Publish(store, null, null);
            }
            return;
        }

        var now = _time.GetUtcNow();
        // A new place (or just turned on): fetch now, and stop showing the old place's readings meanwhile.
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
        Publish(store, _report, wanted);
    }

    /// <param name="place">Where the readings are for, which decides the automatic scale.</param>
    internal void Publish(MetricStore store, AirQualityReport? report, AirQualityRequest? place)
    {
        var scale = place is null ? AqiScale.European : AirQualityReadings.ScaleFor(place.Scale, place.Latitude, place.Longitude);
        if (report is not null)
            RegisterIndex(store, scale);

        var index = scale == AqiScale.Us ? report?.UsAqi : report?.EuropeanAqi;
        store.Set(Index, index);
        store.SetText(Level, AirQualityReadings.AqiLevel(scale, index));
        store.Set(European, report?.EuropeanAqi);
        store.Set(Us, report?.UsAqi);
        store.Set(Pm25, report?.Pm25);
        store.Set(Pm10, report?.Pm10);
        store.Set(Ozone, report?.Ozone);
        store.Set(NitrogenDioxide, report?.NitrogenDioxide);
        foreach (var (group, count, level) in Groups)
        {
            store.Set(count, report is null ? null : AirQualityReadings.Count(report, group));
            store.SetText(level, report is not null && AirQualityReadings.Level(report, group) is { } found ? AirQualityReadings.Describe(found) : null);
        }
        store.SetText(Pollen, report is null ? null : AirQualityReadings.PollenSummary(report));
    }

    public void Dispose()
    {
    }
}
