using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Sessions;

namespace ImpiousBonum.Core.Tests;

public sealed class SessionTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 10, 11, 18, 0, 0, DateTimeKind.Utc);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ImpiousBonum.Tests", Guid.NewGuid().ToString("N"));

    public SessionTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    /// <summary>Feeds one sample a second from <paramref name="from"/> seconds in; returns any recaps.</summary>
    private static List<SessionRecap> Feed(SessionRecorder recorder, int from, int seconds, string? game, Func<int, double>? fps = null)
    {
        var recaps = new List<SessionRecap>();
        for (var i = from; i < from + seconds; i++)
        {
            var at = i;
            if (recorder.Update(game, id => id switch
                {
                    "fps" => fps?.Invoke(at) ?? 60,
                    "gpu.load" => 90 + at % 10,
                    "cpu.temp" => 70,
                    _ => null,
                }, Start.AddSeconds(i)) is { } recap)
                recaps.Add(recap);
        }
        return recaps;
    }

    [Fact]
    public void A_session_starts_after_a_few_seconds_and_survives_alt_tabs()
    {
        var recorder = new SessionRecorder();
        Feed(recorder, 0, 2, "Elden Ring");
        Assert.Null(recorder.Game);
        Feed(recorder, 2, 600, "Elden Ring");
        Assert.Equal("Elden Ring", recorder.Game);

        // Thirty seconds on Discord doesn't end it, and isn't counted.
        Assert.Empty(Feed(recorder, 602, 30, null, _ => 1));
        Assert.Empty(Feed(recorder, 632, 300, "Elden Ring"));

        var recap = Assert.Single(Feed(recorder, 932, 61, null));
        Assert.Null(recorder.Game);
        Assert.Equal("Elden Ring", recap.Game);
        Assert.Equal(Start, recap.Start);
        Assert.Equal(Start.AddSeconds(931), recap.End);
        var fps = recap.Stat("fps")!;
        Assert.Equal(60, fps.Average);
        Assert.Equal(60, fps.Min);
        var gpu = recap.Stat("gpu")!;
        Assert.Equal(90, gpu.Min);
        Assert.Equal(99, gpu.Max);
        Assert.Null(recap.Stat("cpu"));
    }

    [Fact]
    public void Short_sessions_arent_reported_and_another_game_starts_a_new_one()
    {
        var recorder = new SessionRecorder();
        Feed(recorder, 0, 30, "Launcher");
        Assert.Empty(Feed(recorder, 30, 70, null));

        Feed(recorder, 100, 120, "Game A");
        var recaps = Feed(recorder, 220, 10, "Game B");
        Assert.Equal("Game A", Assert.Single(recaps).Game);
        Assert.Equal("Game B", recorder.Game);
    }

    [Fact]
    public void No_frames_yet_isnt_a_low()
    {
        var recorder = new SessionRecorder();
        Feed(recorder, 0, 120, "Game", i => i < 10 ? 0 : 30 + i % 2 * 30);
        var fps = recorder.Current!.Stat("fps")!;
        Assert.Equal(30, fps.Min);
        Assert.Equal(60, fps.Max);
        Assert.Equal(45, fps.Average, 0);
    }

    [Fact]
    public void The_recap_reads_well()
    {
        var recap = new SessionRecap("Elden Ring", Start, Start.AddMinutes(83), new Dictionary<string, SessionStat>
        {
            ["fps"] = new(31, 58.4, 60, 100),
            ["low1"] = new(20, 42, 50, 100),
            ["gpu"] = new(60, 97, 100, 100),
            ["gputemp"] = new(50, 66, 72.4, 100),
        });
        Assert.Equal(
            "Elden Ring, 1 h 23 min\n58 FPS average (lowest 31, highest 60), 1% low 42\nGPU 97% average, 100% peak\nHottest: GPU 72 °C",
            recap.Describe());
        Assert.Equal("25 min", SessionRecap.DescribeLength(TimeSpan.FromMinutes(25.5)));
        Assert.Equal("under a minute", SessionRecap.DescribeLength(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void Sessions_are_written_to_a_csv_with_a_header_once()
    {
        var path = Path.Combine(_directory, "sessions.csv");
        var recap = new SessionRecap("Game, The \"Sequel\"", Start, Start.AddMinutes(45), new Dictionary<string, SessionStat>
        {
            ["fps"] = new(31, 58.44, 60, 100),
            ["cpu"] = new(10, 40, 75, 100),
        });
        SessionLog.Append(path, recap);
        SessionLog.Append(path, recap);

        var lines = File.ReadAllLines(path);
        Assert.Equal(3, lines.Length);
        Assert.Equal(SessionLog.Header, lines[0]);
        Assert.Contains(",\"Game, The \"\"Sequel\"\"\",45,58.4,31,60,,40,75,,,,", lines[1]);
        Assert.Equal(lines[1], lines[2]);
    }

    [Fact]
    public async Task The_provider_shows_the_session_so_far_then_the_last_one()
    {
        var time = new FakeTime(new DateTimeOffset(Start));
        string? game = "Game";
        var ended = new List<SessionRecap>();
        using var provider = new SessionProvider(() => game, ended.Add, time);
        var store = new MetricStore();
        store.Register(new MetricDefinition("fps", "FPS", "Test", MetricUnit.FramesPerSecond));
        provider.Initialize(store);

        for (var i = 0; i < 200; i++)
        {
            store.Set("fps", 100 + i % 2 * 20);
            await provider.SampleAsync(store, CancellationToken.None);
            time.Advance(TimeSpan.FromSeconds(1));
        }
        Assert.True(store.TryGet(SessionProvider.Playing, out var playing));
        Assert.Equal("Playing", playing.Text);
        Assert.True(store.TryGet(SessionProvider.Average("fps"), out var average));
        Assert.Equal(110, average.Value!.Value, 0);

        game = null;
        for (var i = 0; i < 61; i++)
        {
            await provider.SampleAsync(store, CancellationToken.None);
            time.Advance(TimeSpan.FromSeconds(1));
        }
        Assert.Single(ended);
        Assert.True(store.TryGet(SessionProvider.Playing, out var last));
        Assert.Equal("Last played", last.Text);
        Assert.True(store.TryGet(SessionProvider.Max("fps"), out var max));
        Assert.Equal(120, max.Value);
        Assert.True(store.TryGet(SessionProvider.Minutes, out var minutes));
        Assert.Equal(3, minutes.Value);
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
