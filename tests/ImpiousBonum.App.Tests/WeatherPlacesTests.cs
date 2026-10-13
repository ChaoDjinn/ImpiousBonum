using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
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

/// <summary>More places besides the location: saving them, the weather widget moving between them, and the Weather page's list.</summary>
public sealed class WeatherPlacesTests
{
    private static readonly WeatherLocation Leeds = new("Leeds, England, United Kingdom", 53.8, -1.55) { CountryCode = "GB" };
    private static readonly WeatherLocation Paris = new("Paris, Île-de-France, France", 48.85, 2.35) { CountryCode = "FR" };
    private static readonly WeatherLocation Sydney = new("Sydney, New South Wales, Australia", -33.87, 151.21) { CountryCode = "AU" };

    private static readonly DateTime Start = new(2026, 10, 13, 9, 0, 0);

    /// <summary>11° and cloudy, with a couple of days and hours.</summary>
    private static WeatherReport Report(double temperature = 10.9) => new(
        WeatherUnits.Metric, new DateTime(2026, 10, 11, 9, 45, 0), temperature, 8.8, 72, 7.6, 283, 3, true,
        [new WeatherPeriod(new DateTime(2026, 10, 11, 10, 0, 0), 11.1, null, 61, true, 40)],
        [new WeatherPeriod(new DateTime(2026, 10, 11), 15.5, 9.5, 3, true, 0), new WeatherPeriod(new DateTime(2026, 10, 12), 17.3, 13.3, 51, true, 45)],
        null, null);

    private static readonly WeatherReport LeedsWeather = Report();
    private static readonly WeatherReport ParisWeather = Report(18.2);

    private static readonly WeatherWarning Yellow = new("rain", "Yellow warning of rain affecting Yorkshire", WarningLevel.Minor, "Yellow",
        null, null, "https://www.metoffice.gov.uk/weather/warnings-and-advice/uk-warnings#?id=rain", WarningSource.MetOffice);

    /// <summary>Readings as the weather provider leaves them: Leeds's forecast and name, then every place's, Sydney's not fetched yet.</summary>
    private static MetricStore Store(bool morePlaces = true)
    {
        var store = new MetricStore();
        new WeatherProvider(() => null).Initialize(store);
        store.SetAttachment(WeatherProvider.Forecast, LeedsWeather);
        store.SetText(WeatherProvider.Location, Leeds.Name);
        if (morePlaces)
            store.SetAttachment(WeatherProvider.Places, (IReadOnlyList<PlaceForecast>)[new(Leeds.Name, LeedsWeather), new(Paris.Name, ParisWeather), new(Sydney.Name, null)]);
        return store;
    }

    private static Widget Weather(JsonObject settings, Theme? theme = null)
    {
        settings["type"] = "weather";
        return WidgetFactory.Create(settings, theme ?? Theme.From(new ThemeSettings()));
    }

    // ---- Settings -----------------------------------------------------------------------------------

    [Fact]
    public void More_places_are_saved_and_older_settings_have_none()
    {
        var settings = new AppSettings { WeatherLocation = Leeds, WeatherPlaces = [Paris, Sydney], WeatherUnits = AppSettings.WeatherUnitsImperial };
        var json = JsonSerializer.Serialize(settings, JsonDefaults.Options);
        var restored = JsonSerializer.Deserialize<AppSettings>(json, JsonDefaults.Options)!;

        Assert.Contains("\"weatherPlaces\": [", json);
        Assert.Equal([Paris, Sydney], restored.WeatherPlaces);
        Assert.Equal("FR", restored.WeatherPlaces[0].CountryCode);
        // In their order, in the location's units.
        Assert.Equal(
            [new WeatherRequest(Paris.Name, 48.85, 2.35, WeatherUnits.Imperial), new WeatherRequest(Sydney.Name, -33.87, 151.21, WeatherUnits.Imperial)],
            restored.WeatherPlaceRequests());
        // The location is just as it was.
        Assert.Equal(new WeatherRequest(Leeds.Name, 53.8, -1.55, WeatherUnits.Imperial), restored.WeatherRequest());

        // Settings from before have none, and so nothing more to fetch.
        var old = JsonSerializer.Deserialize<AppSettings>("""{ "weatherLocation": { "name": "Leeds", "latitude": 53.8, "longitude": -1.55 } }""", JsonDefaults.Options)!;
        Assert.Empty(old.WeatherPlaces);
        Assert.Empty(old.WeatherPlaceRequests());
        Assert.Empty(JsonSerializer.Deserialize<AppSettings>("""{ "weatherPlaces": null }""", JsonDefaults.Options)!.WeatherPlaces);

        // Without a location the weather is off, places or not.
        Assert.Empty(new AppSettings { WeatherPlaces = [Paris] }.WeatherPlaceRequests());
    }

