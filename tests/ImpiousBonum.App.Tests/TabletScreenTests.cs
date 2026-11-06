using System.Text.Json;
using System.Text.Json.Nodes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Tests;

/// <summary>Tablet screens: a tablet's size to plan a layout at, picked in the editor like a monitor's dashboard.</summary>
public sealed class TabletScreenTests
{
    private static readonly TabletScreen Galaxy = new("Galaxy Tab", 2560, 1600, "Galaxy Tab");
    private static readonly TabletScreen Fire = new("Kitchen Fire", 800, 1280, "Recipes");

    [Fact]
    public void A_tablet_screen_is_known_by_its_name_apart_from_monitors()
    {
        Assert.Equal("tablet:Galaxy Tab", Galaxy.Id);
        Assert.Equal("Galaxy Tab · 2560×1600 (tablet)", Galaxy.DisplayName);
        Assert.False(Galaxy.IsPortrait);
        Assert.True(Fire.IsPortrait);
        Assert.Same(Fire, TabletScreen.Find([Galaxy, Fire], " kitchen FIRE "));
        Assert.Null(TabletScreen.Find([Galaxy, Fire], "Kitchen"));
    }

    [Fact]
    public void Tablet_screens_are_kept_in_settings_without_whats_worked_out_from_them()
    {
        var settings = new AppSettings { TabletScreens = [Galaxy, Fire] };
        var json = JsonSerializer.Serialize(settings, JsonDefaults.Options);
        var restored = JsonSerializer.Deserialize<AppSettings>(json, JsonDefaults.Options)!;
        Assert.Equal([Galaxy, Fire], restored.TabletScreens);

        var saved = JsonNode.Parse(json)!["tabletScreens"]![0]!.AsObject();
        Assert.Equal(["name", "width", "height", "layout"], saved.Select(p => p.Key));

        // None for anyone who hasn't added one, and a hand-edited null is none too.
        Assert.Empty(new AppSettings().TabletScreens);
        Assert.Empty(JsonSerializer.Deserialize<AppSettings>("""{ "tabletScreens": null }""", JsonDefaults.Options)!.TabletScreens);
    }

    [Theory]
    [InlineData("Galaxy Tab", 2560, 1600, null)]
    [InlineData("  Spare  ", 1280, 800, null)]
    [InlineData("", 2560, 1600, "Give it a name, such as the tablet's.")]
    [InlineData("   ", 2560, 1600, "Give it a name, such as the tablet's.")]
    [InlineData("kitchen fire", 2560, 1600, "There's already a tablet screen called \"kitchen fire\".")]
    [InlineData("Galaxy Tab", null, 1600, "Write the width and height in pixels, like 2560 and 1600.")]
    [InlineData("Galaxy Tab", 49, 1600, "The width and height can each be from 50 to 10000 pixels.")]
    [InlineData("Galaxy Tab", 2560, 10_001, "The width and height can each be from 50 to 10000 pixels.")]
    public void A_new_tablet_screen_needs_a_name_of_its_own_and_a_size(string name, int? width, int? height, string? problem) =>
        Assert.Equal(problem, TabletScreen.Problem(name, width, height, [Fire]));

    [Fact]
    public void Names_are_kept_short_and_plain()
    {
        Assert.Equal("Keep the name to 40 characters.", TabletScreen.Problem(new string('A', 41), 2560, 1600, []));
        Assert.Null(TabletScreen.Problem(new string('A', 40), 2560, 1600, []));
        Assert.Equal("The name can't contain control characters.", TabletScreen.Problem("Tab\u0007", 2560, 1600, []));
    }

    [Fact]
    public void A_new_tablet_screen_is_called_tablet_numbered_if_need_be()
    {
        Assert.Equal("Tablet", TabletScreen.NewName([]));
        Assert.Equal("Tablet 2", TabletScreen.NewName([Galaxy with { Name = "tablet" }]));
        Assert.Equal("Tablet 3", TabletScreen.NewName([Galaxy with { Name = "Tablet" }, Fire with { Name = "Tablet 2" }]));
    }

    [Fact]
    public void Its_layout_is_named_after_it_and_made_empty_at_its_size_in_the_main_theme()
    {
        Assert.Equal("Galaxy Tab", TabletScreen.NewLayoutName("Galaxy Tab", ["Default"]));
        Assert.Equal("Galaxy Tab 2", TabletScreen.NewLayoutName(" Galaxy Tab ", ["Default", "galaxy tab"]));
        // A name a file can't have.
        Assert.Equal("Tablet", TabletScreen.NewLayoutName("Tab: 10\"", ["Default"]));
        Assert.Equal("Tablet 2", TabletScreen.NewLayoutName("CON", ["Tablet"]));

        var theme = new JsonObject { ["base"] = "Nord" };
        var layout = Fire.NewLayout(theme);
        Assert.Equal((800.0, 1280.0), (layout.Width, layout.Height));
        Assert.Empty(layout.Widgets);
        Assert.Equal(theme.ToJsonString(), layout.Theme.ToJsonString());
        layout.Theme["base"] = "Glacier";
        Assert.Equal("Nord", (string?)theme["base"]);
        Assert.Empty(LayoutValidator.Validate(layout));
    }

