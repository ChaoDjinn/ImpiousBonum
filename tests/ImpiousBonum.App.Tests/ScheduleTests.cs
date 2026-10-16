using System.Globalization;
using System.IO;
using System.Windows;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.App.Tests;

// Shares AppPaths (a static) with the layout store tests, so they mustn't run in parallel.
[Collection("AppPaths")]
public sealed class ScheduleTests : IDisposable
{
    private static readonly ScheduleRule Night = new("22:00", "07:00", null, null, "Night Watch");
    private static readonly ScheduleRule Work = new("9:00", "17:30", null, "Work", null);
    private static readonly ScheduleRule DarkMode = new(null, null, "dark", null, "Nord");
    private static readonly ScheduleRule LightMode = new(null, null, "LIGHT", null, "Glacier");
    private static readonly ScheduleRule Spooky = new(null, null, null, null, "Grimoire", FromDate: "10-24", ToDate: "11-01");
    private static readonly ScheduleRule Winter = new(null, null, null, null, "Ice", FromDate: "12-20", ToDate: "01-06");
    private static readonly ScheduleRule AfterDark = new(null, null, "night", null, "Night Watch");
    private static readonly ScheduleRule Daylight = new(null, null, "Day", null, "Glacier");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ImpiousBonum.Tests", Guid.NewGuid().ToString("N"));

    public ScheduleTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    /// <summary>A moment to check rules against, on a Friday in October unless it says otherwise.</summary>
    private static ScheduleNow At(string time, string date = "2026-10-16", bool windowsDark = false, bool? sunDown = null) =>
        new(DateTime.Parse($"{date} {time}", CultureInfo.InvariantCulture), windowsDark, sunDown);

    [Theory]
    [InlineData("21:59", false)]
    [InlineData("22:00", true)]
    [InlineData("23:59", true)]
    [InlineData("00:00", true)]
    [InlineData("06:59", true)]
    [InlineData("07:00", false)]
    [InlineData("12:00", false)]
    public void A_time_span_can_cross_midnight(string now, bool on) =>
        Assert.Equal(on, Night.Matches(At(now)));

    [Theory]
    [InlineData("08:59", false)]
    [InlineData("09:00", true)]
    [InlineData("17:29", true)]
    [InlineData("17:30", false)]
    public void A_time_span_within_a_day_starts_on_time_and_ends_on_time(string now, bool on) =>
        Assert.Equal(on, Work.Matches(At(now)));

    [Fact]
    public void Colour_mode_rules_follow_windows()
    {
        Assert.True(DarkMode.Matches(At("12:00", windowsDark: true)));
        Assert.False(DarkMode.Matches(At("12:00", windowsDark: false)));
        Assert.True(LightMode.Matches(At("12:00", windowsDark: false)));
    }

    [Theory]
    [InlineData("2026-10-23 23:59", false)]
    [InlineData("2026-10-24 00:00", true)]
    [InlineData("2026-10-31 12:00", true)]
    [InlineData("2026-11-01 23:59", true)]
    [InlineData("2026-11-02 00:00", false)]
    [InlineData("2027-10-24 00:00", true)]
    public void A_date_range_includes_both_days_every_year(string moment, bool on) =>
        Assert.Equal(on, Spooky.Matches(At(moment[11..], moment[..10])));

    [Theory]
    [InlineData("2026-12-19", false)]
    [InlineData("2026-12-20", true)]
    [InlineData("2026-12-31", true)]
    [InlineData("2027-01-01", true)]
    [InlineData("2027-01-06", true)]
    [InlineData("2027-01-07", false)]
    [InlineData("2027-07-01", false)]
    public void A_date_range_can_cross_new_year(string date, bool on) =>
        Assert.Equal(on, Winter.Matches(At("12:00", date)));

