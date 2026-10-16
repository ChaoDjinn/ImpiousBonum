using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Demo;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.App.Tests;

public sealed class RainWidgetTests
{
    /// <summary>When the screenshots are drawn: the demo's shower is due at half past ten.</summary>
    private static readonly DateTime Evening = new(2026, 10, 11, 21, 47, 0);

    [Fact]
    public void Without_a_forecast_it_says_how_to_get_one()
    {
        InBritain(() =>
        {
            foreach (var showSummary in new[] { true, false })
            {
                var widget = Rain(new JsonObject { ["showSummary"] = showSummary }, new MetricStore());
                // Even with the summary off, as nothing else would say why there are no bars.
                Assert.Equal("No weather yet: set a location on the Weather page", Summary(widget).Text);
                Assert.True(Shown(Summary(widget), widget));
                Assert.Empty(Chart(widget).Heights);
                Assert.Empty(Chart(widget).TimesShown);
            }

            // A forecast without the quarter-hours, which a new one always has.
            var old = new MetricStore();
            old.SetAttachment(WeatherProvider.Forecast, ((WeatherReport)Demo().GetAttachment(WeatherProvider.Forecast)!) with { QuarterHours = [] });
            Assert.Equal("No rain forecast yet", Summary(Rain(new JsonObject(), old)).Text);
        });
    }

    [Fact]
    public void The_demo_shower_shows_as_bars_after_the_summary()
    {
        InBritain(() =>
        {
            var store = Demo();
            var widget = Rain(new JsonObject { ["barColor"] = "#FF0000", ["textColor"] = "#00FF00" }, store);

            // The same as the readings say: in about 40 minutes.
            Assert.True(store.TryGet(WeatherProvider.RainSoon, out var soon));
            Assert.True(store.TryGet(WeatherProvider.RainNext, out var next));
            Assert.Equal("Rain from 22:30", soon.Text);
            Assert.Equal(43, next.Value);
            Assert.Equal("Rain from 22:30", Summary(widget).Text);
            Assert.Equal(Colors.Lime, ((SolidColorBrush)Summary(widget).Foreground).Color);
            // A bar a quarter of an hour from 21:45: dry until half past ten, and heaviest from eleven to a quarter past.
            var heights = Chart(widget).Heights;
            Assert.Equal(RainAhead.Length, heights.Count);
            Assert.Equal([0, 0, 0], heights.Take(3));
            Assert.All(heights.Skip(3), height => Assert.True(height > 0));
            Assert.Equal(heights.Max(), heights[5]);
            // The time every half hour under them, and the bars in their colour above the times.
            Assert.Equal(["22:00", "22:30", "23:00", "23:30"], Chart(widget).TimesShown);
            Assert.True(Drawn(widget, c => c == Colors.Red) > 1000);
        });
    }

    [Fact]
    public void The_summary_can_be_left_out_for_more_room_for_the_bars()
    {
        InBritain(() =>
        {
            var with = Rain(new JsonObject(), Demo());
            var without = Rain(new JsonObject { ["showSummary"] = false }, Demo());
            Assert.True(Shown(Summary(with), with));
            Assert.False(Shown(Summary(without), without));
            Assert.True(Chart(without).ActualHeight > Chart(with).ActualHeight + 20);
            Assert.Equal(160, Chart(without).ActualHeight, 0.5);
        });
    }

    [Fact]
    public void Its_the_rain_at_the_place_as_it_moves_on()
    {
        InBritain(() =>
        {
            // Paris's shower at a quarter to eight there, two hours ahead of UTC, whatever the PC's time zone.
            var start = new DateTime(2026, 10, 16, 19, 0, 0);
            double[] amounts = [0, 0, 0, 0, 0.3, 0.1, 0.3, 0.4, 0.2, 0, 0, 0];
            var report = new WeatherReport(WeatherUnits.Metric, start, 16, 14, 74, 16, 327, 3, false, [], [], null, null)
            {
                UtcOffset = TimeSpan.FromHours(2),
                QuarterHours = [.. amounts.Select((amount, i) => new QuarterHour(start.AddMinutes(15 * i), amount))],
            };
            var store = new MetricStore();
            store.SetAttachment(WeatherProvider.Forecast, report);
            var utc = new DateTimeOffset(2026, 10, 16, 17, 11, 0, TimeSpan.Zero);

            var widget = Rain(new JsonObject(), store, now: utc.LocalDateTime);
            Assert.Equal("Rain from 19:45", Summary(widget).Text);
            // From the start of the quarter-hour it is now to two hours on, which are on the hour here.
            Assert.Equal(["19:00", "19:30", "20:00", "20:30", "21:00"], Chart(widget).TimesShown);

            // Without a new forecast, the next quarter-hour moves it on.
            widget.Refresh(store, utc.AddMinutes(39).LocalDateTime);
            Assert.Equal("Rain stopping around 21:00", Summary(widget).Text);
            Assert.True(Chart(widget).Heights[0] > 0);
        });
    }

