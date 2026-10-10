using System.Text.Json.Nodes;
using ImpiousBonum.Core.Claude;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.Core.Tests;

public sealed class ClaudeUsageTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ImpiousBonum.Tests", Guid.NewGuid().ToString("N"));

    private string UsagePath => Path.Combine(_directory, ClaudeUsageFile.FileName);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    /// <summary>Claude Code's status line input, with rate limits when <paramref name="fiveHour"/> or <paramref name="sevenDay"/> is given.</summary>
    private static string Input((double Used, TimeSpan ResetsIn)? fiveHour = null, (double Used, TimeSpan ResetsIn)? sevenDay = null)
    {
        var root = new JsonObject
        {
            ["model"] = new JsonObject { ["display_name"] = "Opus 5.5" },
            ["context_window"] = new JsonObject { ["used_percentage"] = 8 },
        };
        if (fiveHour is not null || sevenDay is not null)
        {
            var limits = new JsonObject();
            if (fiveHour is { } f)
                limits["five_hour"] = new JsonObject { ["used_percentage"] = f.Used, ["resets_at"] = (Now + f.ResetsIn).ToUnixTimeSeconds() };
            if (sevenDay is { } s)
                limits["seven_day"] = new JsonObject { ["used_percentage"] = s.Used, ["resets_at"] = (Now + s.ResetsIn).ToUnixTimeSeconds() };
            root["rate_limits"] = limits;
        }
        return root.ToJsonString();
    }

    [Fact]
    public void Status_line_records_limits_and_prints_them()
    {
        var line = ClaudeStatusLine.Handle(Input((23.5, TimeSpan.FromMinutes(134)), (41.2, TimeSpan.FromDays(3))), UsagePath, Now);

        Assert.Equal("Opus 5.5 · context 8% · session 24% (2h 14m) · week 41%", line);
        var snapshot = ClaudeUsageFile.Read(UsagePath)!;
        Assert.Equal(Now.ToUnixTimeSeconds(), snapshot.Updated);
        Assert.Equal(23.5, snapshot.FiveHour!.UsedPercentage);
        Assert.Equal(41.2, snapshot.SevenDay!.UsedPercentage);
    }

    [Fact]
    public void Status_line_keeps_the_last_limits_until_a_new_session_reports_them()
    {
        ClaudeStatusLine.Handle(Input((10, TimeSpan.FromHours(1)), (20, TimeSpan.FromDays(2))), UsagePath, Now);
        // A new session sends no rate_limits until its first reply.
        ClaudeStatusLine.Handle(Input(), UsagePath, Now.AddMinutes(5));

        var snapshot = ClaudeUsageFile.Read(UsagePath)!;
        Assert.Equal(Now.AddMinutes(5).ToUnixTimeSeconds(), snapshot.Updated);
        Assert.Equal(10, snapshot.FiveHour!.UsedPercentage);
        Assert.Equal(20, snapshot.SevenDay!.UsedPercentage);
    }

    [Fact]
    public void Status_line_survives_bad_input_and_still_marks_Claude_Code_as_open()
    {
        var line = ClaudeStatusLine.Handle("not json", UsagePath, Now);

        Assert.Equal("Claude Code", line);
        Assert.Equal(Now.ToUnixTimeSeconds(), ClaudeUsageFile.Read(UsagePath)!.Updated);
    }

    [Fact]
    public void Windows_past_their_reset_have_nothing_used()
    {
        var snapshot = new ClaudeUsageSnapshot(Now.ToUnixTimeSeconds(),
            new ClaudeUsageWindow(80, Now.AddMinutes(-1).ToUnixTimeSeconds()),
            new ClaudeUsageWindow(50, Now.AddDays(1).ToUnixTimeSeconds()));

        Assert.Equal(new ClaudeUsageReading(0, null), snapshot.Reading(snapshot.FiveHour, Now));
        Assert.Equal("not started", snapshot.Reading(snapshot.FiveHour, Now).ResetsText);
        Assert.Equal("resets in 1d 0h", snapshot.Reading(snapshot.SevenDay, Now).ResetsText);
        Assert.Equal(new ClaudeUsageReading(null, null), new ClaudeUsageSnapshot(0, null, null).Reading(null, Now));
    }

    [Theory]
    [InlineData(30, "<1m")]
    [InlineData(59 * 60, "59m")]
    [InlineData(2 * 3600 + 14 * 60 + 59, "2h 14m")]
    [InlineData(3 * 86400 + 5 * 3600, "3d 5h")]
    public void Durations_read_naturally(int seconds, string expected) =>
        Assert.Equal(expected, ClaudeUsageReading.FormatDuration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Provider_has_no_readings_until_Claude_Code_reports()
    {
        var (store, provider, clock) = Provider();

        Sample(store, provider);
        Assert.Equal("Not connected to Claude Code", Text(store, ClaudeUsageProvider.Status));
        Assert.False(Has(store, ClaudeUsageProvider.Session));

        ClaudeStatusLine.Handle(Input(), UsagePath, clock.GetUtcNow());
        Sample(store, provider);
        Assert.Equal("Waiting for Claude Code's first reply", Text(store, ClaudeUsageProvider.Status));
        Assert.False(Has(store, ClaudeUsageProvider.Session));
    }

    [Fact]
    public void Provider_reports_usage_while_Claude_Code_is_open_and_stops_when_it_closes()
    {
        var (store, provider, clock) = Provider();
        ClaudeStatusLine.Handle(Input((23.5, TimeSpan.FromMinutes(134)), (41.2, TimeSpan.FromDays(3))), UsagePath, clock.GetUtcNow());

        Sample(store, provider);
        Assert.Equal("Claude Code connected", Text(store, ClaudeUsageProvider.Status));
        Assert.Equal(23.5, Value(store, ClaudeUsageProvider.Session));
        Assert.Equal("resets in 2h 14m", Text(store, ClaudeUsageProvider.SessionResets));
        Assert.Equal(41.2, Value(store, ClaudeUsageProvider.Week));
        Assert.Equal("resets in 3d 0h", Text(store, ClaudeUsageProvider.WeekResets));

        // No status line run for longer than StaleAfter: Claude Code has closed.
        clock.Advance(ClaudeUsageProvider.StaleAfter + TimeSpan.FromSeconds(1));
        Sample(store, provider);
        Assert.Equal("Claude Code isn't open", Text(store, ClaudeUsageProvider.Status));
        Assert.False(Has(store, ClaudeUsageProvider.Session));
        Assert.False(Has(store, ClaudeUsageProvider.WeekResets));
    }

    private (MetricStore Store, ClaudeUsageProvider Provider, TestClock Clock) Provider()
    {
        var store = new MetricStore();
        var clock = new TestClock(Now);
        var provider = new ClaudeUsageProvider(UsagePath, clock);
        provider.Initialize(store);
        return (store, provider, clock);
    }

    private static void Sample(MetricStore store, ClaudeUsageProvider provider) =>
        provider.SampleAsync(store, CancellationToken.None).AsTask().GetAwaiter().GetResult();

    private static bool Has(MetricStore store, string id) => store.TryGet(id, out var sample) && sample.HasValue;

    private static double? Value(MetricStore store, string id) => store.TryGet(id, out var sample) ? sample.Value : null;

    private static string? Text(MetricStore store, string id) => store.TryGet(id, out var sample) ? sample.Text : null;

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
