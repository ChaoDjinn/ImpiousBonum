using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Weather;
using static ImpiousBonum.App.Widgets.WeatherWidget;

namespace ImpiousBonum.App.Tests;

/// <summary>What the weather widget shows beside the temperature and under each day or hour.</summary>
public sealed class WeatherDetailsTests
{
    private static readonly DateTime Morning = new(2026, 10, 11, 9, 45, 0);

    /// <summary>
    /// Cloudy and 11° at 9:45, with a south-west wind and the pressure falling 3 hPa since 6:00; today and tomorrow, and
    /// the hours to noon. In <paramref name="units"/>, with the numbers Open-Meteo would give in them.
    /// </summary>
    private static WeatherReport Report(WeatherUnits units = WeatherUnits.Metric)
    {
        var imperial = units == WeatherUnits.Imperial;
        List<WeatherPeriod> hours =
        [
            new(Morning.Date.AddHours(6), 9, null, 3, true, 0) { Pressure = 1015.5 },
            new(Morning.Date.AddHours(9), 10, null, 3, true, 0) { Pressure = 1012.5 },
            new(Morning.Date.AddHours(10), 11, null, 61, true, 40) { Pressure = 1012, Precipitation = imperial ? 0.02 : 0.6, Wind = imperial ? 9 : 14, Gusts = imperial ? 21 : 33, Uv = 2 },
            new(Morning.Date.AddHours(11), 12, null, 3, true, 0) { Pressure = 1011.5, Precipitation = 0, Wind = imperial ? 8 : 13, Gusts = imperial ? 20 : 31, Uv = 3 },
        ];
        List<WeatherPeriod> days =
        [
            new(Morning.Date, imperial ? 60.1 : 15.5, imperial ? 49.6 : 9.5, 3, true, 0)
            {
                Precipitation = 0, Wind = imperial ? 12.4 : 20, Gusts = imperial ? 25.5 : 41, Uv = 3.4, Daylight = TimeSpan.FromMinutes(649),
            },
            new(Morning.Date.AddDays(1), imperial ? 63.1 : 17.3, imperial ? 55.9 : 13.3, 51, true, 45)
            {
                Precipitation = imperial ? 0.244 : 6.2, Wind = imperial ? 18.6 : 30, Gusts = imperial ? 34.2 : 55, Uv = 6.6,
            },
        ];
        return new WeatherReport(units, Morning, imperial ? 51.6 : 10.9, imperial ? 47.8 : 8.8, 72, imperial ? 7.2 : 11.6, 225, 3, true, hours, days,
            Morning.Date.AddHours(7.2), Morning.Date.AddHours(18.6))
        {
            Gusts = imperial ? 25.3 : 40.2, Uv = 3.2, Pressure = 1012.4, DewPoint = imperial ? 43.4 : 6.4, Visibility = 24135, Clouds = 75,
        };
    }

    /// <summary>Air quality and pollen as their provider leaves them while they're on, or with nothing while they're off.</summary>
    private static MetricStore Store(WeatherReport? report = null, bool airQuality = false)
    {
        var store = new MetricStore();
        new WeatherProvider(() => null).Initialize(store);
        new AirQualityProvider(() => null).Initialize(store);
        store.SetAttachment(WeatherProvider.Forecast, report ?? Report());
        if (airQuality)
        {
            store.SetText(AirQualityProvider.Level, "Fair");
            store.SetText(AirQualityProvider.Pollen, "Grass high");
        }
        return store;
    }

    private static Widget Weather(JsonObject settings, MetricStore? store = null)
    {
        settings["type"] = "weather";
        var widget = WidgetFactory.Create(settings, Theme.From(new ThemeSettings()));
        widget.Refresh(store ?? Store(), Morning);
        return widget;
    }