    [Fact]
    public void The_presets_are_tablets_held_landscape_that_can_be_tablet_screens()
    {
        Assert.NotEmpty(TabletScreen.Presets);
        Assert.Equal(TabletScreen.Presets.Count, TabletScreen.Presets.Select(p => p.Label).Distinct().Count());
        foreach (var (label, width, height) in TabletScreen.Presets)
        {
            Assert.True(width > height, label);
            Assert.Null(TabletScreen.Problem(label[..Math.Min(label.Length, TabletScreen.MaxNameLength)], width, height, []));
        }
    }

    [Fact]
    public void The_editor_lists_tablet_screens_after_every_monitors_dashboard()
    {
        List<ExtraDashboard> extras = [new("strip", @"\\.\DISPLAY2", "Clock"), new("square", @"\\.\DISPLAY3", "Gaming")];
        List<TabletScreen> tablets = [Galaxy, Fire];

        Assert.Equal(0, EditorScreenList.IndexOf(null, extras, tablets));
        Assert.Equal(2, EditorScreenList.IndexOf("square", extras, tablets));
        Assert.Equal(3, EditorScreenList.IndexOf(Galaxy.Id, extras, tablets));
        Assert.Equal(4, EditorScreenList.IndexOf(Fire.Id, extras, tablets));
        // One that's gone is the main dashboard, as it always was.
        Assert.Equal(0, EditorScreenList.IndexOf("tablet:Gone", extras, tablets));
        Assert.Equal(0, EditorScreenList.IndexOf("Galaxy Tab", extras, tablets));

        Assert.Equal([null, "strip", "square", Galaxy.Id, Fire.Id, null], Enumerable.Range(0, 6).Select(i => EditorScreenList.At(i, extras, tablets)));
        Assert.Equal(1, EditorScreenList.IndexOf(Galaxy.Id, [], tablets));
        Assert.Equal(Galaxy.Id, EditorScreenList.At(1, [], tablets));
    }

    [Fact]
    public void Picking_a_tablet_fills_in_its_size_the_way_up_its_held() => Sta.Run(() =>
    {
        var window = new TabletScreenWindow([Galaxy], null);
        // A name of its own, and the first tablet's size, to start with.
        Assert.Equal("Tablet", window.NameBox.Text);
        Assert.Equal(1, window.PresetBox.SelectedIndex);
        Assert.Equal(("2560", "1600"), (window.WidthBox.Text, window.HeightBox.Text));
        Assert.Equal(("Tablet", 2560, 1600), window.Result());

        var fire = TabletScreen.Presets.ToList().FindIndex(p => p.Label == "Amazon Fire HD 8") + 1;
        window.PresetBox.SelectedIndex = fire;
        Assert.Equal(("1280", "800"), (window.WidthBox.Text, window.HeightBox.Text));

        // Turned to portrait, the sides swap; a tablet picked then comes portrait too.
        window.Portrait.IsChecked = true;
        Assert.Equal(("800", "1280"), (window.WidthBox.Text, window.HeightBox.Text));
        window.PresetBox.SelectedIndex = 1;
        Assert.Equal(("1600", "2560"), (window.WidthBox.Text, window.HeightBox.Text));
        Assert.Equal(("Tablet", 1600, 2560), window.Result());
        window.Close();
    });

    [Fact]
    public void A_size_typed_in_finds_its_tablet_and_way_up() => Sta.Run(() =>
    {
        var window = new TabletScreenWindow([], null);
        window.WidthBox.Text = "1200";
        window.HeightBox.Text = "1920";
        Assert.Equal(TabletScreen.Presets.ToList().FindIndex(p => p is (_, 1920, 1200)) + 1, window.PresetBox.SelectedIndex);
        Assert.True(window.Portrait.IsChecked);

        // One that isn't listed is another tablet; wider than it's tall, it's landscape.
        window.WidthBox.Text = "2345";
        Assert.Equal(0, window.PresetBox.SelectedIndex);
        Assert.True(window.Landscape.IsChecked);
        Assert.Equal(("Tablet", 2345, 1920), window.Result());

        window.WidthBox.Text = "wide";
        Assert.Equal(0, window.PresetBox.SelectedIndex);
        Assert.Equal("Write the width and height in pixels, like 2560 and 1600.", window.Problem());
        Assert.Null(window.Result());
        Assert.False(window.OkButton.IsEnabled);
        window.Close();
    });

    [Fact]
    public void Changing_a_tablet_screen_starts_from_it_and_can_keep_its_name() => Sta.Run(() =>
    {
        var window = new TabletScreenWindow([Galaxy], Fire);
        Assert.Equal("Change the tablet screen", window.Title);
        Assert.Equal("Save", window.OkButton.Content);
        Assert.Equal(("Kitchen Fire", "800", "1280"), (window.NameBox.Text, window.WidthBox.Text, window.HeightBox.Text));
        Assert.True(window.Portrait.IsChecked);
        Assert.Equal(("Kitchen Fire", 800, 1280), window.Result());

        window.NameBox.Text = "galaxy tab";
        Assert.Equal("There's already a tablet screen called \"galaxy tab\".", window.Problem());
        Assert.False(window.OkButton.IsEnabled);
        window.Close();
    });

    [Fact]
    public void A_screens_link_says_what_it_shows()
    {
        Assert.Contains("the tablet screen \"Galaxy Tab\"", TabletLinkWindow.IntroFor("Ignored", "Galaxy Tab"));
        Assert.Contains("the layout \"Clock\"", TabletLinkWindow.IntroFor("Clock"));
        Assert.DoesNotContain("\"", TabletLinkWindow.IntroFor(null));
    }
}
