using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Sessions;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// Game session recaps: while a tracked game is in front, each tracked metric's lowest, average and highest reading so
/// far (<c>session.fps.avg</c>, <c>session.gpu.max</c>, …); afterwards, the last session's. <paramref name="ended"/>
/// hears about each session as it ends, on the sampler's thread.
/// </summary>
/// <param name="gameInFront">The tracked game in front now, or null; read about once a second from the sampler's thread.</param>
public sealed class SessionProvider(Func<string?> gameInFront, Action<SessionRecap>? ended = null, TimeProvider? time = null) : IMetricProvider
{
    public const string Game = "session.game";
    public const string Minutes = "session.minutes";
    public const string Playing = "session.playing";

    public static string Average(string key) => $"session.{key}.avg";

    public static string Min(string key) => $"session.{key}.min";

    public static string Max(string key) => $"session.{key}.max";

    private readonly SessionRecorder _recorder = new();
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private SessionRecap? _last;

    public TimeSpan Interval => TimeSpan.FromSeconds(1);

    public void Initialize(MetricStore store)
    {
        store.Register(new MetricDefinition(Game, "Game session: game", "Game session", MetricUnit.Text));
        store.Register(new MetricDefinition(Minutes, "Game session: minutes played", "Game session", MetricUnit.None, Format: "0"));
        store.Register(new MetricDefinition(Playing, "Game session: Playing while a session is on, else Last played", "Game session", MetricUnit.Text));
        foreach (var (key, _, name, unit) in SessionRecorder.Tracked)
        {
            var max = unit == MetricUnit.Percent ? 100 : (double?)null;
            store.Register(new MetricDefinition(Average(key), $"Game session: average {name}", "Game session", unit, max, "0"));
            store.Register(new MetricDefinition(Min(key), $"Game session: lowest {name}", "Game session", unit, max, "0"));
            store.Register(new MetricDefinition(Max(key), $"Game session: highest {name}", "Game session", unit, max, "0"));
        }
    }

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        var recap = _recorder.Update(gameInFront(), id => store.TryGet(id, out var sample) ? sample.Value : null, _time.GetUtcNow().UtcDateTime);
        if (recap is not null)
        {
            _last = recap;
            ended?.Invoke(recap);
        }
        Publish(store, _recorder.Current ?? _last, _recorder.Current is not null);
        return ValueTask.CompletedTask;
    }

    private static void Publish(MetricStore store, SessionRecap? recap, bool playing)
    {
        store.SetText(Game, recap?.Game);
        store.Set(Minutes, recap is null ? null : Math.Floor(recap.Length.TotalMinutes));
        store.SetText(Playing, recap is null ? null : playing ? "Playing" : "Last played");
        foreach (var (key, _, _, _) in SessionRecorder.Tracked)
        {
            var stat = recap?.Stat(key);
            store.Set(Average(key), stat?.Average);
            store.Set(Min(key), stat?.Min);
            store.Set(Max(key), stat?.Max);
        }
    }

    public void Dispose()
    {
    }
}
