using System.Globalization;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Sessions;

/// <summary>One metric over a session: its lowest, average and highest reading.</summary>
public sealed record SessionStat(double Min, double Average, double Max, int Samples);

/// <summary>A game session: which game, when, and how the PC did, by tracked metric (see <see cref="SessionRecorder.Tracked"/>).</summary>
public sealed record SessionRecap(string Game, DateTime Start, DateTime End, IReadOnlyDictionary<string, SessionStat> Stats)
{
    public TimeSpan Length => End - Start;

    public SessionStat? Stat(string key) => Stats.GetValueOrDefault(key);

    /// <summary>"1 h 23 min", "25 min", "under a minute".</summary>
    public static string DescribeLength(TimeSpan length) =>
        length.TotalHours >= 1 ? $"{(int)length.TotalHours} h {length.Minutes} min"
        : length.TotalMinutes >= 1 ? $"{(int)length.TotalMinutes} min"
        : "under a minute";

    /// <summary>The recap in a few lines, for a notification.</summary>
    public string Describe()
    {
        var culture = CultureInfo.CurrentCulture;
        var lines = new List<string> { $"{Game}, {DescribeLength(Length)}" };
        if (Stat("fps") is { } fps)
        {
            var low = Stat("low1") is { } low1 ? string.Create(culture, $", 1% low {low1.Average:0}") : string.Empty;
            lines.Add(string.Create(culture, $"{fps.Average:0} FPS average (lowest {fps.Min:0}, highest {fps.Max:0}){low}"));
        }
        var loads = new List<string>();
        if (Stat("cpu") is { } cpu)
            loads.Add(string.Create(culture, $"CPU {cpu.Average:0}% average, {cpu.Max:0}% peak"));
        if (Stat("gpu") is { } gpu)
            loads.Add(string.Create(culture, $"GPU {gpu.Average:0}% average, {gpu.Max:0}% peak"));
        if (loads.Count > 0)
            lines.Add(string.Join(" · ", loads));
        var temps = new List<string>();
        if (Stat("cputemp") is { } cpuTemp)
            temps.Add(string.Create(culture, $"CPU {cpuTemp.Max:0} °C"));
        if (Stat("gputemp") is { } gpuTemp)
            temps.Add(string.Create(culture, $"GPU {gpuTemp.Max:0} °C"));
        if (temps.Count > 0)
            lines.Add("Hottest: " + string.Join(", ", temps));
        return string.Join("\n", lines);
    }
}

/// <summary>
/// Notices a game session from the game in front (sampled about once a second) and keeps each tracked metric's lowest,
/// average and highest reading while it lasts. A session starts once the game has been in front for
/// <see cref="StartAfter"/>, keeps going through alt-tabs shorter than <see cref="EndAfter"/>, and only counts
/// readings while the game is actually in front. Sessions shorter than <see cref="Shortest"/> (a launcher, a quick
/// look) aren't reported.
/// </summary>
public sealed class SessionRecorder
{
    public static readonly TimeSpan StartAfter = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan EndAfter = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan Shortest = TimeSpan.FromMinutes(1);

    /// <summary>What's tracked: a short key (used in <c>session.&lt;key&gt;.avg</c> and so on) for each metric.</summary>
    public static readonly IReadOnlyList<(string Key, string Metric, string Name, MetricUnit Unit)> Tracked =
    [
        ("fps", "fps", "FPS", MetricUnit.FramesPerSecond),
        ("low1", "fps.low1", "1% low FPS", MetricUnit.FramesPerSecond),
        ("cpu", "cpu.load", "CPU load", MetricUnit.Percent),
        ("gpu", "gpu.load", "GPU load", MetricUnit.Percent),
        ("cputemp", "cpu.temp", "CPU temperature", MetricUnit.Celsius),
        ("gputemp", "gpu.temp", "GPU temperature", MetricUnit.Celsius),
    ];

    private readonly Dictionary<string, Accumulator> _stats = new();
    private string? _candidate;
    private DateTime _candidateSince;
    private DateTime _start;
    private DateTime _lastSeen;

    /// <summary>The game being played, or null between sessions.</summary>
    public string? Game { get; private set; }

    private sealed class Accumulator
    {
        public double Min = double.MaxValue;
        public double Max = double.MinValue;
        public double Sum;
        public int Count;

        public void Add(double value)
        {
            Min = Math.Min(Min, value);
            Max = Math.Max(Max, value);
            Sum += value;
            Count++;
        }
    }

    /// <summary>
    /// Feeds one sample: the tracked game in front (null for anything else) and the readings now. Returns the recap of a
    /// session that just ended (and was long enough to report), else null.
    /// </summary>
    /// <param name="read">A metric's reading, or null when it has none.</param>
    public SessionRecap? Update(string? gameInFront, Func<string, double?> read, DateTime now)
    {
        SessionRecap? ended = null;

        if (Game is not null)
        {
            if (Same(gameInFront, Game))
            {
                _lastSeen = now;
                Record(read);
                return null;
            }
            if (gameInFront is null && now - _lastSeen < EndAfter)
                return null;
            // Gone long enough, or another tracked game came to the front.
            ended = Finish();
        }

        if (gameInFront is null)
        {
            _candidate = null;
            return ended;
        }
        if (!Same(gameInFront, _candidate))
        {
            _candidate = gameInFront;
            _candidateSince = now;
        }
        if (now - _candidateSince >= StartAfter)
        {
            Game = gameInFront;
            _start = _candidateSince;
            _lastSeen = now;
            _candidate = null;
            _stats.Clear();
            Record(read);
        }
        return ended;
    }

    /// <summary>The session so far, or null between sessions.</summary>
    public SessionRecap? Current => Game is null ? null : Snapshot(Game);

    /// <summary>Ends the session now (the app is closing, or tracking was turned off); its recap if it was long enough.</summary>
    public SessionRecap? Finish()
    {
        if (Game is not { } game)
            return null;
        var recap = Snapshot(game);
        Game = null;
        _stats.Clear();
        return recap.Length >= Shortest ? recap : null;
    }

    private SessionRecap Snapshot(string game) => new(
        game, _start, _lastSeen,
        _stats.Where(s => s.Value.Count > 0).ToDictionary(s => s.Key, s => new SessionStat(s.Value.Min, s.Value.Sum / s.Value.Count, s.Value.Max, s.Value.Count)));

    private void Record(Func<string, double?> read)
    {
        foreach (var (key, metric, _, _) in Tracked)
        {
            if (read(metric) is not { } value || !double.IsFinite(value))
                continue;
            // No frames counted yet reads as 0 FPS: not a real low.
            if (key is "fps" or "low1" && value <= 0)
                continue;
            if (!_stats.TryGetValue(key, out var stat))
                _stats[key] = stat = new Accumulator();
            stat.Add(value);
        }
    }

    private static bool Same(string? a, string? b) => a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