    [Fact]
    public void Dates_combine_with_times_and_a_night_belongs_to_the_evening_it_starts()
    {
        var halloweenNight = new ScheduleRule("18:00", "02:00", null, null, "Grimoire", FromDate: "10-31", ToDate: "10-31");
        Assert.False(halloweenNight.Matches(At("17:59", "2026-10-31")));
        Assert.True(halloweenNight.Matches(At("18:00", "2026-10-31")));
        Assert.True(halloweenNight.Matches(At("01:30", "2026-11-01")));
        Assert.False(halloweenNight.Matches(At("02:00", "2026-11-01")));
        // The early hours of the 31st are the night of the 30th, and the evening of 1 November isn't Halloween.
        Assert.False(halloweenNight.Matches(At("01:30", "2026-10-31")));
        Assert.False(halloweenNight.Matches(At("19:00", "2026-11-01")));

        // New Year's Eve runs on into the next year.
        var newYearsEve = new ScheduleRule("20:00", "03:00", null, null, "Gatsby", FromDate: "12-31", ToDate: "12-31");
        Assert.True(newYearsEve.Matches(At("01:00", "2027-01-01")));
        Assert.False(newYearsEve.Matches(At("01:00", "2026-12-31")));

        // Within a day, the times apply on each day in the range.
        var workDays = Work with { FromDate = "12-01", ToDate = "12-23" };
        Assert.True(workDays.Matches(At("10:00", "2026-12-23")));
        Assert.False(workDays.Matches(At("10:00", "2026-12-24")));
    }

    [Fact]
    public void Sun_rules_follow_the_sun_where_the_weather_is_and_never_apply_without_a_location()
    {
        Assert.True(AfterDark.Matches(At("21:00", sunDown: true)));
        Assert.False(AfterDark.Matches(At("21:00", sunDown: false)));
        Assert.True(Daylight.Matches(At("12:00", sunDown: false)));
        Assert.False(Daylight.Matches(At("12:00", sunDown: true)));

        // No location: kept, but never on.
        Assert.True(AfterDark.IsUsable);
        Assert.True(AfterDark.FollowsSun);
        Assert.False(Night.FollowsSun);
        Assert.False(AfterDark.Matches(At("21:00")));
        Assert.False(Daylight.Matches(At("12:00")));

        // Christmas Eve's night lasts until the sun comes up on Christmas morning.
        var christmasEve = AfterDark with { FromDate = "12-24", ToDate = "12-24" };
        Assert.True(christmasEve.Matches(At("05:00", "2026-12-25", sunDown: true)));
        Assert.False(christmasEve.Matches(At("18:00", "2026-12-25", sunDown: true)));
    }

    [Theory]
    // London on 11 October 2026: sunrise at 07:17 BST (06:17 UTC) and sunset at 18:13 BST (17:13 UTC).
    [InlineData("2026-10-11T06:00:00Z", true)]
    [InlineData("2026-10-11T06:30:00Z", false)]
    [InlineData("2026-10-11T12:00:00Z", false)]
    [InlineData("2026-10-11T17:00:00Z", false)]
    [InlineData("2026-10-11T17:30:00Z", true)]
    [InlineData("2026-10-11T23:00:00Z", true)]
    public void After_sunset_and_before_sunrise_come_from_the_location(string time, bool night)
    {
        var instant = DateTimeOffset.Parse(time, CultureInfo.InvariantCulture);
        var now = new ScheduleNow(instant.UtcDateTime.AddHours(1), WindowsDark: false, SunDown: SunPosition.IsDown(51.51, -0.13, instant));
        Assert.Equal(night, AfterDark.Matches(now));
        Assert.Equal(!night, Daylight.Matches(now));
    }

