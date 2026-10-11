using System.IO;
using System.Text.Json;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Tests;

// Shares AppPaths (a static) with the layout store tests, so they mustn't run in parallel.
[Collection("AppPaths")]
public sealed class ScheduleTests : IDisposable
{
    private static readonly ScheduleRule Night = new("22:00", "07:00", null, null, "Night Watch");
    private static readonly ScheduleRule Work = new("9:00", "17:30", null, "Work", null);
    private static readonly ScheduleRule DarkMode = new(null, null, "dark", null, "Nord");
    private static readonly ScheduleRule LightMode = new(null, null, "LIGHT", null, "Glacier");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ImpiousBonum.Tests", Guid.NewGuid().ToString("N"));

    public ScheduleTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Theory]
    [InlineData("21:59", false)]
    [InlineData("22:00", true)]
    [InlineData("23:59", true)]
    [InlineData("00:00", true)]
    [InlineData("06:59", true)]
    [InlineData("07:00", false)]
    [InlineData("12:00", false)]
    public void A_time_span_can_cross_midnight(string now, bool on) =>
        Assert.Equal(on, Night.Matches(TimeOnly.Parse(now), windowsDark: false));

    [Theory]
    [InlineData("08:59", false)]
    [InlineData("09:00", true)]
    [InlineData("17:29", true)]
    [InlineData("17:30", false)]
    public void A_time_span_within_a_day_starts_on_time_and_ends_on_time(string now, bool on) =>
        Assert.Equal(on, Work.Matches(TimeOnly.Parse(now), windowsDark: false));

    [Fact]
    public void Colour_mode_rules_follow_windows()
    {
        Assert.True(DarkMode.Matches(new TimeOnly(12, 0), windowsDark: true));
        Assert.False(DarkMode.Matches(new TimeOnly(12, 0), windowsDark: false));
        Assert.True(LightMode.Matches(new TimeOnly(12, 0), windowsDark: false));
    }

    [Fact]
    public void The_first_rule_that_applies_wins()
    {
        ScheduleRule[] rules = [Night, DarkMode];
        Assert.Same(Night, ScheduleRule.Match(rules, new TimeOnly(23, 0), windowsDark: true));
        Assert.Same(DarkMode, ScheduleRule.Match(rules, new TimeOnly(12, 0), windowsDark: true));
        Assert.Null(ScheduleRule.Match(rules, new TimeOnly(12, 0), windowsDark: false));
    }

    [Fact]
    public void Rules_need_a_when_and_a_what()
    {
        Assert.True(Night.IsUsable);
        Assert.False(new ScheduleRule("22:00", null, null, null, "Nord").IsUsable);
        Assert.False(new ScheduleRule("22:00", "22:00", null, null, "Nord").IsUsable);
        Assert.False(new ScheduleRule("10pm", "7am", null, null, "Nord").IsUsable);
        Assert.False(new ScheduleRule(null, null, "dusk", null, "Nord").IsUsable);
        Assert.False(new ScheduleRule("22:00", "07:00", null, null, null).IsUsable);
    }

    [Fact]
    public void Rules_describe_themselves()
    {
        Assert.Equal("22:00–07:00: theme Night Watch", Night.Describe());
        Assert.Equal("9:00–17:30: Work", Work.Describe());
        Assert.Equal("Windows dark mode: Gaming, theme Nord", (DarkMode with { Layout = "Gaming" }).Describe());
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

        var loaded = AppSettings.Load();
        Assert.Equal(new[] { Night, DarkMode }, loaded.Schedule);
        Assert.False(loaded.ScheduleEnabled);

        File.WriteAllText(AppPaths.SettingsFile, """{ "schedule": [ { "from": "22:00", "to": "07:00", "theme": "Nord" }, { "from": "soon", "theme": "Nord" }, null ] }""");
        Assert.Single(AppSettings.Load().Schedule);
    }

    [Fact]
    public void The_add_dialog_starts_with_a_night_theme_and_checks_what_you_type()
    {
        Sta.Run(() =>
        {
            var window = new ScheduleRuleWindow(["Default", "Gaming"], ["Glacier", "Night Watch", "Nord"], "Night Watch");
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
