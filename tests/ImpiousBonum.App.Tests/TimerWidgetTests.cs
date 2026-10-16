using System.Text.Json.Nodes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Tests;

public sealed class TimerWidgetTests
{
    private static readonly DateTime Noon = new(2026, 10, 11, 12, 0, 0);

    [Theory]
    [InlineData("25:00", 25 * 60)]
    [InlineData("1:30:00", 90 * 60)]
    [InlineData("0:45", 45)]
    [InlineData("90", 90 * 60)]
    [InlineData("1.5", 90)]
    public void Lengths_read_as_minutes_and_seconds(string text, double seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), TimerWidget.ParseDuration(text));

    [Theory]
    [InlineData("")]
    [InlineData("0:00")]
    [InlineData("soon")]
    [InlineData("1:2:3:4")]
    [InlineData("-5")]
    public void Unreadable_lengths_are_null(string text) => Assert.Null(TimerWidget.ParseDuration(text));

    [Fact]
    public void A_time_of_day_is_its_next_occurrence()
    {
        Assert.Equal(Noon.Date.AddHours(18), TimerWidget.ParseTarget("18:00", Noon));
        Assert.Equal(Noon.Date.AddDays(1).AddHours(9), TimerWidget.ParseTarget("09:00", Noon));
    }

    [Fact]
    public void Dates_read_with_or_without_a_time()
    {
        Assert.Equal(new DateTime(2026, 12, 25), TimerWidget.ParseTarget("2026-12-25", Noon));
        Assert.Equal(new DateTime(2026, 12, 25, 9, 30, 0), TimerWidget.ParseTarget("2026-12-25 09:30", Noon));
        Assert.Null(TimerWidget.ParseTarget("whenever", Noon));
    }

    [Fact]
    public void A_month_and_day_comes_round_every_year()
    {
        Assert.Equal(new DateTime(2026, 12, 25), TimerWidget.ParseTarget("12-25", Noon));
        Assert.Equal(new DateTime(2026, 10, 31, 18, 0, 0), TimerWidget.ParseTarget("10-31 18:00", Noon));
        // Still today's while today lasts, so the day itself can show it's here; then next year's.
        Assert.Equal(new DateTime(2026, 10, 11), TimerWidget.ParseTarget("10-11", Noon));
        Assert.Equal(new DateTime(2027, 1, 1), TimerWidget.ParseTarget("1-1", Noon));
        Assert.Equal(new DateTime(2027, 10, 11), TimerWidget.ParseTarget("10-11", Noon.Date.AddDays(1)));
        // 29 February waits for a leap year.
        Assert.Equal(new DateTime(2028, 2, 29), TimerWidget.ParseTarget("02-29", Noon));
        Assert.Null(TimerWidget.ParseTarget("12-32", Noon));
        Assert.Null(TimerWidget.ParseTarget("02-30", Noon));
        Assert.Null(TimerWidget.ParseTarget("12-25 24:00", Noon));
    }

    [Theory]
    [InlineData(0, true, "0:00")]
    [InlineData(0.2, true, "0:01")]
    [InlineData(0.8, false, "0:00")]
    [InlineData(65, false, "1:05")]
    [InlineData(3_725, false, "1:02:05")]
    [InlineData(3 * 86_400 + 3_725, false, "3d 01:02:05")]
    public void Times_show_in_whole_seconds(double seconds, bool roundUp, string expected) =>
        Assert.Equal(expected, TimerWidget.Format(TimeSpan.FromSeconds(seconds), roundUp));

    [Fact]
    public void A_clock_counts_only_while_running()
    {
        var state = TimerClock.For($"test|{Guid.NewGuid()}");
        state.Toggle(Noon);
        state.Toggle(Noon.AddSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(10), state.Elapsed(Noon.AddMinutes(5)));
        state.Toggle(Noon.AddMinutes(5));
        Assert.Equal(TimeSpan.FromSeconds(15), state.Elapsed(Noon.AddMinutes(5).AddSeconds(5)));
        Assert.True(state.Announce());
        Assert.False(state.Announce());
        state.Reset(Noon);
        Assert.Equal(TimeSpan.Zero, state.Elapsed(Noon.AddHours(1)));
        Assert.True(state.Announce());
    }

    [Fact]
    public void A_countdown_runs_out_and_a_tap_starts_it_again()
    {
        Sta.Run(() =>
        {
            var label = Guid.NewGuid().ToString();
            var widget = Create(new JsonObject { ["mode"] = "countdown", ["duration"] = "1:00", ["label"] = label });
            var store = new MetricStore();

            widget.Refresh(store, Noon);
            Assert.Equal("1:00", widget.ShownText);
            widget.Toggle(Noon);
            widget.Refresh(store, Noon.AddSeconds(15));
            Assert.Equal("0:45", widget.ShownText);
            widget.Refresh(store, Noon.AddSeconds(61));
            Assert.Equal("Time's up", widget.ShownText);

            // Rebuilt (a page swipe, an edit): the same timer carries on.
            var again = Create(new JsonObject { ["mode"] = "countdown", ["duration"] = "1:00", ["label"] = label });
            again.Refresh(store, Noon.AddSeconds(62));
            Assert.Equal("Time's up", again.ShownText);

            again.Toggle(Noon.AddSeconds(70));
            Assert.Equal("1:00", again.ShownText);
        });
    }

    [Fact]
    public void A_stopwatch_pauses_and_resets()
    {
        Sta.Run(() =>
        {
            var widget = Create(new JsonObject { ["mode"] = "stopwatch", ["label"] = Guid.NewGuid().ToString() });
            var store = new MetricStore();
            widget.Toggle(Noon);
            widget.Refresh(store, Noon.AddSeconds(75.5));
            Assert.Equal("1:15", widget.ShownText);
            widget.Toggle(Noon.AddSeconds(80));
            widget.Refresh(store, Noon.AddHours(1));
            Assert.Equal("1:20", widget.ShownText);
            widget.Reset(Noon.AddHours(1));
            Assert.Equal("0:00", widget.ShownText);
        });
    }

    [Fact]
    public void Until_counts_down_to_a_date()
    {
        Sta.Run(() =>
        {
            var widget = Create(new JsonObject { ["mode"] = "until", ["target"] = "2026-10-12 12:00", ["label"] = Guid.NewGuid().ToString() });
            widget.Refresh(new MetricStore(), Noon.AddSeconds(-5));
            Assert.Equal("1d 00:00:05", widget.ShownText);
            widget.Refresh(new MetricStore(), new DateTime(2026, 10, 12, 12, 0, 1));
            Assert.Equal("Time's up", widget.ShownText);
        });
    }

    [Fact]
    public void Until_a_yearly_day_shows_done_all_that_day_then_counts_to_next_year()
    {
        Sta.Run(() =>
        {
            var widget = Create(new JsonObject { ["mode"] = "until", ["target"] = "12-25", ["doneText"] = "Merry Christmas", ["label"] = Guid.NewGuid().ToString() });
            var store = new MetricStore();
            widget.Refresh(store, new DateTime(2026, 12, 24, 12, 0, 0));
            Assert.Equal("12:00:00", widget.ShownText);
            widget.Refresh(store, new DateTime(2026, 12, 25, 23, 59, 0));
            Assert.Equal("Merry Christmas", widget.ShownText);
            widget.Refresh(store, new DateTime(2026, 12, 26, 0, 0, 1));
            Assert.Equal("363d 23:59:59", widget.ShownText);
        });
    }

    private static TimerWidget Create(JsonObject settings)
    {
        settings["type"] = "timer";
        return Assert.IsType<TimerWidget>(WidgetFactory.Create(settings, Theme.From(new ThemeSettings())));
    }
}