    [Fact]
    public void Rules_combine_and_for_each_setting_the_first_rule_that_sets_it_wins()
    {
        ScheduleRule[] rules = [Night, DarkMode, new("23:00", "06:00", null, "Bedside", null, 30), new("00:00", "05:00", null, null, null, 0)];

        Assert.Equal(new ScheduledLook(null, "Night Watch", null), ScheduledLook.From(rules, At("22:30")));
        Assert.Equal(new ScheduledLook("Bedside", "Night Watch", 30), ScheduledLook.From(rules, At("01:00", windowsDark: true)));
        Assert.Equal(new ScheduledLook(null, "Nord", null), ScheduledLook.From(rules, At("12:00", windowsDark: true)));
        Assert.Null(ScheduledLook.From(rules, At("12:00", windowsDark: false)));
        Assert.Equal(new[] { Night, DarkMode }, ScheduledLook.Active(rules, At("22:30", windowsDark: true)));
        Assert.Equal("Bedside, theme Night Watch, 30% brightness", new ScheduledLook("Bedside", "Night Watch", 30).Describe());

        // A date rule listed first wins its dates; the night theme comes back after them.
        ScheduleRule[] seasonal = [Spooky, AfterDark, Night with { Theme = null, Layout = "Bedside" }];
        Assert.Equal(new ScheduledLook("Bedside", "Grimoire", null), ScheduledLook.From(seasonal, At("22:30", "2026-10-31", sunDown: true)));
        Assert.Equal(new ScheduledLook("Bedside", "Night Watch", null), ScheduledLook.From(seasonal, At("22:30", "2026-11-05", sunDown: true)));
    }

    [Fact]
    public void A_rule_can_just_dim_the_screen()
    {
        var dim = new ScheduleRule("23:00", "07:00", null, null, null, 0);
        Assert.True(dim.IsUsable);
        Assert.Equal("23:00–07:00: screen off", dim.Describe());
        Assert.False((dim with { Brightness = 100 }).IsUsable);
        Assert.False((dim with { Brightness = -5 }).IsUsable);
    }

    [Theory]
    // scheduled, idle minutes, away after, away level, game drawing, peeking, editing → brightness
    [InlineData(null, 0, 0, 20, false, false, false, 1.0)]
    [InlineData(30, 0, 0, 20, false, false, false, 0.3)]
    [InlineData(null, 16, 15, 20, false, false, false, 0.2)]
    [InlineData(null, 14, 15, 20, false, false, false, 1.0)]
    [InlineData(30, 16, 15, 0, false, false, false, 0.0)]
    [InlineData(10, 16, 15, 40, false, false, false, 0.1)]
    [InlineData(null, 60, 15, 20, true, false, false, 1.0)]
    [InlineData(0, 60, 15, 0, false, true, false, 1.0)]
    [InlineData(0, 60, 15, 0, false, false, true, 1.0)]
    public void Brightness_takes_the_dimmest_of_the_schedule_and_being_away(
        int? scheduled, int idleMinutes, int awayMinutes, int awayLevel, bool gameDrawing, bool peeking, bool editing, double expected) =>
        Assert.Equal(expected, Dimming.Brightness(scheduled, TimeSpan.FromMinutes(idleMinutes), awayMinutes, awayLevel, gameDrawing, peeking, editing), 6);

    [Fact]
    public void Rules_need_a_when_and_a_what()
    {
        Assert.True(Night.IsUsable);
        Assert.False(new ScheduleRule("22:00", null, null, null, "Nord").IsUsable);
        Assert.False(new ScheduleRule("22:00", "22:00", null, null, "Nord").IsUsable);
        Assert.False(new ScheduleRule("10pm", "7am", null, null, "Nord").IsUsable);
        Assert.False(new ScheduleRule(null, null, "dusk", null, "Nord").IsUsable);
        Assert.False(new ScheduleRule("22:00", "07:00", null, null, null).IsUsable);
        Assert.False(new ScheduleRule(null, null, null, null, "Nord").IsUsable);

        // Dates alone are a when; dates have to make sense, and need both ends.
        Assert.True(Spooky.IsUsable);
        Assert.True((Night with { FromDate = "2-29", ToDate = "3-1" }).IsUsable);
        Assert.False((Spooky with { FromDate = "31-10" }).IsUsable);
        Assert.False((Spooky with { ToDate = null }).IsUsable);
        Assert.False((Night with { FromDate = "12-25" }).IsUsable);
        Assert.False((Spooky with { When = "dusk" }).IsUsable);
        Assert.False((Spooky with { From = "18:00" }).IsUsable);
    }