    [Fact]
    public void Making_a_place_the_main_one_swaps_it_with_the_location()
    {
        var settings = new AppSettings { WeatherLocation = Leeds, WeatherPlaces = [Paris, Sydney], MetOfficeArea = "yh" };
        settings.MakeMainWeatherPlace(Sydney);

        Assert.Equal(Sydney, settings.WeatherLocation);
        Assert.Equal([Paris, Leeds], settings.WeatherPlaces);
        // As with any new location, the area chosen for the old one goes.
        Assert.Equal(AppSettings.MetOfficeAreaAuto, settings.MetOfficeArea);

        // One that isn't among them changes nothing.
        settings.MakeMainWeatherPlace(Leeds with { Name = "Elsewhere" });
        Assert.Equal(Sydney, settings.WeatherLocation);
        Assert.Equal([Paris, Leeds], settings.WeatherPlaces);
    }

    [Fact]
    public void A_place_is_the_same_place_by_where_it_is()
    {
        var settings = new AppSettings { WeatherLocation = Leeds, WeatherPlaces = [Paris] };
        Assert.True(settings.HasWeatherPlace(Leeds));
        Assert.True(settings.HasWeatherPlace(new WeatherLocation("48.85, 2.35", 48.850001, 2.349999)));
        Assert.False(settings.HasWeatherPlace(Sydney));
    }

    [Fact]
    public void A_new_location_that_was_one_of_the_places_is_not_kept_twice()
    {
        var settings = new AppSettings { WeatherLocation = Leeds, WeatherPlaces = [Paris, Sydney], MetOfficeArea = "yh" };
        // Arriving in Paris, and setting the location there by searching for it again.
        settings.SetWeatherLocation(Paris with { Name = "Paris, France" });

        Assert.Equal("Paris, France", settings.WeatherLocation!.Name);
        Assert.Equal([Sydney], settings.WeatherPlaces);
        Assert.Equal([new WeatherRequest(Sydney.Name, -33.87, 151.21, settings.WeatherRequest()!.Units)], settings.WeatherPlaceRequests());
        Assert.Equal(AppSettings.MetOfficeAreaAuto, settings.MetOfficeArea);

        // Anywhere else leaves the places be.
        settings.SetWeatherLocation(Leeds);
        Assert.Equal(Leeds, settings.WeatherLocation);
        Assert.Equal([Sydney], settings.WeatherPlaces);
    }

    // ---- Names --------------------------------------------------------------------------------------

    [Fact]
    public void Places_go_by_their_short_names_and_places_with_the_same_one_are_told_apart()
    {
        Assert.Equal(["Leeds", "Sydney"], WeatherWidget.ShortNames([Leeds.Name, Sydney.Name]));
        Assert.Equal(["Leeds", "Paris (Île-de-France)", "Paris (Texas)"],
            WeatherWidget.ShortNames([Leeds.Name, Paris.Name, "Paris, Texas, United States"]));
        // The same whatever their case, as picking goes.
        Assert.Equal(["Paris (Île-de-France)", "paris (Ontario)"], WeatherWidget.ShortNames([Paris.Name, "paris, Ontario, Canada"]));
        // Coordinates typed in have no name, so they keep both numbers, without the comma that can't be picked.
        Assert.Equal(["Leeds", "51.5 -0.12", "51.5 2"], WeatherWidget.ShortNames([Leeds.Name, "51.5, -0.12", "51.5,2"]));
        Assert.True(PickList.CanPick("51.5 -0.12", allowAll: false));
    }

