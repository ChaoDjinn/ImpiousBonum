using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ImpiousBonum.App.Controls;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Settings;
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

    [Fact]
    public void The_weather_page_credits_cams_only_while_air_quality_is_on()
    {
        Sta.Run(() =>
        {
            var settings = new AppSettings { WeatherLocation = new WeatherLocation("Leeds, England, United Kingdom", 53.8, -1.55) };
            var host = DispatchProxy.Create<ISettingsHost, SettingsOnlyHost>();
            ((SettingsOnlyHost)(object)host).Settings = settings;
            var page = new WeatherPage(host);

            page.Load();
            Assert.DoesNotContain(LogicalTexts(page), text => text.Contains("CAMS", StringComparison.Ordinal));

            settings.AirQuality = true;
            page.Load();
            Assert.Contains(OpenMeteo.AirQualityAttribution + ".", LogicalTexts(page));
        });
    }

    private static readonly WeatherWarning Yellow = new("rain", "Yellow warning of rain affecting Wales", WarningLevel.Minor, "Yellow",
        null, null, "https://www.metoffice.gov.uk/weather/warnings-and-advice/uk-warnings#?id=rain", WarningSource.MetOffice);

    private static readonly WeatherWarning Red = Yellow with { Id = "wind", Headline = "Red warning of wind affecting Wales", Level = WarningLevel.Extreme, LevelText = "Red", Link = "https://example.com/wind" };

    [Fact]
    public void A_forecast_only_widget_leaves_the_warning_to_the_one_with_the_weather_now()
    {
        Sta.Run(() =>
        {
            var store = new MetricStore();
            store.SetAttachment(WeatherProvider.Forecast, Report());
            store.SetAttachment(WeatherWarningsProvider.Warnings, (IReadOnlyList<WeatherWarning>)[Yellow]);
            var theme = Theme.From(new ThemeSettings());

            var now = WidgetFactory.Create(new JsonObject { ["type"] = "weather", ["forecast"] = "none" }, theme);
            now.Refresh(store, DateTime.Now);
            Assert.Equal(Visibility.Visible, Descendants<TapTarget>(now).Single().Visibility);

            var forecast = WidgetFactory.Create(new JsonObject { ["type"] = "weather", ["showCurrent"] = false }, theme);
            forecast.Refresh(store, DateTime.Now);
            Assert.Empty(Descendants<TapTarget>(forecast));
        });
    }

    [Fact]
    public void Warnings_show_along_the_bottom_and_take_no_room_without_any()
    {
        Sta.Run(() =>
        {
            var theme = Theme.From(new ThemeSettings());
            var store = new MetricStore();
            store.SetAttachment(WeatherProvider.Forecast, Report());
            Size Measured(JsonObject settings)
            {
                var measured = WidgetFactory.Create(settings, theme);
                measured.Refresh(store, DateTime.Now);
                measured.Measure(new Size(900, 400));
                return measured.DesiredSize;
            }

            // No warnings: the same size as a widget without the strip at all.
            var without = Measured(new JsonObject { ["type"] = "weather", ["showWarnings"] = false });
            Assert.Equal(without, Measured(new JsonObject { ["type"] = "weather" }));

            var widget = WidgetFactory.Create(new JsonObject { ["type"] = "weather" }, theme);
            widget.Refresh(store, DateTime.Now);
            var strip = Descendants<TapTarget>(widget).Single();
            Assert.Equal(Visibility.Collapsed, strip.Visibility);

            store.SetAttachment(WeatherWarningsProvider.Warnings, (IReadOnlyList<WeatherWarning>)[Yellow]);
            widget.Refresh(store, DateTime.Now);
            Assert.Equal(Visibility.Visible, strip.Visibility);
            var headline = Descendants<TextBlock>(strip).Single();
            Assert.Equal("Yellow warning of rain affecting Wales", headline.Text);
            // Yellow is a warning, in the theme's warning colour, and its sign too.
            Assert.Same(theme.Warning, headline.Foreground);
            Assert.Same(theme.Warning, Descendants<System.Windows.Shapes.Path>(strip).Single().Stroke);
            Assert.True(Measured(new JsonObject { ["type"] = "weather" }).Height > without.Height);

            // Gone again: nothing shows.
            store.SetAttachment(WeatherWarningsProvider.Warnings, null);
            widget.Refresh(store, DateTime.Now);
            Assert.Equal(Visibility.Collapsed, strip.Visibility);
        });
    }

    [Fact]
    public void A_warning_keeps_its_room_when_the_weather_fills_the_widget()
    {
        Sta.Run(() =>
        {
            var store = new MetricStore();
            store.SetAttachment(WeatherProvider.Forecast, Report());
            store.SetAttachment(WeatherWarningsProvider.Warnings, (IReadOnlyList<WeatherWarning>)[Yellow]);
            (Rect Strip, Rect Hours) Arranged(double height, bool shrinkToFit = false)
            {
                // The weather now beside the hours ahead, as in the README's picture: they fill a widget 120 high.
                var widget = WidgetFactory.Create(new JsonObject
                {
                    ["type"] = "weather", ["fontSize"] = 40, ["forecast"] = "hours", ["forecastSize"] = 22, ["rainFrom"] = 0,
                    [Setting.ShrinkToFitKey] = shrinkToFit,
                }, Theme.From(new ThemeSettings()));
                widget.Refresh(store, DateTime.Now);
                widget.Measure(new Size(760, height));
                widget.Arrange(new Rect(0, 0, 760, height));
                var strip = Descendants<TapTarget>(widget).Single();
                var hours = Descendants<UniformGrid>(widget).Single();
                return (strip.TransformToAncestor(widget).TransformBounds(new Rect(strip.RenderSize)),
                    hours.TransformToAncestor(widget).TransformBounds(new Rect(hours.RenderSize)));
            }

            // Too short for both: the hours give way, and the warning is all there at the bottom.
            var (strip, hours) = Arranged(120);
            Assert.True(hours.Height > 100, $"{hours}");
            Assert.True(strip.Top > 0 && Math.Abs(strip.Bottom - 120) < 0.5, $"{strip}");

            // With shrink to fit, the weather gets smaller to fit above the warning instead of being cut off.
            (strip, hours) = Arranged(120, shrinkToFit: true);
            Assert.True(hours.Bottom <= strip.Top + 0.5 && hours.Height < 100, $"{hours} {strip}");
            Assert.True(Math.Abs(strip.Bottom - 120) < 0.5, $"{strip}");

            // With room to spare, the warning is just under the hours, and the two are centred together.
            (strip, hours) = Arranged(300);
            Assert.True(strip.Top > hours.Bottom && strip.Top - hours.Bottom < 10, $"{hours} {strip}");
            Assert.True(Math.Abs(hours.Top - (300 - strip.Bottom)) < 0.5, $"{hours} {strip}");
        });
    }

    [Fact]
    public void A_warning_is_the_size_of_the_forecast_or_without_one_of_the_details()
    {
        Sta.Run(() =>
        {
            var store = new MetricStore();
            store.SetAttachment(WeatherWarningsProvider.Warnings, (IReadOnlyList<WeatherWarning>)[Yellow]);
            double TextSize(JsonObject settings)
            {
                var widget = WidgetFactory.Create(settings, Theme.From(new ThemeSettings()));
                widget.Refresh(store, DateTime.Now);
                return Descendants<TextBlock>(Descendants<TapTarget>(widget).Single()).Single().FontSize;
            }

            Assert.Equal(27, TextSize(new JsonObject { ["type"] = "weather", ["forecastSize"] = 30, ["fontSize"] = 100 }), 6);
            // Forecast size does nothing else on a widget without a forecast, so there it follows the temperature's.
            Assert.Equal(22, TextSize(new JsonObject { ["type"] = "weather", ["forecast"] = "none", ["forecastSize"] = 30, ["fontSize"] = 100 }), 6);
        });
    }

    [Fact]
    public void The_worst_warning_shows_with_how_many_more_and_a_tap_opens_it()
    {
        Sta.Run(() =>
        {
            var theme = Theme.From(new ThemeSettings());
            var widget = WidgetFactory.Create(new JsonObject { ["type"] = "weather" }, theme);
            var opened = new List<WidgetAction>();
            widget.AddHandler(Widget.ActionEvent, new EventHandler<WidgetActionEventArgs>((_, e) => opened.Add(e.Action)));
            var store = new MetricStore();
            store.SetAttachment(WeatherWarningsProvider.Warnings, (IReadOnlyList<WeatherWarning>)[Red, Yellow, Yellow with { Id = "more" }]);
            widget.Refresh(store, DateTime.Now);

            // Shown without a forecast too: the warning doesn't wait for one.
            var strip = Descendants<TapTarget>(widget).Single();
            var texts = Descendants<TextBlock>(strip).ToList();
            Assert.Equal(["and 2 more", "Red warning of wind affecting Wales"], texts.Select(t => t.Text));
            Assert.Same(theme.Critical, texts[1].Foreground);

            strip.Tap();
            Assert.Equal([new WidgetAction(WidgetAction.Open, "https://example.com/wind")], opened);

            // Left out, nothing shows however many there are.
            var plain = WidgetFactory.Create(new JsonObject { ["type"] = "weather", ["showWarnings"] = false }, theme);
            plain.Refresh(store, DateTime.Now);
            Assert.Empty(Descendants<TapTarget>(plain));
            Assert.DoesNotContain(Texts(plain), t => t.Contains("warning", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void Warnings_are_off_until_turned_on_and_only_for_the_uk_and_the_us()
    {
        var settings = new AppSettings();
        Assert.False(settings.WeatherWarnings);
        Assert.True(settings.WeatherWarningsNotify);
        Assert.Equal(AppSettings.MetOfficeAreaAuto, settings.MetOfficeArea);
        Assert.Null(settings.WeatherWarningsPlace());
        Assert.Null(settings.WeatherWarningsRequest());

        // A UK place gets its Met Office area, but nothing's fetched until they're on.
        settings.WeatherLocation = new WeatherLocation("Liverpool, England, United Kingdom", 53.41, -2.98) { CountryCode = "GB" };
        Assert.Equal(new WarningsRequest(WarningSource.MetOffice, 53.41, -2.98, "nw"), settings.WeatherWarningsPlace());
        Assert.Null(settings.WeatherWarningsRequest());
        settings.WeatherWarnings = true;
        Assert.Equal(new WarningsRequest(WarningSource.MetOffice, 53.41, -2.98, "nw"), settings.WeatherWarningsRequest());

        settings.WeatherLocation = new WeatherLocation("Springfield, Missouri, United States", 37.22, -93.29) { CountryCode = "US" };
        Assert.Equal(new WarningsRequest(WarningSource.NationalWeatherService, 37.22, -93.29), settings.WeatherWarningsRequest());

        // Anywhere else, there's nothing to fetch from anyone.
        settings.WeatherLocation = new WeatherLocation("Paris, Île-de-France, France", 48.85, 2.35) { CountryCode = "FR" };
        Assert.Equal(WarningSource.None, settings.WeatherWarningsPlace()?.Source);
        Assert.Null(settings.WeatherWarningsRequest());
        settings.WeatherLocation = new WeatherLocation("Dublin, Leinster, Ireland", 53.35, -6.26);
        Assert.Null(settings.WeatherWarningsRequest());
        // Coordinates typed in for New York don't say it's the US, so they never go to the NWS.
        settings.WeatherLocation = new WeatherLocation("40.71, -74.01", 40.71, -74.01);
        Assert.Null(settings.WeatherWarningsRequest());
        // Typed in for London, they're plainly in Great Britain.
        settings.WeatherLocation = new WeatherLocation("51.5, -0.12", 51.5, -0.12);
        Assert.Equal(new WarningsRequest(WarningSource.MetOffice, 51.5, -0.12, "se"), settings.WeatherWarningsRequest());
    }

    [Fact]
    public void The_country_is_saved_and_places_saved_without_one_still_get_warnings()
    {
        var settings = new AppSettings
        {
            WeatherLocation = new WeatherLocation("Leeds, England, United Kingdom", 53.8, -1.55) { CountryCode = "GB" }, WeatherWarnings = true, MetOfficeArea = "ne",
        };
        var json = JsonSerializer.Serialize(settings, JsonDefaults.Options);
        var restored = JsonSerializer.Deserialize<AppSettings>(json, JsonDefaults.Options)!;

        Assert.Contains("\"countryCode\": \"GB\"", json);
        Assert.Contains("\"weatherWarnings\": true", json);
        Assert.Equal(settings.WeatherLocation, restored.WeatherLocation);
        // The area chosen rather than Leeds's own; one that isn't an area is automatic.
        Assert.Equal("ne", restored.WeatherWarningsRequest()?.Area);
        restored.MetOfficeArea = "xx";
        Assert.Equal("yh", restored.WeatherWarningsRequest()?.Area);

        // Saved before the country was kept: the name says.
        var old = JsonSerializer.Deserialize<AppSettings>(
            """{ "weatherWarnings": true, "weatherLocation": { "name": "Leeds, England, United Kingdom", "latitude": 53.8, "longitude": -1.55 } }""", JsonDefaults.Options)!;
        Assert.Null(old.WeatherLocation?.CountryCode);
        Assert.Equal(new WarningsRequest(WarningSource.MetOffice, 53.8, -1.55, "yh"), old.WeatherWarningsRequest());

        // Saved before warnings were there: off, with notifications ready for when they're on.
        var older = JsonSerializer.Deserialize<AppSettings>("""{ "weatherLocation": { "name": "Leeds", "latitude": 53.8, "longitude": -1.55 } }""", JsonDefaults.Options)!;
        Assert.False(older.WeatherWarnings);
        Assert.True(older.WeatherWarningsNotify);
        Assert.Null(older.WeatherWarningsRequest());
    }

    [Fact]
    public void The_weather_page_offers_warnings_and_credits_whoever_gives_them()
    {
        Sta.Run(() =>
        {
            var settings = new AppSettings { WeatherLocation = new WeatherLocation("Liverpool, England, United Kingdom", 53.41, -2.98) { CountryCode = "GB" } };
            var host = DispatchProxy.Create<ISettingsHost, SettingsOnlyHost>();
            ((SettingsOnlyHost)(object)host).Settings = settings;
            var page = new WeatherPage(host);

            // Off: only the switch, and no credit.
            page.Load();
            Assert.Contains("Severe weather warnings", Headers(page));
            Assert.DoesNotContain("Warnings now", Headers(page));
            Assert.DoesNotContain(LogicalTexts(page), text => text.Contains("Met Office", StringComparison.Ordinal));

            settings.WeatherWarnings = true;
            page.Load();
            Assert.Contains("Notify me", Headers(page));
            var area = Logical<ComboBox>(page).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Met Office area");
            var areas = Assert.IsType<List<string>>(area.ItemsSource);
            Assert.Equal(17, areas.Count);
            Assert.Equal("Automatic (North West England)", areas[0]);
            Assert.Equal(0, area.SelectedIndex);
            Assert.Contains(MetOfficeWarnings.Attribution + ".", LogicalTexts(page));

            // A US place: the NWS's credit, and no Met Office area.
            settings.WeatherLocation = new WeatherLocation("Springfield, Missouri, United States", 37.22, -93.29) { CountryCode = "US" };
            page.Load();
            Assert.DoesNotContain("Met Office area", Headers(page));
            Assert.Contains(NationalWeatherService.Attribution + ".", LogicalTexts(page));

            // Anywhere else: why there are none, but nothing to notify about or credit.
            settings.WeatherLocation = new WeatherLocation("Paris, Île-de-France, France", 48.85, 2.35) { CountryCode = "FR" };
            page.Load();
            Assert.Contains("Warnings now", Headers(page));
            Assert.DoesNotContain("Notify me", Headers(page));
            Assert.DoesNotContain(LogicalTexts(page), text => text.Contains("Weather Service", StringComparison.Ordinal));
        });
    }

    private static List<string?> Headers(DependencyObject root) => [.. Logical<SettingsCard>(root).Select(c => c.Header)];

    private static IEnumerable<T> Logical<T>(DependencyObject root) where T : DependencyObject =>
        LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().SelectMany(child => child is T match ? [match, .. Logical<T>(child)] : Logical<T>(child));

    /// <summary>A settings host with just the settings: everything else answers with nothing.</summary>
    public class SettingsOnlyHost : DispatchProxy
    {
        public AppSettings Settings { get; set; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == "get_Settings" ? Settings
            : targetMethod?.ReturnType is { IsValueType: true } type && type != typeof(void) ? Activator.CreateInstance(type)
            : null;
    }

    /// <summary>Text in a page that hasn't been shown, so has no visual tree yet.</summary>
    private static List<string> LogicalTexts(DependencyObject root) =>
        [.. LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().SelectMany(child => child is TextBlock text ? [text.Text] : LogicalTexts(child))];

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