    [Fact]
    public void Rules_describe_themselves()
    {
        Assert.Equal("22:00–07:00: theme Night Watch", Night.Describe());
        Assert.Equal("9:00–17:30: Work", Work.Describe());
        Assert.Equal("Windows dark mode: Gaming, theme Nord", (DarkMode with { Layout = "Gaming" }).Describe());
        Assert.Equal("Sunset to sunrise: theme Night Watch", AfterDark.Describe());
        Assert.Equal("24 Oct–1 Nov: theme Grimoire", Spooky.Describe());
        Assert.Equal("20 Dec–6 Jan, sunrise to sunset: theme Ice", (Winter with { When = "day" }).Describe());
        Assert.Equal("25 Dec, 18:00–23:00: theme Ice", (Winter with { From = "18:00", To = "23:00", FromDate = "12-25", ToDate = "12-25" }).Describe());
    }

    [Fact]
    public void The_schedule_is_saved_by_hand_editable_names_and_bad_rules_are_dropped()
    {
        AppPaths.UseDataDirectory(_directory);
        AppPaths.EnsureCreated();
        new AppSettings { Schedule = [Night, DarkMode], ScheduleEnabled = false }.Save();
        var json = File.ReadAllText(AppPaths.SettingsFile);
        Assert.Contains("\"from\": \"22:00\"", json);
        Assert.Contains("\"when\": \"dark\"", json);
        Assert.DoesNotContain("\"layout\": null", json);
        // Rules from before dates and the sun are saved just as they were.
        Assert.DoesNotContain("fromDate", json);
        Assert.DoesNotContain("toDate", json);

        var loaded = AppSettings.Load();
        Assert.Equal(new[] { Night, DarkMode }, loaded.Schedule);
        Assert.False(loaded.ScheduleEnabled);

        var halloween = new ScheduleRule("18:00", "23:00", null, null, "Grimoire", FromDate: "10-31", ToDate: "10-31");
        new AppSettings { Schedule = [Spooky, AfterDark, halloween] }.Save();
        json = File.ReadAllText(AppPaths.SettingsFile);
        Assert.Contains("\"fromDate\": \"10-24\"", json);
        Assert.Contains("\"toDate\": \"11-01\"", json);
        Assert.Contains("\"when\": \"night\"", json);
        Assert.Equal(new[] { Spooky, AfterDark, halloween }, AppSettings.Load().Schedule);

        File.WriteAllText(AppPaths.SettingsFile, """
            { "schedule": [ { "from": "22:00", "to": "07:00", "theme": "Nord" }, { "from": "soon", "theme": "Nord" }, null,
              { "fromDate": "12-20", "toDate": "1-6", "theme": "Ice" }, { "fromDate": "31-10", "toDate": "11-01", "theme": "Nord" },
              { "fromDate": "10-24", "theme": "Nord" }, { "when": "day", "theme": "Glacier" } ] }
            """);
        Assert.Equal(
            new[] { new ScheduleRule("22:00", "07:00", null, null, "Nord"), Winter with { ToDate = "1-6" }, Daylight with { When = "day" } },
            AppSettings.Load().Schedule);
    }