    [Fact]
    public void Chosen_places_are_found_by_name_in_the_order_chosen()
    {
        string[] names = [Leeds.Name, Paris.Name, "Paris, Texas, United States"];
        Assert.Equal([2, 0], WeatherWidget.Chosen(["paris (texas)", "Leeds", "Nowhere"], names));
        // Chosen as plain Paris before Paris, Texas came along: the first Paris.
        Assert.Equal([1], WeatherWidget.Chosen(["Paris", "Paris (Île-de-France)"], names));
        Assert.Empty(WeatherWidget.Chosen(["Gone"], names));
        Assert.Equal([1], WeatherWidget.Chosen(["51.5 -0.12"], [Leeds.Name, "51.5, -0.12"]));
    }

    [Fact]
    public void A_place_chosen_by_its_longer_name_is_still_found_once_the_other_of_that_name_goes()
    {
        // Chosen while there were two Parises; then one is removed, and the other is plain Paris.
        string[] withoutFrance = [Leeds.Name, "Paris, Texas, United States"];
        Assert.Equal([0, 1], WeatherWidget.Chosen(["Leeds", "Paris (Texas)"], withoutFrance));
        Assert.Equal([1, 0], WeatherWidget.Chosen(["Paris (Île-de-France)", "Leeds"], [Leeds.Name, Paris.Name]));
        // The one removed isn't taken for the other.
        Assert.Equal([0], WeatherWidget.Chosen(["Leeds", "Paris (Île-de-France)"], withoutFrance));

        // And the editor ticks what the widget shows, rather than saying it's not found.
        Assert.Equal("Paris", WeatherWidget.PlaceOption("Paris (Texas)", withoutFrance));
        Assert.Equal("Paris (Île-de-France)", WeatherWidget.PlaceOption("Paris", [Leeds.Name, Paris.Name, "Paris, Texas, United States"]));
        Assert.Null(WeatherWidget.PlaceOption("Paris (Île-de-France)", withoutFrance));
        Assert.Equal(
            [new PickEntry("Paris", Ticked: true, Found: true), new PickEntry("Leeds", Ticked: false, Found: true)],
            PickList.Parse("Paris (Texas)", allowAll: false).Entries(WeatherWidget.ShortNames(withoutFrance), name => WeatherWidget.PlaceOption(name, withoutFrance)));
        // Two spellings of the same place are ticked once.
        Assert.Equal(
            [new PickEntry("Paris", Ticked: true, Found: true), new PickEntry("Leeds", Ticked: false, Found: true)],
            PickList.Parse("Paris (Texas), paris", allowAll: false).Entries(WeatherWidget.ShortNames(withoutFrance), name => WeatherWidget.PlaceOption(name, withoutFrance)));
    }

    // ---- The widget ---------------------------------------------------------------------------------

    [Fact]
    public void Without_places_chosen_it_shows_just_the_location_as_before()
    {
        Sta.Run(() =>
        {
            var store = Store();
            var widget = Weather([]);
            foreach (var seconds in new[] { 0, 10, 60 })
            {
                widget.Refresh(store, Start.AddSeconds(seconds));
                Assert.Contains("11°", Texts(widget));
                Assert.DoesNotContain("Leeds", Texts(widget));
            }
            // Nothing to tap but the warning strip.
            Assert.Single(Descendants<TapTarget>(widget));
        });
    }

