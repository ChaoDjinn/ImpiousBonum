using System.Diagnostics;
using ImpiousBonum.Core.CustomData;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Sessions;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.Core;

/// <summary>Runs each provider on its own background loop, writing into a shared <see cref="MetricStore"/>.</summary>
public sealed class Sampler : IAsyncDisposable
{
    private readonly MetricStore _store;
    private readonly IReadOnlyList<IMetricProvider> _providers;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _loops = [];

    public Sampler(MetricStore store, IEnumerable<IMetricProvider> providers)
    {
        _store = store;
        _providers = providers.ToList();
    }

    public MetricStore Store => _store;

    /// <param name="claudeUsagePath">Where Claude Code's status line writes plan usage; null leaves out the Claude metrics.</param>
    /// <param name="weather">Where to get the weather for; null, or returning null, fetches none.</param>
    /// <param name="customData">Your own sources of metrics; null for none.</param>
    /// <param name="calendars">Calendar links to show events from; null for none.</param>
    /// <param name="news">News feeds to show headlines from; null for none.</param>
    /// <param name="gameInFront">The tracked game in front, for session recaps; null tracks none.</param>
    /// <param name="sessionEnded">Hears each game session's recap as it ends, on the sampler's thread.</param>
    /// <param name="airQuality">
    /// Where to get the air quality and pollen for; null, or returning null (off, or no location), fetches none. They're
    /// sampled on the weather's loop, so they add no loop of their own.
    /// </param>
    /// <param name="warnings">
    /// Where to get severe weather warnings from; null, or returning null (off, no location, or a place outside the UK and
    /// the US), fetches none. They're sampled on the weather's loop too.
    /// </param>
    /// <param name="weatherPlaces">
    /// Other places to get forecasts for, besides <paramref name="weather"/>'s, for the weather widget to move between;
    /// null, or none, fetches no more. They're fetched on the weather's loop, and only along with its place.
    /// </param>
    public static IReadOnlyList<IMetricProvider> CreateDefaultProviders(
        string pingHost = "1.1.1.1", Func<(int ProcessId, string? Name)>? frameRateTarget = null, string? claudeUsagePath = null,
        Func<WeatherRequest?>? weather = null, Func<IReadOnlyList<CustomSource>>? customData = null,
        Func<IReadOnlyList<CalendarLink>>? calendars = null, Func<IReadOnlyList<NewsFeed>>? news = null, Func<string?>? gameInFront = null, Action<SessionRecap>? sessionEnded = null,
        Func<AirQualityRequest?>? airQuality = null, Func<WarningsRequest?>? warnings = null, Func<IReadOnlyList<WeatherRequest>>? weatherPlaces = null) =>
    [
        new CpuProvider(),
        new MemoryProvider(),
        new GpuProvider(),
        new DriveProvider(),
        new DiskActivityProvider(),
        new NetworkProvider(),
        new WifiProvider(),
        new PingProvider(pingHost),
        new SensorHostProvider(frameRateTarget),
        new SystemProvider(),
        new ProcessProvider(),
        new MediaProvider(),
        new WeatherProvider(weather ?? (() => null), airQuality: new AirQualityProvider(airQuality ?? (() => null)),
            warnings: new WeatherWarningsProvider(warnings ?? (() => null)), places: weatherPlaces),
        new CustomDataProvider(customData ?? (() => [])),
        new CalendarProvider(calendars ?? (() => [])),
        new NewsProvider(news ?? (() => [])),
        new SessionProvider(gameInFront ?? (() => null), sessionEnded),
        .. claudeUsagePath is null ? Array.Empty<IMetricProvider>() : [new ClaudeUsageProvider(claudeUsagePath)],
    ];

    public void Start()
    {
        foreach (var provider in _providers)
        {
            try
            {
                provider.Initialize(_store);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"{provider.GetType().Name} disabled: {ex.Message}");
                continue;
            }

            if (provider.Interval > TimeSpan.Zero)
                _loops.Add(Task.Run(() => RunAsync(provider, _stop.Token)));
        }
    }

    /// <summary>Samples every provider once, synchronously. Used for snapshots so the first frame has data.</summary>
    public async Task SampleOnceAsync()
    {
        foreach (var provider in _providers)
        {
            try
            {
                await provider.SampleAsync(_store, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"{provider.GetType().Name} sample failed: {ex.Message}");
            }
        }
    }

    private async Task RunAsync(IMetricProvider provider, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(provider.Interval);
        do
        {
            try
            {
                await provider.SampleAsync(_store, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"{provider.GetType().Name} sample failed: {ex.Message}");
            }
        }
        while (await WaitAsync(timer, cancellationToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        // ConfigureAwait(false) so a UI thread blocking on shutdown can't deadlock against these continuations.
        await _stop.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(_loops).ConfigureAwait(false);
        foreach (var provider in _providers)
            provider.Dispose();
        _stop.Dispose();
    }
}
