using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Demo;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.App.Tests;

public sealed class SunMoonWidgetTests
{
    private static readonly DateTime Evening = new(2026, 10, 11, 21, 47, 0);
    private static readonly Point Centre = new(50, 50);
    private const double Radius = 40;

    [Fact]
    public void A_new_moon_has_nothing_lit_and_a_full_moon_all_of_it()
    {
        Sta.Run(() =>
        {
            Assert.Null(MoonElement.LitShape(0, true, Centre, Radius));
            var full = MoonElement.LitShape(1, true, Centre, Radius)!;
            Assert.InRange(full.GetArea() / (Math.PI * Radius * Radius), 0.99, 1.01);
        });
    }

    [Theory]
    // As seen from the northern hemisphere: a waxing moon's lit on the right, a waning one on the left.
    [InlineData(0.25, true)]
    [InlineData(0.5, true)]
    [InlineData(0.75, true)]
    [InlineData(0.25, false)]
    [InlineData(0.5, false)]
    [InlineData(0.75, false)]
    public void The_lit_part_is_on_its_side_and_as_much_of_the_disc_as_is_lit(double illumination, bool litOnRight)
    {
        Sta.Run(() =>
        {
            var lit = MoonElement.LitShape(illumination, litOnRight, Centre, Radius)!;
            // Measured from the shape drawn with straight lines, so a shade under.
            Assert.InRange(lit.GetArea() / (Math.PI * Radius * Radius * illumination), 0.99, 1.01);

            // Across the middle, the light reaches in from its edge to the line between light and dark: a quarter of the
            // way across for a crescent a quarter lit, halfway at a quarter, three quarters of the way for a gibbous moon.
            var side = litOnRight ? 1 : -1;
            var edge = Centre.X + side * Radius;
            var reach = 2 * Radius * illumination;
            Assert.True(lit.FillContains(new Point(edge - side * (reach - 4), Centre.Y)), "lit just short of the line between light and dark");
            Assert.False(lit.FillContains(new Point(edge - side * (reach + 4), Centre.Y)), "dark just past it");
            Assert.True(lit.FillContains(new Point(edge - side * 2, Centre.Y)), "lit at its edge");
            Assert.False(lit.FillContains(new Point(Centre.X - side * (Radius - 2), Centre.Y)), "dark at the other edge");
        });
    }

    [Theory]
    [InlineData(51.5, true, true)]
    [InlineData(51.5, false, false)]
    [InlineData(-33.9, true, false)]
    [InlineData(-33.9, false, true)]
    public void From_the_southern_hemisphere_it_waxes_on_the_left(double latitude, bool waxing, bool litOnRight)
    {
        Sta.Run(() =>
        {
            var widget = Create(new JsonObject());
            widget.Refresh(Store(Sky(Moon(0.3, waxing), Day(), latitude)), Evening);
            var moon = Descendants<MoonElement>(widget).Single();
            Assert.Equal(litOnRight, moon.LitOnRight);
            Assert.Equal(0.3, moon.Illumination);
        });
    }

    [Fact]
    public void Without_a_location_the_moon_still_shows_and_the_sun_says_where_to_set_one()
    {
        Sta.Run(() =>
        {
            var widget = Create(new JsonObject());
            widget.Refresh(Store(Sky(Moon(0.3, true), null, null)), Evening);

            var arc = Descendants<SunArcElement>(widget).Single();
            Assert.Null(arc.Sun);
            Assert.Equal("Set a location", arc.Text);
            Assert.Equal("on the Weather page", arc.Caption);
            Assert.Contains("Waxing crescent", Texts(widget));
            Assert.Contains("30% lit", Texts(widget));
            // Moonrise and moonset need the location too, so their line takes no room.
            Assert.Contains(Descendants<TextBlock>(widget), t => t.Text.Length == 0 && t.Visibility == Visibility.Collapsed);
        });
    }

    [Fact]
    public void The_sun_goes_along_the_arc_from_sunrise_to_sunset_and_isnt_on_it_at_night()
    {
        var day = Day();
        Assert.Null(SunMoonWidget.Sun(day, At(6)).Sun);
        Assert.Equal(0, SunMoonWidget.Sun(day, At(7)).Sun!.Value, 3);
        Assert.Equal(0.5, SunMoonWidget.Sun(day, At(12.5)).Sun!.Value, 3);
        Assert.Equal(1, SunMoonWidget.Sun(day, At(18)).Sun!.Value, 3);
        Assert.Null(SunMoonWidget.Sun(day, At(21)).Sun);
        Assert.Equal(("11 h 0 min", "daylight"), (SunMoonWidget.Sun(day, At(12)).Text, SunMoonWidget.Sun(day, At(12)).Caption));
    }

    [Fact]
    public void In_the_far_north_and_south_it_can_be_up_or_down_all_day()
    {
        var up = new SunDay(At(0), At(24), null, null, TimeSpan.FromDays(1));
        Assert.Equal((0.5, "Up all day"), (SunMoonWidget.Sun(up, At(12)).Sun!.Value, SunMoonWidget.Sun(up, At(12)).Text));
        var down = new SunDay(At(0), At(24), null, null, TimeSpan.Zero);
        Assert.Equal(((double?)null, "Down all day"), (SunMoonWidget.Sun(down, At(12)).Sun, SunMoonWidget.Sun(down, At(12)).Text));
    }