    [Fact]
    public void Chosen_places_show_one_at_a_time_with_their_names_and_move_on_by_themselves()
    {
        Sta.Run(() =>
        {
            var store = Store();
            var widget = Weather(new JsonObject { ["places"] = "Paris, Leeds", ["placeSeconds"] = 10 });

            widget.Refresh(store, Start);
            // Named, though Location is off, so it's clear which place it is.
            Assert.Contains("18°", Texts(widget));
            Assert.Contains("Paris", Texts(widget));
            widget.Refresh(store, Start.AddSeconds(9));
            Assert.Contains("Paris", Texts(widget));

            widget.Refresh(store, Start.AddSeconds(10));
            Assert.Contains("11°", Texts(widget));
            Assert.Contains("Leeds", Texts(widget));
            Assert.DoesNotContain("Paris", Texts(widget));

            // Round again.
            widget.Refresh(store, Start.AddSeconds(20.5));
            Assert.Contains("Paris", Texts(widget));
            widget.Refresh(store, Start.AddSeconds(30));
            Assert.Contains("Leeds", Texts(widget));
        });
    }

    [Fact]
    public void A_tap_moves_on_straight_away_and_the_next_place_gets_its_full_time()
    {
        Sta.Run(() =>
        {
            var store = Store();
            var widget = Weather(new JsonObject { ["places"] = "Leeds, Paris, Sydney", ["placeSeconds"] = 10 });
            widget.Refresh(store, Start);
            widget.Refresh(store, Start.AddSeconds(6));
            Assert.Contains("Leeds", Texts(widget));

            // The whole widget is the tap target, apart from the warning strip.
            var tap = Descendants<TapTarget>(widget).First();
            Assert.IsType<Grid>(tap.Child);
            tap.Tap();
            Assert.Contains("Paris", Texts(widget));
            Assert.Contains("18°", Texts(widget));

            // Ten seconds from the tap, not from when Leeds came up.
            widget.Refresh(store, Start.AddSeconds(12));
            Assert.Contains("Paris", Texts(widget));
            widget.Refresh(store, Start.AddSeconds(16));
            // Sydney has no forecast yet, and says so.
            Assert.Contains("Sydney", Texts(widget));
            Assert.Contains("No weather yet", Texts(widget));
            Assert.Contains("—", Texts(widget));

            tap.Tap();
            Assert.Contains("Leeds", Texts(widget));
        });
    }

    [Fact]
    public void One_place_chosen_stays_put_and_is_named_unless_it_is_the_location()
    {
        Sta.Run(() =>
        {
            var store = Store();
            // Another place's weather is never shown as if it were your own, even without Details.
            foreach (var details in new[] { true, false })
            {
                var widget = Weather(new JsonObject { ["places"] = "Paris", ["showDetails"] = details });
                widget.Refresh(store, Start);
                widget.Refresh(store, Start.AddMinutes(5));
                Assert.Contains("18°", Texts(widget));
                Assert.Contains("Paris", Texts(widget));
                Assert.Single(Descendants<TapTarget>(widget));
            }

            // Your location is named only with Location on, as without places chosen.
            var location = Weather(new JsonObject { ["places"] = "Leeds", ["showDetails"] = false });
            location.Refresh(store, Start);
            Assert.Contains("11°", Texts(location));
            Assert.DoesNotContain("Leeds", Texts(location));
            // Nor does it leave room beside the temperature for a name it doesn't show.
            Assert.DoesNotContain(Descendants<StackPanel>(location), p => p.Visibility == Visibility.Visible && p.Children.Count == 0);
            var named = Weather(new JsonObject { ["places"] = "Leeds", ["showLocation"] = true });
            named.Refresh(store, Start);
            Assert.Contains("Leeds", Texts(named));
        });
    }

    [Fact]
    public void Places_chosen_but_only_one_still_found_name_it()
    {
        Sta.Run(() =>
        {
            // Leeds was the location when they were chosen; now Manchester is, and only Paris is left of them.
            var store = Store(morePlaces: false);
            store.SetText(WeatherProvider.Location, "Manchester, England, United Kingdom");
            store.SetAttachment(WeatherProvider.Places, (IReadOnlyList<PlaceForecast>)[new("Manchester, England, United Kingdom", LeedsWeather), new(Paris.Name, ParisWeather)]);
            var widget = Weather(new JsonObject { ["places"] = "Leeds, Paris" });
            widget.Refresh(store, Start);
            widget.Refresh(store, Start.AddSeconds(30));
            Assert.Contains("18°", Texts(widget));
            Assert.Contains("Paris", Texts(widget));
        });
    }