    [Fact]
    public void Bars_are_taller_the_heavier_the_rain_and_a_drizzle_still_shows()
    {
        // Dry is no bar at all; a drizzle has a little one.
        Assert.Equal(0, RainWidget.BarHeight(0, WeatherUnits.Metric));
        Assert.Equal(0, RainWidget.BarHeight(0.01, WeatherUnits.Metric));
        Assert.Equal(0.15, RainWidget.BarHeight(0.055, WeatherUnits.Metric), 3);
        // Light rain (to 2.5 mm an hour) reaches half way, heavy rain (from 10) the top.
        Assert.Equal(0.5, RainWidget.BarHeight(0.625, WeatherUnits.Metric), 3);
        Assert.Equal(1, RainWidget.BarHeight(2.5, WeatherUnits.Metric), 3);
        Assert.Equal(1, RainWidget.BarHeight(6, WeatherUnits.Metric), 3);
        // Inches are inches.
        Assert.Equal(RainWidget.BarHeight(0.254, WeatherUnits.Metric), RainWidget.BarHeight(0.01, WeatherUnits.Imperial), 6);

        double[] amounts = [0.1, 0.2, 0.4, 0.8, 1.6, 2.4];
        var heights = amounts.Select(a => RainWidget.BarHeight(a, WeatherUnits.Metric)).ToList();
        Assert.Equal(heights.Order(), heights);
        Assert.Equal(heights.Count, heights.Distinct().Count());
    }

    [Fact]
    public void Times_shrink_to_fit_a_narrow_widget_or_without_shrink_to_fit_thin_out()
    {
        InBritain(() =>
        {
            var shrunk = Chart(Rain(new JsonObject(), Demo(), width: 140));
            Assert.Equal(["22:00", "22:30", "23:00", "23:30"], shrunk.TimesShown);
            Assert.True(shrunk.ShownTimeSize < 22 * 0.75);

            var thinned = Chart(Rain(new JsonObject { [Setting.ShrinkToFitKey] = false }, Demo(), width: 140));
            Assert.Equal(22 * 0.75, thinned.ShownTimeSize);
            Assert.InRange(thinned.TimesShown.Count, 1, 3);
            Assert.Equal("22:00", thinned.TimesShown[0]);

            // Everything it draws stays inside it.
            var widget = Rain(new JsonObject { ["fontSize"] = 60 }, Demo(), width: 200, height: 100);
            Assert.True(new Rect(-1, -1, 202, 102).Contains(ShrinkToFit.Ink(widget)), ShrinkToFit.Ink(widget).ToString());
        });
    }

    /// <summary>On a thread of its own in British English, as the expected times are written.</summary>
    private static void InBritain(Action action) => Sta.Run(() =>
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
        action();
    });

    private static MetricStore Demo()
    {
        var store = new MetricStore();
        DemoReadings.Fill(store, Evening);
        return store;
    }

    private static Widget Rain(JsonObject settings, MetricStore store, double width = 420, double height = 160, DateTime? now = null)
    {
        settings["type"] = "rain";
        var widget = WidgetFactory.Create(settings, Theme.From(new ThemeSettings()));
        widget.Refresh(store, now ?? Evening);
        widget.Measure(new Size(width, height));
        widget.Arrange(new Rect(0, 0, width, height));
        widget.UpdateLayout();
        return widget;
    }

    private static TextBlock Summary(Widget widget) => Descendants<TextBlock>(widget).Single();

    /// <summary>The bars and times, drawn as they are now.</summary>
    private static RainWidget.Chart Chart(Widget widget)
    {
        widget.UpdateLayout();
        return Descendants<RainWidget.Chart>(widget).Single();
    }

    /// <summary>Whether <paramref name="element"/> shows in <paramref name="widget"/>: neither it nor anything it's in is hidden.</summary>
    private static bool Shown(UIElement element, Widget widget)
    {
        for (DependencyObject? visual = element; visual is not null && !ReferenceEquals(visual, widget); visual = VisualTreeHelper.GetParent(visual))
        {
            if (visual is UIElement { Visibility: not Visibility.Visible })
                return false;
        }
        return true;
    }

    /// <summary>How many pixels of the widget drawn at its size have a colour that <paramref name="match"/>es.</summary>
    private static int Drawn(Widget widget, Func<Color, bool> match)
    {
        var bitmap = Render(widget);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var count = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] == 255 && match(Color.FromRgb(pixels[i + 2], pixels[i + 1], pixels[i])))
                count++;
        }
        return count;
    }

    private static RenderTargetBitmap Render(Widget widget)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(widget.ActualWidth), (int)Math.Ceiling(widget.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(widget);
        return bitmap;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var deeper in Descendants<T>(child))
                yield return deeper;
        }
    }
}