    [Fact]
    public void Moonrise_says_rose_while_its_up_and_rises_while_its_down()
    {
        var rise = TimeOfDay(At(15.4));
        var set = TimeOfDay(At(26.6));
        var moon = Moon(0.79, true) with { Rise = At(15.4), Set = At(26.6) };
        Assert.Equal($"Rose {rise} · sets {set}", SunMoonWidget.MoonTimes(moon, At(21)));
        Assert.Equal($"Rises {rise} · sets {set}", SunMoonWidget.MoonTimes(moon, At(12)));
        Assert.Equal(string.Empty, SunMoonWidget.MoonTimes(Moon(0.79, true), At(12)));
    }

    [Fact]
    public void The_demo_evening_has_a_waxing_gibbous_moon_after_sunset()
    {
        Sta.Run(() =>
        {
            var store = new MetricStore();
            DemoReadings.Fill(store, Evening);
            var widget = Create(new JsonObject());
            widget.Refresh(store, Evening);

            var arc = Descendants<SunArcElement>(widget).Single();
            Assert.Null(arc.Sun);
            Assert.Equal(TimeOfDay(new DateTimeOffset(Evening.Date.AddHours(7.2))), arc.Rise);
            Assert.Equal(TimeOfDay(new DateTimeOffset(Evening.Date.AddHours(18.6))), arc.Set);
            Assert.Equal("11 h 24 min", arc.Text);
            var texts = Texts(widget);
            Assert.Contains("Waxing gibbous", texts);
            Assert.Contains("79% lit", texts);
            Assert.Contains(texts, t => t.StartsWith("Next full moon ", StringComparison.Ordinal));
            Assert.Contains(texts, t => t.StartsWith("Rose ", StringComparison.Ordinal));
            Assert.True(Descendants<MoonElement>(widget).Single().LitOnRight);
        });
    }

    [Theory]
    [InlineData("sun", true, false)]
    [InlineData("moon", false, true)]
    [InlineData("both", true, true)]
    public void It_shows_the_sun_the_moon_or_both(string show, bool sun, bool moon)
    {
        Sta.Run(() =>
        {
            var widget = Create(new JsonObject { ["show"] = show });
            Assert.Equal(sun, Descendants<SunArcElement>(widget).Any());
            Assert.Equal(moon, Descendants<MoonElement>(widget).Any());
        });
    }

    [Fact]
    public void Text_inside_the_arc_that_doesnt_fit_gets_smaller_unless_told_not_to()
    {
        Sta.Run(() =>
        {
            var shrunk = Laid(new JsonObject { ["show"] = "sun", ["fontSize"] = 80 }, 300, 200);
            Assert.InRange(Descendants<SunArcElement>(shrunk).Single().ShownTextSize, 6, 79);
            Assert.True(new Rect(-1, -1, 302, 202).Contains(VisualTreeHelper.GetDescendantBounds(shrunk)));

            var normal = Laid(new JsonObject { ["show"] = "sun" }, 300, 200);
            Assert.Equal(20, Descendants<SunArcElement>(normal).Single().ShownTextSize);
            var unshrunk = Laid(new JsonObject { ["show"] = "sun", ["fontSize"] = 80, [Setting.ShrinkToFitKey] = false }, 300, 200);
            Assert.Equal(80, Descendants<SunArcElement>(unshrunk).Single().ShownTextSize);
        });
    }

    [Fact]
    public void Text_that_fits_inside_the_arc_doesnt_reach_it()
    {
        // A box standing on the horizon whose top corners touch the inside of the arc, and no further.
        var scale = SunArcElement.InsideScale(200, 60, 100, 100, 5);
        var (width, height) = (200 * scale, 60 * scale);
        Assert.Equal(1, Math.Pow(width / 2 / 100, 2) + Math.Pow((height + 5) / 100, 2), 3);
        Assert.Equal(1, SunArcElement.InsideScale(80, 30, 100, 100, 5));
    }

    private static Widget Create(JsonObject definition)
    {
        definition["type"] = "sun";
        return WidgetFactory.Create(definition, Theme.From(new ThemeSettings()));
    }

    /// <summary>A widget with the demo readings laid out as on the dashboard.</summary>
    private static Widget Laid(JsonObject definition, double width, double height)
    {
        var widget = Create(definition);
        widget.Width = width;
        widget.Height = height;
        var store = new MetricStore();
        DemoReadings.Fill(store, Evening);
        widget.Refresh(store, Evening);
        widget.Measure(new Size(width, height));
        widget.Arrange(new Rect(0, 0, width, height));
        widget.UpdateLayout();
        // Drawing it lays out its text.
        new System.Windows.Media.Imaging.RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32).Render(widget);
        return widget;
    }

    private static MetricStore Store(SkyReport sky)
    {
        var store = new MetricStore();
        store.SetAttachment(MoonProvider.Sky, sky);
        return store;
    }

    private static SkyReport Sky(MoonReport moon, SunDay? sun, double? latitude) => new(At(21), moon, sun, latitude);

    private static MoonReport Moon(double illumination, bool waxing) =>
        new(illumination, waxing, 5, waxing ? "Waxing crescent" : "Waning crescent", At(100), At(400));

    /// <summary>A day with sunrise at 7:00 and sunset at 18:00.</summary>
    private static SunDay Day() => new(At(0), At(24), At(7), At(18), TimeSpan.FromHours(11));

    private static DateTimeOffset At(double hours) => new(Evening.Date.AddHours(hours));

    private static string TimeOfDay(DateTimeOffset time) => MoonProvider.TimeOfDay(time, TimeZoneInfo.Local);

    private static List<string> Texts(DependencyObject root) => [.. Descendants<TextBlock>(root).Select(t => t.Text)];

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var inner in Descendants<T>(child))
                yield return inner;
        }
    }
}