    [Fact]
    public void A_tap_moves_on_every_weather_widget_beside_it_showing_the_same_places()
    {
        Sta.Run(() =>
        {
            var store = Store();
            var dashboard = new Canvas();
            var now = Weather(new JsonObject { ["places"] = "Leeds, Paris" });
            var days = Weather(new JsonObject { ["places"] = "leeds, paris", ["showCurrent"] = false });
            // Other places, or another pace, go their own way.
            var others = Weather(new JsonObject { ["places"] = "Leeds, Sydney", ["showCurrent"] = false });
            var slower = Weather(new JsonObject { ["places"] = "Leeds, Paris", ["placeSeconds"] = 30 });
            Widget[] widgets = [now, days, others, slower];
            foreach (var widget in widgets)
            {
                dashboard.Children.Add(widget);
                widget.Refresh(store, Start);
                widget.Refresh(store, Start.AddSeconds(4));
            }

            Descendants<TapTarget>(now).First().Tap();
            Assert.Contains("Paris", Texts(now));
            Assert.Contains("Paris", Texts(days));
            Assert.Contains("Leeds", Texts(others));
            Assert.Contains("Leeds", Texts(slower));

            // And they keep together from there.
            foreach (var widget in widgets)
                widget.Refresh(store, Start.AddSeconds(14));
            Assert.Contains("Leeds", Texts(now));
            Assert.Contains("Leeds", Texts(days));
        });
    }

    [Fact]
    public void Places_that_are_gone_leave_the_location()
    {
        Sta.Run(() =>
        {
            // Chosen places that were removed, or no more places at all: the location, as without any chosen.
            foreach (var store in new[] { Store(), Store(morePlaces: false) })
            {
                var widget = Weather(new JsonObject { ["places"] = "Gone, Nowhere" });
                widget.Refresh(store, Start);
                widget.Refresh(store, Start.AddSeconds(30));
                Assert.Contains("11°", Texts(widget));
                Assert.DoesNotContain("Leeds", Texts(widget));
            }
        });
    }

    [Fact]
    public void The_warning_strip_shows_only_while_the_location_is_on_show()
    {
        Sta.Run(() =>
        {
            var store = Store();
            store.SetAttachment(WeatherWarningsProvider.Warnings, (IReadOnlyList<WeatherWarning>)[Yellow]);
            var widget = Weather(new JsonObject { ["places"] = "Leeds, Paris", ["placeSeconds"] = 10 });
            var actions = new List<WidgetAction>();
            widget.AddHandler(Widget.ActionEvent, new EventHandler<WidgetActionEventArgs>((_, e) => actions.Add(e.Action)));

            widget.Refresh(store, Start);
            var strip = Descendants<TapTarget>(widget).Last();
            Assert.Equal(Visibility.Visible, strip.Visibility);

            // A tap on the strip opens the warning rather than moving on.
            strip.Tap();
            Assert.Equal([new WidgetAction(WidgetAction.Open, Yellow.Link!)], actions);
            Assert.Contains("Leeds", Texts(widget));

            // Paris's weather isn't Leeds's warning.
            widget.Refresh(store, Start.AddSeconds(10));
            Assert.Contains("Paris", Texts(widget));
            Assert.Equal(Visibility.Collapsed, strip.Visibility);

            Descendants<TapTarget>(widget).First().Tap();
            Assert.Contains("Leeds", Texts(widget));
            Assert.Equal(Visibility.Visible, strip.Visibility);
        });
    }

