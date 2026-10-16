using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Tests;

public sealed class CountdownsWidgetTests
{
    private static readonly DateTime Evening = new(2026, 10, 16, 21, 47, 0);

    private static readonly (string Label, string Date)[] Events =
    [
        ("Christmas", "12-25"),
        ("Sam's birthday", "10-16"),
        ("Fun run", "2026-10-01"),
        ("Party", "someday"),
        ("Launch", "2026-10-16 09:00"),
        ("", "10-17"),
        ("Halloween", "10-31"),
    ];

    [Fact]
    public void Events_are_listed_soonest_first_and_ones_that_have_been_are_left_out()
    {
        Assert.Equal(
            [
                new CountdownsWidget.Row("Sam's birthday", "Today", true),
                // Its time has passed, but it's still today.
                new CountdownsWidget.Row("Launch", "Today", true),
                // With no label, the date is shown instead.
                new CountdownsWidget.Row("10-17", "Tomorrow", false),
                new CountdownsWidget.Row("Halloween", "15 days", false),
                new CountdownsWidget.Row("Christmas", "70 days", false),
                // A date it can't read goes last.
                new CountdownsWidget.Row("Party", null, false),
            ],
            CountdownsWidget.Rows(Events, Evening, 10, time: false));
        Assert.Equal(["Sam's birthday", "Launch"], CountdownsWidget.Rows(Events, Evening, 2, time: false).Select(r => r.Label));
        Assert.Empty(CountdownsWidget.Rows([], Evening, 10, time: false));
    }

    [Fact]
    public void The_day_after_a_yearly_one_it_comes_round_again_and_a_one_off_is_gone()
    {
        var rows = CountdownsWidget.Rows(Events, Evening.Date.AddDays(1), 10, time: false);
        Assert.Equal(["10-17", "Halloween", "Christmas", "Sam's birthday", "Party"], rows.Select(r => r.Label));
        Assert.Equal(new CountdownsWidget.Row("10-17", "Today", true), rows[0]);
        Assert.Equal(new CountdownsWidget.Row("Sam's birthday", "364 days", false), rows[3]);
    }

    [Fact]
    public void How_long_reads_in_days_or_in_days_hours_and_minutes()
    {
        var christmas = new DateTime(2026, 12, 25);
        Assert.Equal("70 days", CountdownsWidget.HowLong(christmas, Evening, time: false));
        Assert.Equal("Tomorrow", CountdownsWidget.HowLong(Evening.Date.AddDays(1).AddHours(23), Evening, time: false));
        Assert.Equal("Today", CountdownsWidget.HowLong(Evening.AddHours(1), Evening, time: false));
        Assert.Equal("Today", CountdownsWidget.HowLong(Evening.Date, Evening, time: false));

        Assert.Equal("69d 02:13", CountdownsWidget.HowLong(christmas, Evening, time: true));
        Assert.Equal("0d 01:13", CountdownsWidget.HowLong(Evening.Date.AddHours(23), Evening, time: true));
        // Rounded up, so it only says Today once it's here.
        Assert.Equal("0d 00:01", CountdownsWidget.HowLong(Evening.AddSeconds(20), Evening, time: true));
        Assert.Equal("Today", CountdownsWidget.HowLong(Evening, Evening, time: true));
        Assert.Equal("Today", CountdownsWidget.HowLong(Evening.Date, Evening, time: true));
    }

    [Fact]
    public void The_widget_lists_them_with_todays_in_the_accent_colour()
    {
        Sta.Run(() =>
        {
            var theme = Theme.From(new ThemeSettings());
            var widget = Create(new JsonObject { ["heading"] = "Coming up", ["events"] = Json(Events) });
            widget.Refresh(new MetricStore(), Evening);
            var texts = Texts(widget);
            // Five rows unless told otherwise.
            Assert.Equal(
                ["Coming up", "Sam's birthday", "Today", "Launch", "Today", "10-17", "Tomorrow", "Halloween", "15 days", "Christmas", "70 days"],
                texts.Select(t => t.Text));
            Assert.All(texts.Take(5).Skip(1), t => Assert.Equal(Colour(theme.Accent), Colour(t.Foreground)));
            Assert.Equal(Colour(theme.Foreground), Colour(texts[7].Foreground));
            Assert.Equal(Colour(theme.Secondary), Colour(texts[8].Foreground));

            // Nothing on show changed a second later, so nothing was built afresh.
            var first = ((Panel)widget.Children[0]).Children[0];
            widget.Refresh(new MetricStore(), Evening.AddSeconds(1));
            Assert.Same(first, ((Panel)widget.Children[0]).Children[0]);
            widget.Refresh(new MetricStore(), Evening.Date.AddDays(1));
            Assert.Equal(["Coming up", "10-17", "Today"], Texts(widget).Take(3).Select(t => t.Text));
        });
    }