    /// <summary>On a thread of its own in British English, as the expected texts are written.</summary>
    private static void InBritain(Action action) => Sta.Run(() =>
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
        action();
    });

    // ---- Beside the temperature ---------------------------------------------------------------------

    [Fact]
    public void Without_details_chosen_it_shows_what_it_always_has()
    {
        InBritain(() =>
        {
            // A layout from before Details to show, and one choosing what it starts with.
            foreach (var settings in new[] { new JsonObject(), new JsonObject { ["details"] = "conditions, high and low, feels like" } })
            {
                var details = Details(Weather(settings));
                Assert.Equal(["Cloudy", "H 16° · L 10°  ·  Feels 9°"], details.Select(t => t.Text));
                // The conditions larger and in the text colour, the rest smaller in the detail colour.
                Assert.Equal([80 * 0.3, 80 * 0.22], details.Select(t => t.FontSize));
                var theme = Theme.From(new ThemeSettings());
                Assert.Equal(new[] { theme.Foreground, theme.Secondary }.Select(Colour), details.Select(t => Colour(t.Foreground)));
            }

            // Without today's range, feels like alone, as before.
            Assert.Equal(["Cloudy", "Feels 9°"], Details(Weather([], Store(Report() with { Days = [] }))).Select(t => t.Text));
            Assert.Equal(DetailName.RainChance, WeatherWidget.Descriptor.Find("forecastDetails")!.Default);
        });
    }

    [Theory]
    [InlineData(DetailName.Conditions, "Cloudy", "Cloudy")]
    [InlineData(DetailName.HighAndLow, "H 16° · L 10°", "H 60° · L 50°")]
    [InlineData(DetailName.FeelsLike, "Feels 9°", "Feels 48°")]
    [InlineData(DetailName.Wind, "12 km/h SW", "7 mph SW")]
    [InlineData(DetailName.Gusts, "Gusts 40 km/h", "Gusts 25 mph")]
    [InlineData(DetailName.Humidity, "Humidity 72%", "Humidity 72%")]
    [InlineData(DetailName.Pressure, "1012 hPa ↓", "29.90 inHg ↓")]
    [InlineData(DetailName.Uv, "UV 3 Moderate", "UV 3 Moderate")]
    [InlineData(DetailName.RainChance, "Rain 0%", "Rain 0%")]
    [InlineData(DetailName.Rainfall, "Rainfall 0.0 mm", "Rainfall 0.00 in")]
    [InlineData(DetailName.Sunrise, "Sunrise 07:12", "Sunrise 07:12")]
    [InlineData(DetailName.Sunset, "Sunset 18:36", "Sunset 18:36")]
    [InlineData(DetailName.Daylight, "Daylight 10 h 49 min", "Daylight 10 h 49 min")]
    [InlineData(DetailName.DewPoint, "Dew point 6°", "Dew point 43°")]
    [InlineData(DetailName.Visibility, "Visibility 24.1 km", "Visibility 15.0 mi")]
    [InlineData(DetailName.Clouds, "Cloud 75%", "Cloud 75%")]
    [InlineData(DetailName.AirQuality, "Air quality Fair", "Air quality Fair")]
    [InlineData(DetailName.Pollen, "Pollen Grass high", "Pollen Grass high")]
    public void Each_detail_reads_in_the_forecasts_units(string detail, string metric, string imperial)
    {
        InBritain(() =>
        {
            var live = new LiveDetails("Fair", "Grass high", PressureTrend.Falling);
            Assert.Equal(metric, DetailText(detail, Report(), live));
            Assert.Equal(imperial, DetailText(detail, Report(WeatherUnits.Imperial), live));

            // And the widget shows it, with the pressure's trend worked out from the hours.
            var store = Store(airQuality: true);
            Assert.Contains(metric, Details(Weather(new JsonObject { ["details"] = detail }, store)).Select(t => t.Text));
            Assert.Contains(imperial, Details(Weather(new JsonObject { ["details"] = detail }, Store(Report(WeatherUnits.Imperial), airQuality: true))).Select(t => t.Text));
        });
    }

    [Fact]
    public void Every_detail_offered_has_a_text()
    {
        InBritain(() =>
        {
            var live = new LiveDetails("Fair", "Grass high", PressureTrend.Steady);
            Assert.All(DetailNames, name => Assert.NotNull(DetailText(name, Report(), live)));
            Assert.Equal(18, DetailNames.Count);
        });
    }

    [Fact]
    public void Readings_the_forecast_has_not_got_are_left_out()
    {
        InBritain(() =>
        {
            var bare = Report() with { WindSpeed = double.NaN, Humidity = double.NaN, Sunrise = null, Sunset = null, Days = [] };
            bare = bare with { Gusts = null, Uv = null, Pressure = null, DewPoint = null, Visibility = null, Clouds = null };
            Assert.All(DetailNames.Except([DetailName.Conditions, DetailName.FeelsLike]), name => Assert.Null(DetailText(name, bare, default)));
            // A wind without a direction is just its speed.
            Assert.Equal("12 km/h", DetailText(DetailName.Wind, Report() with { WindDirection = double.NaN }, default));
            Assert.Equal("1012 hPa", DetailText(DetailName.Pressure, Report(), default));
            Assert.Equal("1012 hPa ↑", DetailText(DetailName.Pressure, Report(), new LiveDetails(null, null, PressureTrend.Rising)));
            Assert.Equal("1012 hPa →", DetailText(DetailName.Pressure, Report(), new LiveDetails(null, null, PressureTrend.Steady)));
        });
    }

    [Fact]
    public void Details_go_in_the_order_chosen()
    {
        InBritain(() =>
        {
            // The conditions only have the larger line when they're first.
            var later = Details(Weather(new JsonObject { ["details"] = "wind, Feels like, conditions" }));
            Assert.Equal(["12 km/h SW  ·  Feels 9°  ·  Cloudy"], later.Select(t => t.Text));
            Assert.Equal(80 * 0.22, later.Single().FontSize);

            var first = Details(Weather(new JsonObject { ["details"] = "conditions, uv, wind, sunset" }));
            Assert.Equal(["Cloudy", "UV 3 Moderate  ·  12 km/h SW", "Sunset 18:36"], first.Select(t => t.Text));

            // A name that isn't one is left out, and none at all leaves the temperature on its own.
            Assert.Equal(["Feels 9°"], Details(Weather(new JsonObject { ["details"] = "sunshine, feels like" })).Select(t => t.Text));
            var none = Weather(new JsonObject { ["details"] = "" });
            Assert.Empty(Details(none));
            Assert.Equal(Visibility.Collapsed, DetailsPanel(none)!.Visibility);
        });
    }

    [Fact]
    public void A_layout_naming_a_detail_there_is_no_such_thing_as_is_told_so()
    {
        var widget = new JsonObject
        {
            ["type"] = "weather", ["x"] = 0, ["y"] = 0, ["width"] = 900, ["height"] = 180, ["details"] = "conditions, Wind, sunshine", ["forecastDetails"] = "uv, gusts",
        };
        var issue = Assert.Single(LayoutValidator.Validate(new LayoutDocument { Widgets = [widget] }));
        Assert.Contains("'details' has 'sunshine'", issue);
    }

    [Fact]
    public void Details_share_lines_a_few_at_a_time()
    {
        Assert.Equal(
            ["H 16° · L 10°  ·  Feels 9°", "12 km/h SW  ·  Gusts 40 km/h", "Visibility 24.1 km", "Air quality Unhealthy for sensitive groups", "Rain 0%"],
            DetailLines(["H 16° · L 10°", "Feels 9°", "12 km/h SW", "Gusts 40 km/h", "Visibility 24.1 km", "Air quality Unhealthy for sensitive groups", "Rain 0%"]));
        // Today's high and low and feels like share a line however cold or hot it gets.
        Assert.Single(DetailLines(["H -12° · L -20°", "Feels -28°"]));
        Assert.Single(DetailLines(["H 104° · L 88°", "Feels 110°"]));
        Assert.Empty(DetailLines([]));
    }

    [Fact]
    public void Turning_details_off_leaves_them_all_out()
    {
        InBritain(() =>
        {
            var everything = string.Join(", ", DetailNames);
            var off = Weather(new JsonObject { ["showDetails"] = false, ["details"] = everything }, Store(airQuality: true));
            Assert.Empty(Details(off));
            Assert.DoesNotContain(Texts(off), t => t.Contains("Feels") || t.Contains("Cloudy") || t.Contains("Air quality"));

            // The place's name still shows with Location.
            var named = Weather(new JsonObject { ["showDetails"] = false, ["showLocation"] = true }, WithLocation(Store()));
            Assert.Equal(["Leeds"], Details(named).Select(t => t.Text));
        });
    }

    [Fact]
    public void Air_quality_and_pollen_show_only_while_they_are_on()
    {
        InBritain(() =>
        {
            var settings = new JsonObject { ["details"] = "conditions, air quality, pollen" };
            // Off: their readings are there to pick, but have nothing in them.
            Assert.Equal(["Cloudy"], Details(Weather(settings.DeepClone().AsObject())).Select(t => t.Text));
            Assert.Equal(["Cloudy"], Details(Weather(settings.DeepClone().AsObject(), WithoutAirQuality())).Select(t => t.Text));

            var store = Store(airQuality: true);
            var widget = Weather(settings.DeepClone().AsObject(), store);
            Assert.Equal(["Cloudy", "Air quality Fair  ·  Pollen Grass high"], Details(widget).Select(t => t.Text));

            // They change on the hour, between forecasts, and the widget keeps up.
            store.SetText(AirQualityProvider.Level, "Moderate");
            store.SetText(AirQualityProvider.Pollen, null);
            widget.Refresh(store, Morning.AddSeconds(1));
            Assert.Equal(["Cloudy", "Air quality Moderate"], Details(widget).Select(t => t.Text));

            // Turned off.
            store.SetText(AirQualityProvider.Level, null);
            widget.Refresh(store, Morning.AddSeconds(2));
            Assert.Equal(["Cloudy"], Details(widget).Select(t => t.Text));
        });
    }

    [Fact]
    public void Nothing_is_built_again_until_something_shown_changes()
    {
        InBritain(() =>
        {
            var store = Store(airQuality: true);
            var widget = Weather(new JsonObject { ["details"] = "conditions, pressure, air quality" }, store);
            var line = Details(widget)[1];
            Assert.Equal("1012 hPa ↓  ·  Air quality Fair", line.Text);
            widget.Refresh(store, Morning.AddSeconds(30));
            Assert.Same(line, Details(widget)[1]);
        });
    }

    [Fact]
    public void Another_places_details_are_its_own_without_air_quality()
    {
        InBritain(() =>
        {
            var leeds = Report();
            var paris = Report() with { FeelsLike = 17.2, Code = 0, WindDirection = 90, Gusts = 12 };
            var store = Store(leeds, airQuality: true);
            store.SetText(WeatherProvider.Location, "Leeds, England, United Kingdom");
            store.SetAttachment(WeatherProvider.Places, (IReadOnlyList<PlaceForecast>)[new("Leeds, England, United Kingdom", leeds), new("Paris, Île-de-France, France", paris)]);

            var settings = new JsonObject { ["details"] = "conditions, feels like, wind, gusts, air quality, pollen" };
            settings["places"] = "Paris";
            Assert.Equal(["Clear", "Feels 17°  ·  12 km/h E", "Gusts 12 km/h", "Paris"], Details(Weather(settings, store)).Select(t => t.Text));

            settings = new JsonObject { ["details"] = "conditions, feels like, wind, gusts, air quality, pollen", ["places"] = "Leeds" };
            Assert.Equal(["Cloudy", "Feels 9°  ·  12 km/h SW", "Gusts 40 km/h  ·  Air quality Fair", "Pollen Grass high"], Details(Weather(settings, store)).Select(t => t.Text));
        });
    }

    // ---- Under each day or hour ---------------------------------------------------------------------

    [Fact]
    public void Each_day_or_hour_shows_the_forecast_details_chosen_in_order()
    {
        InBritain(() =>
        {
            List<string> Under(JsonObject settings, WeatherUnits units = WeatherUnits.Metric) =>
                [.. Descendants<UniformGrid>(Weather(settings, Store(Report(units)))).Single().Children.Cast<StackPanel>()
                    .SelectMany(column => column.Children.OfType<TextBlock>().Skip(2)).Select(t => t.Text)];

            // As before: the chance of rain from 30%.
            Assert.Equal(["45%"], Under(new JsonObject { ["count"] = 2 }));

            var all = new JsonObject { ["count"] = 2, ["forecastDetails"] = "chance of rain, rainfall, wind, gusts, uv", ["rainFrom"] = 0 };
            // A dry day has no rainfall.
            Assert.Equal(["0%", "20 km/h", "Gusts 41", "UV 3", "45%", "6.2 mm", "30 km/h", "Gusts 55", "UV 7"], Under(all));
            Assert.Equal(["0%", "12 mph", "Gusts 26", "UV 3", "45%", "0.24 in", "19 mph", "Gusts 34", "UV 7"], Under(all.DeepClone().AsObject(), WeatherUnits.Imperial));

            Assert.Equal(["Gusts 41", "20 km/h", "Gusts 55", "30 km/h"], Under(new JsonObject { ["count"] = 2, ["forecastDetails"] = "gusts, wind" }));
            Assert.Empty(Under(new JsonObject { ["count"] = 2, ["forecastDetails"] = "" }));

            // The hours have their own.
            Assert.Equal(["40%", "0.6 mm", "UV 2", "0%", "UV 3"],
                Under(new JsonObject { ["forecast"] = "hours", ["forecastDetails"] = "chance of rain, rainfall, UV", ["rainFrom"] = 0 }));
        });
    }

    [Fact]
    public void A_busy_widget_still_fits()
    {
        InBritain(() =>
        {
            var settings = new JsonObject
            {
                ["details"] = string.Join(", ", DetailNames), ["forecastDetails"] = string.Join(", ", ForecastDetailNames), ["rainFrom"] = 0, ["count"] = 2,
            };
            var widget = Weather(settings, Store(airQuality: true));
            widget.Width = 900;
            widget.Height = 180;
            widget.Measure(new Size(900, 180));
            widget.Arrange(new Rect(0, 0, 900, 180));
            widget.UpdateLayout();

            // Smaller as a whole, all of it inside the widget.
            Assert.True(Descendants<ShrinkToFit>(widget).First().Scale < 1);
            var ink = ShrinkToFit.Ink(widget);
            Assert.True(new Rect(-2, -1, 904, 182).Contains(ink), $"{ink} is outside 900×180");

            // Every detail is there, a few to a line.
            var lines = Details(widget).Select(t => t.Text).ToList();
            Assert.InRange(lines.Count, 6, 12);
            var live = new LiveDetails("Fair", "Grass high", PressureTrend.Falling);
            Assert.All(DetailNames, name => Assert.Contains(lines, line => line.Contains(DetailText(name, Report(), live)!, StringComparison.Ordinal)));
            Assert.All(lines.Where(line => line.Contains("  ·  ", StringComparison.Ordinal)), line => Assert.True(line.Length <= 40, line));
        });
    }

    private static Color Colour(Brush brush) => ((SolidColorBrush)brush).Color;

    /// <summary>The text beside the temperature, top to bottom; none while it's collapsed, or without anything to show there.</summary>
    private static List<TextBlock> Details(Widget widget) =>
        DetailsPanel(widget) is { Visibility: Visibility.Visible } panel ? [.. panel.Children.OfType<TextBlock>()] : [];

    /// <summary>The stack beside the temperature, in the row of the weather now, if it has one.</summary>
    private static StackPanel? DetailsPanel(Widget widget) =>
        Descendants<StackPanel>(widget).FirstOrDefault(p => p.Orientation == Orientation.Vertical && VisualTreeHelper.GetParent(p) is StackPanel { Orientation: Orientation.Horizontal });

    private static MetricStore WithLocation(MetricStore store)
    {
        store.SetText(WeatherProvider.Location, "Leeds, England, United Kingdom");
        return store;
    }

    /// <summary>A store without the air quality's readings at all, as before it was there.</summary>
    private static MetricStore WithoutAirQuality()
    {
        var store = new MetricStore();
        new WeatherProvider(() => null).Initialize(store);
        store.SetAttachment(WeatherProvider.Forecast, Report());
        return store;
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