    [Fact]
    public void A_forecast_only_widget_names_the_place_above_its_forecast()
    {
        Sta.Run(() =>
        {
            var store = Store();
            var widget = Weather(new JsonObject { ["places"] = "Leeds, Paris", ["showCurrent"] = false });
            widget.Refresh(store, Start);
            var name = Descendants<TextBlock>(widget).Single(t => t.Text == "Leeds");
            Assert.Equal(Visibility.Visible, name.Visibility);
            // Above the days.
            var days = Descendants<UniformGrid>(widget).Single();
            Assert.Same(name.Parent, days.Parent);

            widget.Refresh(store, Start.AddSeconds(10));
            Assert.Equal("Paris", name.Text);
        });
    }

    [Fact]
    public void Places_are_picked_from_the_apps_list_and_each_shows_ten_seconds_by_default()
    {
        var places = WeatherWidget.Descriptor.Find("places")!;
        Assert.Equal(WeatherWidget.PlaceNames, places.OptionsSource);
        // Nothing picked is the location alone, so there's no "all" to pick.
        Assert.Null(places.Default);
        Assert.Null(places.AllLabel);

        var seconds = WeatherWidget.Descriptor.Find("placeSeconds")!;
        Assert.Equal(10d, seconds.Default);
        Assert.Equal(3, seconds.Min);
        Assert.Equal(600, seconds.Max);
    }

    // ---- The Weather page ---------------------------------------------------------------------------

    [Fact]
    public void The_weather_page_adds_reorders_swaps_and_removes_places()
    {
        Sta.Run(() =>
        {
            var settings = new AppSettings { WeatherLocation = Leeds };
            var host = DispatchProxy.Create<ISettingsHost, PlacesHost>();
            var places = (PlacesHost)(object)host;
            places.Settings = settings;
            var page = new WeatherPage(host);
            page.Load();

            // None yet: just the card, saying what's sent.
            var card = Card(page, "More places");
            Assert.Null(card.Detail);
            Assert.Contains("every 15 minutes", card.Description);
            Assert.Contains("Turn off weather", Logical<SettingsCard>(page).Select(c => c.Header));
            Assert.DoesNotContain("more places", Card(page, "Turn off weather").Description);

            WeatherLocation? picked = Paris;
            page.AskPlace = _ => picked;
            Click(Button(page, "Add…"));
            picked = Sydney;
            Click(Button(page, "Add…"));
            Assert.Equal([Paris, Sydney], settings.WeatherPlaces);
            Assert.Equal([Paris.Name, Sydney.Name], Rows(page).Select(r => Lines(r)[0]));
            Assert.Contains("the more places", Card(page, "Turn off weather").Description);

            // The same place again isn't added twice, and the page says why.
            picked = Paris with { Name = "Paris" };
            Click(Button(page, "Add…"));
            Assert.Equal([Paris, Sydney], settings.WeatherPlaces);
            Assert.Contains("Paris is one of your places already.", LogicalTexts(page));
            // Cancelled, nothing happens.
            picked = null;
            Click(Button(page, "Add…"));
            Assert.Equal([Paris, Sydney], settings.WeatherPlaces);

            // Dragged (or Alt+Up) by its handle.
            var handle = Logical<Border>(Card(page, "More places").Detail as Panel).Single(b => AutomationProperties.GetName(b) == $"Reorder {Sydney.Name}");
            Assert.True(DragReorder.Move(handle, -1));
            Assert.Equal([Sydney, Paris], settings.WeatherPlaces);

            // Made the location, which takes its place in the list.
            Click(Button(page, $"Make {Paris.Name} the main place"));
            Assert.Equal(Paris, settings.WeatherLocation);
            Assert.Equal([Sydney, Leeds], settings.WeatherPlaces);

            Click(Button(page, $"Remove {Sydney.Name}"));
            Assert.Equal([Leeds], settings.WeatherPlaces);
            // Each with its weather now, or why there's none yet.
            Assert.Equal([Leeds.Name, "Waiting for the forecast"], Lines(Assert.Single(Rows(page))));

            // Where the places are isn't logged: it goes with bug reports.
            Assert.Equal(["Added a weather place", "Added a weather place", "Weather places reordered", "Removed a weather place"], places.Logs);
        });
    }

