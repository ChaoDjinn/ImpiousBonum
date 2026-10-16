using ImpiousBonum.Core.Energy;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// How much carbon Great Britain's electricity is giving off, from NESO's Carbon Intensity API every 30 minutes: the
/// intensity now with its level, and when it'll be lowest in the next 24 hours, for the whole of Great Britain or the
/// region of a postcode's first half. Off unless turned on, and nothing is fetched while it's off. Between fetches the
/// readings move on through the forecast already fetched, half an hour at a time. It's sampled on the
/// <see cref="EnergyProvider"/>'s loop rather than a loop of its own, so while it's off it costs nothing but its metric
/// definitions.
/// </summary>
/// <param name="request">Whose figures, or null when it's off. Read on every sample, so changes apply quickly.</param>
/// <param name="fetch">Gets the forecast; the Carbon Intensity API unless a test swaps it.</param>
public sealed class GridCarbonProvider(
    Func<GridCarbonRequest?> request,
    Func<GridCarbonRequest, CancellationToken, Task<CarbonForecast>>? fetch = null,
    TimeProvider? time = null)
{
    public const string Intensity = "grid.carbon";
    public const string Index = "grid.carbon.index";
    public const string Best = "grid.carbon.best";
    public const string Region = "grid.carbon.region";

    private const string Category = "Grid carbon";

    /// <summary>The forecast is made afresh every half-hour.</summary>
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(30);

    /// <summary>After a failed fetch (offline, say), how long until trying again.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);

    /// <summary>How long readings stay on screen when new ones can't be fetched.</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromHours(3);

    private readonly Func<GridCarbonRequest, CancellationToken, Task<CarbonForecast>> _fetch = fetch ?? CarbonIntensity.ForecastAsync;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private string? _key;
    private CarbonForecast? _forecast;
    private DateTimeOffset _fetched;
    private DateTimeOffset _nextTry;

    /// <summary>Registers the readings, so they can be picked in the editor before it's turned on.</summary>
    public void Initialize(MetricStore store)
    {
        store.Register(new MetricDefinition(Intensity, "Grid carbon intensity now", Category, MetricUnit.GramsOfCo2PerKilowattHour));
        store.Register(new MetricDefinition(Index, "Grid carbon: level now, from Very low to Very high", Category, MetricUnit.Text));
        store.Register(new MetricDefinition(Best, "Grid carbon: when it's lowest in the next 24 hours, e.g. Lowest around 14:00", Category, MetricUnit.Text));
        store.Register(new MetricDefinition(Region, "Grid carbon: where the figures are for, e.g. Great Britain", Category, MetricUnit.Text));
    }

    /// <summary>Fetches the forecast when it's due, and publishes the half-hour it is now. Called on the energy's loop.</summary>
    public async ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        var wanted = request();
        if (wanted is null)
        {
            // Off: nothing to fetch. Readings from when it was on go; after that there's nothing to do.
            if (_key is not null)
            {
                _key = null;
                _forecast = null;
                Publish(store, null, _time.GetUtcNow(), _time.LocalTimeZone);
            }
            return;
        }

        var now = _time.GetUtcNow();
        // Another area (or just turned on): fetch now, and stop showing the old area's figures meanwhile.
        if (wanted.Key != _key)
        {
            _key = wanted.Key;
            _forecast = null;
            _nextTry = now;
        }

        if (now >= _nextTry)
        {
            try
            {
                _forecast = await _fetch(wanted, cancellationToken).ConfigureAwait(false);
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

        if (_forecast is not null && now - _fetched > KeepFor)
            _forecast = null;
        Publish(store, _forecast, now, _time.LocalTimeZone);
    }

    /// <param name="now">Which half-hour it is, and where the lowest ahead is looked for from.</param>
    /// <param name="zone">The PC's time zone, for the time the lowest is at.</param>
    internal static void Publish(MetricStore store, CarbonForecast? forecast, DateTimeOffset now, TimeZoneInfo zone)
    {
        var current = forecast?.At(now);
        var lowest = forecast?.Lowest(now);
        store.Set(Intensity, current?.Intensity);
        store.SetText(Index, CarbonIntensity.Level(current?.Index));
        store.SetText(Best, current is not null && lowest is not null ? CarbonIntensity.Lowest(lowest, now, zone) : null);
        store.SetText(Region, current is null ? null : forecast!.Region);
    }
}
