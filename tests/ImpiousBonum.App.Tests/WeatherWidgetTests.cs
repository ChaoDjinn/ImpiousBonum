using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.App.Tests;

public sealed class WeatherWidgetTests
{
    private static WeatherReport Report() => new(
        WeatherUnits.Metric, new DateTime(2026, 10, 11, 9, 45, 0), 10.9, 8.8, 72, 7.6, 283, 3, true,
        [
            new WeatherPeriod(new DateTime(2026, 10, 11, 9, 0, 0), 10.1, null, 3, true, 0),
            new WeatherPeriod(new DateTime(2026, 10, 11, 10, 0, 0), 11.1, null, 61, true, 40),
            new WeatherPeriod(new DateTime(2026, 10, 11, 11, 0, 0), 12.1, null, 3, true, 0),
        ],
        [
            new WeatherPeriod(new DateTime(2026, 10, 11), 15.5, 9.5, 3, true, 0),
            new WeatherPeriod(new DateTime(2026, 10, 12), 17.3, 13.3, 51, true, 45),
        ],
        null, null);

    [Fact]
    public void Without_a_forecast_it_says_how_to_get_one()
    {
        Sta.Run(() =>
        {
            var widget = WidgetFactory.Create(new JsonObject { ["type"] = "weather" }, Theme.From(new ThemeSettings()));
            widget.Refresh(new MetricStore(), DateTime.Now);
            var texts = Texts(widget);
            Assert.Contains("—", texts);
            Assert.Contains(texts, t => t.Contains("Set location"));
        });
    }

    [Fact]
    public void A_forecast_shows_now_and_the_days_ahead()
    {
        Sta.Run(() =>
        {
            var widget = WidgetFactory.Create(new JsonObject { ["type"] = "weather", ["count"] = 2 }, Theme.From(new ThemeSettings()));
            var store = new MetricStore();
            store.SetAttachment(WeatherProvider.Forecast, Report());
            widget.Refresh(store, DateTime.Now);

            var texts = Texts(widget);
            Assert.Contains("11°", texts);
            Assert.Contains("Cloudy", texts);
            Assert.Contains("Today", texts);
            Assert.Contains("45%", texts);
            var days = Descendants<UniformGrid>(widget).Single();
            Assert.Equal(2, days.Children.Count);
        });
    }

    [Fact]
    public void An_hourly_forecast_starts_after_the_current_hour()
    {
        Sta.Run(() =>
        {
            var widget = WidgetFactory.Create(new JsonObject { ["type"] = "weather", ["forecast"] = "hours", ["count"] = 5 }, Theme.From(new ThemeSettings()));
            var store = new MetricStore();
            store.SetAttachment(WeatherProvider.Forecast, Report());
            widget.Refresh(store, DateTime.Now);
            Assert.Equal(2, Descendants<UniformGrid>(widget).Single().Children.Count);
        });
    }

    [Theory]
    [InlineData(10.4, "10°")]
    [InlineData(-0.4, "0°")]
    [InlineData(-2.6, "-3°")]
    [InlineData(null, "—")]
    public void Temperatures_show_as_whole_degrees(double? value, string expected)
    {
        Assert.Equal(expected, WeatherWidget.Degrees(value));
    }

    [Fact]
    public void The_location_is_saved_and_units_follow_the_setting()
    {
        var settings = new AppSettings { WeatherLocation = new WeatherLocation("Leeds, England, United Kingdom", 53.8, -1.55), WeatherUnits = AppSettings.WeatherUnitsImperial };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, JsonDefaults.Options), JsonDefaults.Options)!;

        Assert.Equal(settings.WeatherLocation, restored.WeatherLocation);
        Assert.Equal(new WeatherRequest("Leeds, England, United Kingdom", 53.8, -1.55, WeatherUnits.Imperial), restored.WeatherRequest());
        Assert.Equal(WeatherUnits.Metric, AppSettings.ResolveWeatherUnits(AppSettings.WeatherUnitsMetric));
        Assert.Null(new AppSettings().WeatherRequest());
    }

    private static List<string> Texts(DependencyObject root) =>
        Descendants<TextBlock>(root).Select(t => string.Concat(t.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text)) is { Length: > 0 } runs ? runs : t.Text).ToList();

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
