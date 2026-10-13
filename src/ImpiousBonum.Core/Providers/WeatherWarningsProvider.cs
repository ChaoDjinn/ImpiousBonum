using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// Severe weather warnings for the weather's place, every 10 minutes: the Met Office's for a place in the UK, the National
/// Weather Service's for one in the US. How many there are and the most serious as metrics, and all of them (an
/// <c>IReadOnlyList&lt;WeatherWarning&gt;</c>, most serious first) as an attachment for the weather widget and
/// notifications. Off unless turned on, and nothing is fetched while it's off or the place isn't in the UK or the US.
/// It's sampled on the <see cref="WeatherProvider"/>'s loop rather than a loop of its own, so while it's off it costs
/// nothing but its metric definitions.
/// </summary>
/// <param name="request">Which agency and where, or null when it's off or the place has no warnings. Read on every sample, so changes apply quickly.</param>
/// <param name="fetch">Gets the warnings; the agency's own feed unless a test swaps it.</param>
public sealed class WeatherWarningsProvider(
    Func<WarningsRequest?> request,
    Func<WarningsRequest, CancellationToken, Task<IReadOnlyList<WeatherWarning>>>? fetch = null,
    TimeProvider? time = null)
{
    public const string Count = "weather.warnings";
    public const string Headline = "weather.warning";
    public const string Level = "weather.warning.level";

    /// <summary>
    /// The warnings in force, most serious first, as a <see cref="MetricStore"/> attachment, and none when there aren't
    /// any. Attachments have ids of their own, so it shares the count's.
    /// </summary>
    public const string Warnings = "weather.warnings";

    private const string Category = "Weather warnings";

    public static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(10);

    /// <summary>After a failed fetch (offline, say), how long until trying again.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(3);

    /// <summary>How long the last warnings stay when new ones can't be fetched (each still goes when it ends).</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromHours(1);

    private readonly Func<WarningsRequest, CancellationToken, Task<IReadOnlyList<WeatherWarning>>> _fetch = fetch ?? WeatherWarnings.FetchAsync;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private string? _key;
    private IReadOnlyList<WeatherWarning>? _warnings;
    private DateTimeOffset _fetched;
    private DateTimeOffset _nextTry;

    /// <summary>Registers the readings, so they can be picked in the editor before it's turned on.</summary>
    public void Initialize(MetricStore store)
    {
        store.Register(new MetricDefinition(Count, "Weather warnings: how many there are now", Category, MetricUnit.None, Format: "0"));
        store.Register(new MetricDefinition(Headline, "Weather warnings: the most serious, e.g. Yellow warning of rain affecting Wales", Category, MetricUnit.Text));
        store.Register(new MetricDefinition(Level, "Weather warnings: the most serious one's level, e.g. Yellow or Severe", Category, MetricUnit.Text));
    }

    /// <summary>Fetches the warnings when they're due, and publishes those still in force. Called on every one of the weather's samples.</summary>
    public async ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        var wanted = request();
        if (wanted is null)
        {
            // Off, or nowhere with warnings: nothing to fetch. Warnings from before go; after that there's nothing to do.
            if (_key is not null)
            {
                _key = null;
                _warnings = null;
                Publish(store, null, _time.GetUtcNow());
            }
            return;
        }

        var now = _time.GetUtcNow();
        // Another place or area (or just turned on): fetch now, and stop showing the old one's warnings meanwhile.
        if (wanted.Key != _key)
        {
            _key = wanted.Key;
            _warnings = null;
            _nextTry = now;
        }

        if (now >= _nextTry)
        {
            try
            {
                _warnings = await _fetch(wanted, cancellationToken).ConfigureAwait(false);
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

        if (_warnings is not null && now - _fetched > KeepFor)
            _warnings = null;
        Publish(store, _warnings, now);
    }

    /// <param name="warnings">The last ones fetched, or null when there are none to go on (off, or nothing fetched yet).</param>
    /// <param name="now">Warnings that have ended by then are left out.</param>
    internal static void Publish(MetricStore store, IReadOnlyList<WeatherWarning>? warnings, DateTimeOffset now)
    {
        var current = warnings is null ? null : WeatherWarnings.Current(warnings, now);
        var worst = current?.FirstOrDefault();
        store.Set(Count, current?.Count);
        store.SetText(Headline, worst?.Headline);
        store.SetText(Level, worst?.LevelText);
        store.SetAttachment(Warnings, current is { Count: > 0 } ? current : null);
    }
}