    [Fact]
    public void Without_a_location_there_are_no_more_places_to_add()
    {
        Sta.Run(() =>
        {
            var host = DispatchProxy.Create<ISettingsHost, PlacesHost>();
            ((PlacesHost)(object)host).Settings = new AppSettings();
            var page = new WeatherPage(host);
            page.Load();
            Assert.DoesNotContain("More places", Logical<SettingsCard>(page).Select(c => c.Header));
        });
    }

    [Fact]
    public void Adding_another_place_says_what_is_sent_for_it()
    {
        Sta.Run(() =>
        {
            var window = new WeatherLocationWindow(null, (_, _) => Task.FromResult(new PlaceSearchResults([], false)), another: true);
            Assert.Equal("Add this place", window.OkButton.Content);
            Assert.Equal(WeatherLocationWindow.AnotherPlaceNote, window.PrivacyNote.Text);
            Assert.Contains("every 15 minutes", window.PrivacyNote.Text);
            window.Close();

            var location = new WeatherLocationWindow(null, (_, _) => Task.FromResult(new PlaceSearchResults([], false)));
            Assert.Equal("Use this place", location.OkButton.Content);
            location.Close();
        });
    }

    /// <summary>A settings host that keeps the more places in its settings, as the app does, and notes what it logs.</summary>
    public class PlacesHost : DispatchProxy
    {
        public AppSettings Settings { get; set; } = new();

        public List<string> Logs { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "get_Settings":
                    return Settings;
                case nameof(ISettingsHost.SetWeatherPlaces):
                    Settings.WeatherPlaces = [.. (IReadOnlyList<WeatherLocation>)args![0]!];
                    Logs.Add((string)args[1]!);
                    return null;
                case nameof(ISettingsHost.MakeMainWeatherPlace):
                    Settings.MakeMainWeatherPlace((WeatherLocation)args![0]!);
                    return null;
                case nameof(ISettingsHost.WeatherPlaceStatus):
                    return "Waiting for the forecast";
                default:
                    return targetMethod?.ReturnType is { IsValueType: true } type && type != typeof(void) ? Activator.CreateInstance(type) : null;
            }
        }
    }

    private static SettingsCard Card(DependencyObject page, string header) => Logical<SettingsCard>(page).Single(c => c.Header == header);

    /// <summary>The More places card's rows, one a place (none while it has no list).</summary>
    private static List<DockPanel> Rows(DependencyObject page) => Card(page, "More places").Detail is Panel list ? [.. list.Children.OfType<DockPanel>()] : [];

    /// <summary>A place's name and its weather now, beside its handle and buttons.</summary>
    private static List<string> Lines(DockPanel row) => [.. ((Panel)row.Children[^1]).Children.OfType<TextBlock>().Select(t => t.Text)];

    /// <summary>A button by its name for screen readers, on a card or in a row of the places.</summary>
    private static Button Button(DependencyObject page, string name) =>
        Logical<Button>(page).Concat(Rows(page).SelectMany(Logical<Button>)).First(b => AutomationProperties.GetName(b) == name);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static IEnumerable<T> Logical<T>(DependencyObject? root) where T : DependencyObject =>
        root is null ? [] : LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().SelectMany(child => child is T match ? [match, .. Logical<T>(child)] : Logical<T>(child));

    /// <summary>Text in a page that hasn't been shown, so has no visual tree yet.</summary>
    private static List<string> LogicalTexts(DependencyObject root) =>
        [.. LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().SelectMany(child => child is TextBlock text ? [text.Text] : LogicalTexts(child))];

    private static List<string> Texts(DependencyObject root) =>
        Descendants<TextBlock>(root).Where(t => t.Visibility == Visibility.Visible)
            .Select(t => string.Concat(t.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text)) is { Length: > 0 } runs ? runs : t.Text).ToList();

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
