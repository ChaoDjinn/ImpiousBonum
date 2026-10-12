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
            Assert.Contains(texts, t => t.Contains("Weather page"));
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
    public void The_weather_now_can_be_left_out_for_just_a_forecast()
    {
        Sta.Run(() =>
        {
            var widget = WidgetFactory.Create(new JsonObject { ["type"] = "weather", ["showCurrent"] = false, ["forecast"] = "hours" }, Theme.From(new ThemeSettings()));
            var store = new MetricStore();
            store.SetAttachment(WeatherProvider.Forecast, Report());
            widget.Refresh(store, DateTime.Now);

            // The hours ahead have temperatures of their own; it's the big one for now (80 by default) that's gone.
            Assert.DoesNotContain(Descendants<TextBlock>(widget), t => t.FontSize >= 80);
            Assert.DoesNotContain("Cloudy", Texts(widget));
            var hours = Descendants<UniformGrid>(widget).Single();
            Assert.Equal(2, Grid.GetColumnSpan(hours));
            Assert.Equal(2, hours.Children.Count);

            var empty = WidgetFactory.Create(new JsonObject { ["type"] = "weather", ["showCurrent"] = false }, Theme.From(new ThemeSettings()));
            empty.Refresh(new MetricStore(), DateTime.Now);
            Assert.Contains(Texts(empty), t => t.Contains("Weather page"));
        });
    }

    [Fact]
    public void Colour_icons_are_drawn_in_layers_and_text_takes_the_widgets_own_colours()
    {
        Sta.Run(() =>
        {
            var widget = WidgetFactory.Create(new JsonObject
            {
                ["type"] = "weather", ["iconStyle"] = "colour", ["textColor"] = "#FF0000", ["detailColor"] = "#00FF00",
            }, Theme.From(new ThemeSettings()));
            var store = new MetricStore();
            store.SetAttachment(WeatherProvider.Forecast, Report());
            widget.Refresh(store, DateTime.Now);

            Assert.Contains(Descendants<Canvas>(widget), c => c.Children.Count >= 1);
            var temperature = Descendants<TextBlock>(widget).First(t => t.Text == "11°");
            Assert.Equal(Colors.Red, ((SolidColorBrush)temperature.Foreground).Color);
            var feels = Descendants<TextBlock>(widget).First(t => t.Text.Contains("Feels"));
            Assert.Equal(Colors.Lime, ((SolidColorBrush)feels.Foreground).Color);
        });
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(0, false)]
    [InlineData(2, true)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(45, true)]
    [InlineData(51, true)]
    [InlineData(61, true)]
    [InlineData(66, true)]
    [InlineData(71, true)]
    [InlineData(95, true)]
    public void Every_weather_icon_has_a_colour_version(int code, bool isDay)
    {
        var name = WeatherCodes.Icon(code, isDay);
        Assert.True(Icons.HasColour(name), name);
        Sta.Run(() => Assert.IsType<Viewbox>(Icons.CreateColour(name, 40, 1.6, Brushes.White)));
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

    [Fact]
    public void The_chance_of_rain_shows_from_the_percentage_chosen()
    {
        Sta.Run(() =>
        {
            List<string> Chances(JsonObject settings)
            {
                var widget = WidgetFactory.Create(settings, Theme.From(new ThemeSettings()));
                var store = new MetricStore();
                store.SetAttachment(WeatherProvider.Forecast, Report());
                widget.Refresh(store, DateTime.Now);
                return Texts(widget).Where(t => t.EndsWith('%')).ToList();
            }

            // From 30% unless the widget says otherwise, as it always was.
            Assert.Equal(new[] { "45%" }, Chances(new JsonObject { ["type"] = "weather", ["count"] = 2 }));
            Assert.Equal(new[] { "0%", "45%" }, Chances(new JsonObject { ["type"] = "weather", ["count"] = 2, ["rainFrom"] = 0 }));
            Assert.Empty(Chances(new JsonObject { ["type"] = "weather", ["count"] = 2, ["rainFrom"] = 50 }));
            Assert.Equal(new[] { "40%", "0%" }, Chances(new JsonObject { ["type"] = "weather", ["forecast"] = "hours", ["rainFrom"] = 0 }));
        });
    }

    [Fact]
    public void A_forecast_can_run_to_16_days()
    {
        Sta.Run(() =>
        {
            var days = Enumerable.Range(0, 16).Select(d => new WeatherPeriod(new DateTime(2026, 10, 11).AddDays(d), 15, 9, 3, true, 0)).ToList();
            var store = new MetricStore();
            store.SetAttachment(WeatherProvider.Forecast, Report() with { Days = days });

            foreach (var (count, shown) in new[] { (16, 16), (48, 16), (5, 5) })
            {
                var widget = WidgetFactory.Create(new JsonObject { ["type"] = "weather", ["count"] = count }, Theme.From(new ThemeSettings()));
                widget.Refresh(store, DateTime.Now);
                Assert.Equal(shown, Descendants<UniformGrid>(widget).Single().Children.Count);
            }
            Assert.Equal(48, WeatherWidget.Descriptor.Find("count")!.Max);
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

    [Fact]
    public void Air_quality_is_off_until_turned_on_and_needs_a_location()
    {
        var settings = new AppSettings();
        Assert.False(settings.AirQuality);
        Assert.Equal(AppSettings.AirQualityScaleAuto, settings.AirQualityScale);
        Assert.Null(settings.AirQualityRequest());

        // A location alone isn't enough, and nor is turning it on without one.
        settings.WeatherLocation = new WeatherLocation("Leeds, England, United Kingdom", 53.8, -1.55);
        Assert.Null(settings.AirQualityRequest());
        Assert.Null(new AppSettings { AirQuality = true }.AirQualityRequest());

        settings.AirQuality = true;
        Assert.Equal(new AirQualityRequest(53.8, -1.55, AqiScale.Automatic), settings.AirQualityRequest());
    }

    [Fact]
    public void Air_quality_is_saved_with_its_scale()
    {
        var settings = new AppSettings
        {
            WeatherLocation = new WeatherLocation("Leeds, England, United Kingdom", 53.8, -1.55), AirQuality = true, AirQualityScale = AppSettings.AirQualityScaleUs,
        };
        var json = JsonSerializer.Serialize(settings, JsonDefaults.Options);
        var restored = JsonSerializer.Deserialize<AppSettings>(json, JsonDefaults.Options)!;

        Assert.Contains("\"airQuality\": true", json);
        Assert.Contains("\"airQualityScale\": \"us\"", json);
        Assert.Equal(new AirQualityRequest(53.8, -1.55, AqiScale.Us), restored.AirQualityRequest());
        // Settings from before air quality was there leave it off.
        Assert.False(JsonSerializer.Deserialize<AppSettings>("""{ "weatherLocation": { "name": "Leeds", "latitude": 53.8, "longitude": -1.55 } }""", JsonDefaults.Options)!.AirQuality);

        Assert.Equal(AqiScale.European, AppSettings.ResolveAirQualityScale("European"));
        Assert.Equal(AqiScale.Automatic, AppSettings.ResolveAirQualityScale("something else"));
        Assert.Equal(AqiScale.Automatic, AppSettings.ResolveAirQualityScale(null));
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