    [Fact]
    public void The_time_shows_by_the_minute_and_a_date_it_cant_read_says_so()
    {
        Sta.Run(() =>
        {
            var widget = Create(new JsonObject { ["style"] = "time", ["events"] = Json(("Christmas", "12-25"), ("Party", "someday")) });
            widget.Refresh(new MetricStore(), Evening);
            Assert.Equal(["Christmas", "69d 02:13", "Party", "Unknown date"], Texts(widget).Select(t => t.Text));
            Assert.Equal(Colour(Theme.From(new ThemeSettings()).Critical), Colour(Texts(widget)[3].Foreground));
            widget.Refresh(new MetricStore(), Evening.AddMinutes(1));
            Assert.Equal("69d 02:12", Texts(widget)[1].Text);
        });
    }

    [Fact]
    public void With_nothing_to_list_it_says_so_or_shows_nothing()
    {
        Sta.Run(() =>
        {
            var empty = Create(new JsonObject { ["heading"] = "Coming up", ["events"] = new JsonArray() });
            empty.Refresh(new MetricStore(), Evening);
            Assert.Equal(["Coming up", "Nothing coming up"], Texts(empty).Select(t => t.Text));

            var quiet = Create(new JsonObject { ["heading"] = "Coming up", ["emptyText"] = "", ["events"] = Json(("Fun run", "2026-10-01")) });
            quiet.Refresh(new MetricStore(), Evening);
            Assert.Empty(Texts(quiet));
        });
    }

    [Fact]
    public void A_new_widget_counts_down_to_the_holidays()
    {
        Sta.Run(() =>
        {
            var widget = Create(new JsonObject());
            widget.Refresh(new MetricStore(), Evening);
            Assert.Equal(
                ["Halloween", "15 days", "Christmas", "70 days", "New Year", "77 days", "Valentine's Day", "121 days"],
                Texts(widget).Select(t => t.Text));
        });
    }

    [Fact]
    public void Rows_that_dont_fit_are_left_out_but_the_heading_and_first_row_stay()
    {
        Sta.Run(() =>
        {
            var widget = Create(new JsonObject { ["heading"] = "Coming up", ["fontSize"] = 40 });
            widget.Refresh(new MetricStore(), Evening);
            var list = Assert.IsType<WholeRows>(widget.Children[0]);
            Lay(widget, 300, 400);
            Assert.Equal(4, list.Shown);
            Lay(widget, 300, 160);
            Assert.Equal(2, list.Shown);
            // Too short for even the first: it and the heading get smaller together.
            Lay(widget, 300, 50);
            Assert.Equal(1, list.Shown);
            Assert.Equal(["Coming up", "Halloween", "15 days"], Texts(list.Children[0]).Select(t => t.Text));
        });
    }

    private static CountdownsWidget Create(JsonObject settings)
    {
        settings["type"] = "countdowns";
        return Assert.IsType<CountdownsWidget>(WidgetFactory.Create(settings, Theme.From(new ThemeSettings())));
    }

    private static JsonArray Json(params (string Label, string Date)[] events) =>
        new([.. events.Select(e => (JsonNode)new JsonObject { ["label"] = e.Label, ["date"] = e.Date })]);

    private static void Lay(FrameworkElement element, double width, double height)
    {
        element.Width = width;
        element.Height = height;
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    private static Color Colour(Brush brush) => ((SolidColorBrush)brush).Color;

    /// <summary>The text in <paramref name="root"/>, top to bottom and left to right.</summary>
    private static List<TextBlock> Texts(DependencyObject root)
    {
        var texts = new List<TextBlock>();
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock text)
                texts.Add(text);
            texts.AddRange(Texts(child));
        }
        return texts;
    }
}