    [Fact]
    public void The_add_dialog_makes_each_kind_of_rule()
    {
        Sta.Run(() =>
        {
            var window = new ScheduleRuleWindow(["Default", "Gaming"], ["Glacier", "Grimoire", "Night Watch"], "Night Watch", hasLocation: true);
            window.Night.IsChecked = true;
            Assert.Equal(new ScheduleRule(null, null, "night", null, "Night Watch"), window.Rule());
            Assert.Equal(Visibility.Collapsed, window.SunNote.Visibility);
            window.Daytime.IsChecked = true;
            Assert.Equal(new ScheduleRule(null, null, "day", null, "Night Watch"), window.Rule());

            // All day only makes sense on some dates.
            window.AllDay.IsChecked = true;
            Assert.NotNull(window.Problem());
            window.OnDates.IsChecked = true;
            window.FromDateBox.Text = "10-24";
            window.ToDateBox.Text = "11-1";
            window.ThemeBox.SelectedItem = "Grimoire";
            Assert.Null(window.Problem());
            Assert.Equal(new ScheduleRule(null, null, null, null, "Grimoire", FromDate: "10-24", ToDate: "11-01"), window.Rule());
            Assert.Equal("Every year from 24 Oct to 1 Nov, both days included.", window.DatesNote.Text);

            // A day written first, as in 31-10, isn't a month.
            window.FromDateBox.Text = "31-10";
            Assert.NotNull(window.Problem());
            window.FromDateBox.Text = "10-31";
            window.ToDateBox.Text = "10-31";
            Assert.Equal("Every year on 31 Oct.", window.DatesNote.Text);

            // Dates go with times, the sun or Windows' mode too.
            window.Between.IsChecked = true;
            window.FromBox.Text = "18:00";
            window.ToBox.Text = "2:00";
            Assert.Equal(new ScheduleRule("18:00", "02:00", null, null, "Grimoire", FromDate: "10-31", ToDate: "10-31"), window.Rule());
            window.Night.IsChecked = true;
            Assert.Equal(new ScheduleRule(null, null, "night", null, "Grimoire", FromDate: "10-31", ToDate: "10-31"), window.Rule());
            window.OnDates.IsChecked = false;
            Assert.Equal(new ScheduleRule(null, null, "night", null, "Grimoire"), window.Rule());
            window.Close();

            // Without a location the sun rules can still be added, but the dialog says why they won't apply yet.
            window = new ScheduleRuleWindow(["Default"], ["Night Watch"], "Night Watch", hasLocation: false);
            Assert.Equal(Visibility.Collapsed, window.SunNote.Visibility);
            window.Night.IsChecked = true;
            Assert.Equal(Visibility.Visible, window.SunNote.Visibility);
            Assert.Null(window.Problem());
            window.Close();
        });
    }

    [Fact]
    public void The_add_dialog_starts_with_a_night_theme_and_checks_what_you_type()
    {
        Sta.Run(() =>
        {
            var window = new ScheduleRuleWindow(["Default", "Gaming"], ["Glacier", "Night Watch", "Nord"], "Night Watch", hasLocation: true);
            Assert.Null(window.Problem());
            Assert.Equal(new ScheduleRule("22:00", "07:00", null, null, "Night Watch"), window.Rule());

            window.FromBox.Text = "7:5";
            Assert.NotNull(window.Problem());
            window.FromBox.Text = "6:30";
            Assert.Equal("06:30", window.Rule().From);

            window.Dark.IsChecked = true;
            window.LayoutBox.SelectedItem = "Gaming";
            Assert.Equal(new ScheduleRule(null, null, "dark", "Gaming", "Night Watch"), window.Rule());

            window.LayoutBox.SelectedIndex = 0;
            window.ThemeBox.SelectedIndex = 0;
            Assert.NotNull(window.Problem());

            // Dimming alone is enough.
            window.BrightnessBox.SelectedIndex = 3;
            Assert.Null(window.Problem());
            Assert.Equal(new ScheduleRule(null, null, "dark", null, null, 30), window.Rule());
            window.Close();
        });
    }

    [Fact]
    public void Renaming_a_layout_updates_the_schedule()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            var settings = new AppSettings { Schedule = [Work] };
            using var store = new LayoutStore(settings);
            store.Save("Work", new LayoutDocument());

            store.Rename("Work", "Office");
            Assert.Equal(new[] { Work with { Layout = "Office" } }, AppSettings.Load().Schedule);
        });
    }
}
