using ImpiousBonum.Core.Calendar;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.Core.Tests;

public sealed class CalendarProviderTests
{
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    private const string Ics =
        "BEGIN:VCALENDAR\r\n"
        + "BEGIN:VEVENT\r\nUID:1\r\nSUMMARY:Stand-up\r\nDTSTART;TZID=Europe/London:20261012T093000\r\nDTEND;TZID=Europe/London:20261012T100000\r\nEND:VEVENT\r\n"
        + "BEGIN:VEVENT\r\nUID:2\r\nSUMMARY:Lunch\r\nLOCATION:Café\r\nDTSTART;TZID=Europe/London:20261012T123000\r\nDTEND;TZID=Europe/London:20261012T133000\r\nEND:VEVENT\r\n"
        + "BEGIN:VEVENT\r\nUID:3\r\nSUMMARY:Bins\r\nDTSTART;VALUE=DATE:20261012\r\nEND:VEVENT\r\n"
        + "END:VCALENDAR\r\n";

    [Fact]
    public async Task The_next_event_and_whats_on_now_come_from_the_calendar()
    {
        // 09:45 London time on Monday the 12th: the stand-up is on, lunch is next.
        var time = new FakeTime(new DateTimeOffset(2026, 10, 12, 8, 45, 0, TimeSpan.Zero));
        var fetched = new List<string>();
        using var provider = new CalendarProvider(
            () => [new CalendarLink("Work", "webcal://example.com/work.ics")],
            (url, _) =>
            {
                fetched.Add(url);
                return Task.FromResult(Ics);
            },
            time, London);
        var store = new MetricStore();
        provider.Initialize(store);

        await provider.SampleAsync(store, CancellationToken.None);

        Assert.Equal(["https://example.com/work.ics"], fetched);
        Assert.Equal("Lunch", Text(store, CalendarProvider.NextTitle));
        Assert.Equal("Café", Text(store, CalendarProvider.NextLocation));
        Assert.Equal(new DateTime(2026, 10, 12, 12, 30, 0).ToString("t"), Text(store, CalendarProvider.NextTime));
        Assert.Equal(165, Value(store, CalendarProvider.NextMinutes));
        Assert.Equal("Stand-up", Text(store, CalendarProvider.NowTitle));
        Assert.Equal(3, Value(store, CalendarProvider.Today));
        Assert.Equal("1 calendar", Text(store, CalendarProvider.Status));
        var events = Assert.IsAssignableFrom<IReadOnlyList<CalendarEvent>>(store.GetAttachment(CalendarProvider.Events));
        Assert.Equal(["Bins", "Stand-up", "Lunch"], events.Select(e => e.Title));

        // Not fetched again until 15 minutes have passed.
        time.Advance(TimeSpan.FromMinutes(5));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Single(fetched);
        time.Advance(TimeSpan.FromMinutes(11));
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(2, fetched.Count);
    }

    [Fact]
    public async Task A_link_that_fails_says_so_and_keeps_the_last_events()
    {
        var time = new FakeTime(new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero));
        var fail = false;
        using var provider = new CalendarProvider(
            () => [new CalendarLink("Work", "https://example.com/work.ics")],
            (_, _) => fail ? throw new HttpRequestException("Not found") : Task.FromResult(Ics),
            time, London);
        var store = new MetricStore();
        provider.Initialize(store);
        await provider.SampleAsync(store, CancellationToken.None);

        fail = true;
        time.Advance(TimeSpan.FromMinutes(16));
        await provider.SampleAsync(store, CancellationToken.None);

        Assert.Equal("Couldn't read Work: Not found", Text(store, CalendarProvider.Status));
        Assert.Equal("Stand-up", Text(store, CalendarProvider.NextTitle));
    }

    [Fact]
    public async Task No_links_means_no_readings()
    {
        using var provider = new CalendarProvider(() => [], (_, _) => throw new InvalidOperationException("shouldn't fetch"));
        var store = new MetricStore();
        provider.Initialize(store);
        await provider.SampleAsync(store, CancellationToken.None);

        Assert.Null(Text(store, CalendarProvider.Status));
        Assert.Null(Text(store, CalendarProvider.NextTitle));
        Assert.Null(store.GetAttachment(CalendarProvider.Events));
    }

    [Fact]
    public void Times_read_naturally()
    {
        var now = new DateTime(2026, 10, 12, 9, 0, 0);
        var at = new DateTime(2026, 10, 12, 14, 30, 0);
        Assert.Equal(at.ToString("t"), CalendarProvider.When(at, now));
        Assert.StartsWith("Tomorrow ", CalendarProvider.When(at.AddDays(1), now));
        Assert.StartsWith(at.AddDays(3).ToString("ddd"), CalendarProvider.When(at.AddDays(3), now));
        Assert.StartsWith(at.AddDays(9).ToString("d MMM"), CalendarProvider.When(at.AddDays(9), now));
    }

    private static string? Text(MetricStore store, string id) => store.TryGet(id, out var sample) ? sample.Text : null;

    private static double? Value(MetricStore store, string id) => store.TryGet(id, out var sample) ? sample.Value : null;

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
