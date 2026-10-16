using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImpiousBonum.App.Editor;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Tests;

// Shares AppPaths (a static) with the layout store tests, so they mustn't run in parallel.
[Collection("AppPaths")]
public sealed class ThemeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ImpiousBonum.Tests", Guid.NewGuid().ToString("N"));

    public ThemeTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private ThemeLibrary Library() => new(_directory);

    private static LayoutDocument Parse(string json) => JsonSerializer.Deserialize<LayoutDocument>(json, JsonDefaults.Options)!;

    private static string Serialize(LayoutDocument layout) => JsonSerializer.Serialize(layout, JsonDefaults.Options);

    [Fact]
    public void An_inline_theme_loads_and_saves_unchanged()
    {
        var layout = Parse("""{ "theme": { "fontFamily": "Bahnschrift", "accent": "#00FF00", "track": "#222222" }, "widgets": [] }""");

        var theme = Library().Resolve(layout.Theme);
        Assert.Equal("Bahnschrift", theme.FontFamily);
        Assert.Equal("#00FF00", theme.Accent);
        Assert.Equal(new ThemeSettings().Background, theme.Background);

        var saved = JsonNode.Parse(Serialize(layout))!["theme"]!.AsObject();
        Assert.Equal(new[] { "fontFamily", "accent", "track" }, saved.Select(p => p.Key));
    }

    [Fact]
    public void A_theme_name_is_short_for_a_base_and_is_written_back_the_same_way()
    {
        var layout = Parse("""{ "theme": "Ice", "widgets": [] }""");
        Assert.Equal("Ice", ThemeLibrary.BaseName(layout.Theme));
        Assert.Equal("Ice", JsonNode.Parse(Serialize(layout))!["theme"]!.GetValue<string>());

        var empty = Parse("""{ "theme": null }""");
        Assert.Empty(empty.Theme);
    }

    [Fact]
    public void Inline_values_override_the_named_theme()
    {
        var layout = Parse("""{ "theme": { "base": "ice", "accent": "#00FF00", "Track": "#123456" } }""");
        var ice = ThemeSettings.Merge(Library().Load("Ice"), null);

        var theme = Library().Resolve(layout.Theme);

        Assert.Equal("#00FF00", theme.Accent);
        Assert.Equal("#123456", theme.Track);
        Assert.Equal(ice.Background, theme.Background);
        Assert.NotEqual(new ThemeSettings().Background, theme.Background);
    }

    [Fact]
    public void Built_in_themes_are_valid_and_can_be_picked_but_not_overwritten()
    {
        var library = Library();
        Assert.True(ThemeLibrary.BuiltInNames.Count >= 3);
        Assert.Contains("Orange", ThemeLibrary.BuiltInNames);

        foreach (var name in ThemeLibrary.BuiltInNames)
            Assert.Empty(LayoutValidator.Validate(new LayoutDocument { Theme = new JsonObject { ["base"] = name } }, library));

        // The built-in orange theme is the look a layout has with no theme at all.
        Assert.Equal(JsonSerializer.Serialize(new ThemeSettings()), JsonSerializer.Serialize(library.Resolve(new JsonObject { ["base"] = "Orange" })));

        Assert.Throws<ArgumentException>(() => library.Save("orange", new JsonObject { ["accent"] = "#00FF00" }));
        Assert.Throws<ArgumentException>(() => library.Delete("Ice"));
        Assert.NotNull(library.CheckNewName("ICE"));

        // Save as copies instead.
        library.Save("Orange copy", library.Load("Orange"));
        Assert.Equal(new[] { "Orange copy" }, library.ListSaved());
        Assert.Equal(library.Load("Orange").ToJsonString(), library.Load("Orange copy").ToJsonString());
    }

    [Fact]
    public void Built_in_themes_set_every_colour_and_stay_readable()
    {
        var library = Library();
        string[] keys = ["fontFamily", "fontWeight", "foreground", "secondary", "accent", "background", "track", "warning", "critical"];

        foreach (var name in ThemeLibrary.BuiltInNames)
        {
            // Every value set, so none quietly falls back to the orange defaults.
            var values = library.Load(name);
            foreach (var key in keys)
                Assert.True(ThemeLibrary.GetString(values, key) is not null, $"{name} doesn't set {key}.");

            // WCAG contrast against the background: 4.5:1 for text, 3:1 for the rest (large text and graphics).
            var theme = library.Resolve(new JsonObject { ["base"] = name });
            Assert.True(Contrast(theme.Foreground, theme.Background) >= 4.5, $"{name}: foreground is hard to read.");
            foreach (var (key, value) in new[] { ("secondary", theme.Secondary), ("accent", theme.Accent), ("warning", theme.Warning), ("critical", theme.Critical) })
                Assert.True(Contrast(value, theme.Background) >= 3, $"{name}: {key} is hard to see.");

            // A gradient's second colours have to hold up too: the accent's end on the background, everything on its bottom.
            if (theme.Accent2 is { } accent2)
                Assert.True(Contrast(accent2, theme.Background) >= 3, $"{name}: accent2 is hard to see.");
            if (theme.BackgroundGradient is { } bottom)
            {
                Assert.True(Contrast(theme.Foreground, bottom) >= 4.5, $"{name}: foreground is hard to read at the bottom.");
                foreach (var (key, value) in new[] { ("secondary", theme.Secondary), ("accent", theme.Accent), ("accent2", theme.Accent2 ?? theme.Accent), ("warning", theme.Warning), ("critical", theme.Critical) })
                    Assert.True(Contrast(value, bottom) >= 3, $"{name}: {key} is hard to see at the bottom.");
            }
        }
    }

    [Fact]
    public void Clear_Sight_keeps_warning_and_critical_apart_from_each_other_and_the_normal_colours_in_colour_blindness()
    {
        var theme = Library().Resolve(new JsonObject { ["base"] = "Clear Sight" });
        Assert.True(theme.WarningSymbols);

        foreach (var (vision, matrix) in Visions)
        {
            (string Name, (double L, double A, double B) Lab)[] colours =
                [.. new[] { ("warning", theme.Warning), ("critical", theme.Critical), ("accent", theme.Accent), ("foreground", theme.Foreground) }
                    .Select(c => (c.Item1, Lab(Simulate(c.Item2, matrix))))];
            // Warning and critical against each other and against the normal colours of graphs and text. A CIEDE2000
            // difference of 20 is plainly two different colours.
            foreach (var (first, second) in new[] { (0, 1), (0, 2), (0, 3), (1, 2), (1, 3) })
            {
                var difference = DeltaE(colours[first].Lab, colours[second].Lab);
                Assert.True(difference >= 20, $"With {vision}, {colours[first].Name} and {colours[second].Name} look alike ({difference:0.0}).");
            }
            // One much lighter than the other too, which holds on a dim screen or with no colour at all.
            Assert.True(Math.Abs(colours[0].Lab.L - colours[1].Lab.L) >= 20, $"With {vision}, warning and critical are as light as each other.");
        }
    }

    [Theory]
    // Examples from Sharma, Wu and Dalal's notes on CIEDE2000 (2005).
    [InlineData(50, 2.6772, -79.7751, 50, 0, -82.7485, 2.0425)]
    [InlineData(50, 2.5, 0, 73, 25, -18, 27.1492)]
    [InlineData(50, 2.5, 0, 50, 0, -2.5, 4.3065)]
    [InlineData(60.2574, -34.0099, 36.2677, 60.4626, -34.1751, 39.4387, 1.2644)]
    public void The_colour_difference_matches_its_published_examples(double l1, double a1, double b1, double l2, double a2, double b2, double expected) =>
        Assert.Equal(expected, DeltaE((l1, a1, b1), (l2, a2, b2)), 4);

    /// <summary>
    /// How colours look with each kind of colour blindness, as matrices on linear RGB: Machado, Oliveira and Fernandes
    /// (2009), at full strength, for people with no red (protanopia), green (deuteranopia) or blue (tritanopia) cones.
    /// </summary>
    private static readonly (string Vision, double[,] Matrix)[] Visions =
    [
        ("normal vision", new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } }),
        ("protanopia", new[,] { { 0.152286, 1.052583, -0.204868 }, { 0.114503, 0.786281, 0.099216 }, { -0.003882, -0.048116, 1.051998 } }),
        ("deuteranopia", new[,] { { 0.367322, 0.860646, -0.227968 }, { 0.280085, 0.672501, 0.047413 }, { -0.011820, 0.042940, 0.968881 } }),
        ("tritanopia", new[,] { { 1.255528, -0.076749, -0.178779 }, { -0.078411, 0.930809, 0.147602 }, { 0.004733, 0.691367, 0.303900 } }),
    ];

    /// <summary>A colour as someone with <paramref name="vision"/>'s matrix sees it, in linear RGB.</summary>
    private static double[] Simulate(string color, double[,] vision)
    {
        var parsed = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color);
        static double Linear(byte channel)
        {
            var c = channel / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        double[] rgb = [Linear(parsed.R), Linear(parsed.G), Linear(parsed.B)];
        return [.. Enumerable.Range(0, 3).Select(row => Math.Clamp(vision[row, 0] * rgb[0] + vision[row, 1] * rgb[1] + vision[row, 2] * rgb[2], 0, 1))];
    }

    /// <summary>Linear RGB in CIELAB, under daylight (D65).</summary>
    private static (double L, double A, double B) Lab(double[] rgb)
    {
        var x = (0.4124 * rgb[0] + 0.3576 * rgb[1] + 0.1805 * rgb[2]) / 0.95047;
        var y = 0.2126 * rgb[0] + 0.7152 * rgb[1] + 0.0722 * rgb[2];
        var z = (0.0193 * rgb[0] + 0.1192 * rgb[1] + 0.9505 * rgb[2]) / 1.08883;
        static double F(double t) => t > Math.Pow(6.0 / 29, 3) ? Math.Cbrt(t) : t / (3 * Math.Pow(6.0 / 29, 2)) + 4.0 / 29;
        return (116 * F(y) - 16, 500 * (F(x) - F(y)), 200 * (F(y) - F(z)));
    }

    /// <summary>The CIEDE2000 difference between two colours: about 1 is just noticeable, 20 or more plainly different.</summary>
    private static double DeltaE((double L, double A, double B) first, (double L, double A, double B) second)
    {
        static double Radians(double degrees) => degrees * Math.PI / 180;
        static double Hue(double b, double a) => a == 0 && b == 0 ? 0 : (Math.Atan2(b, a) * 180 / Math.PI + 360) % 360;
        static double Seventh(double c) => Math.Sqrt(Math.Pow(c, 7) / (Math.Pow(c, 7) + Math.Pow(25, 7)));

        var g = 0.5 * (1 - Seventh((Math.Sqrt(first.A * first.A + first.B * first.B) + Math.Sqrt(second.A * second.A + second.B * second.B)) / 2));
        var (a1, a2) = (first.A * (1 + g), second.A * (1 + g));
        var (c1, c2) = (Math.Sqrt(a1 * a1 + first.B * first.B), Math.Sqrt(a2 * a2 + second.B * second.B));
        var (h1, h2) = (Hue(first.B, a1), Hue(second.B, a2));

        var dh = c1 * c2 == 0 ? 0 : h2 - h1 > 180 ? h2 - h1 - 360 : h2 - h1 < -180 ? h2 - h1 + 360 : h2 - h1;
        var (dL, dC, dH) = (second.L - first.L, c2 - c1, 2 * Math.Sqrt(c1 * c2) * Math.Sin(Radians(dh / 2)));
        var (l, c) = ((first.L + second.L) / 2, (c1 + c2) / 2);
        var h = c1 * c2 == 0 ? h1 + h2 : Math.Abs(h1 - h2) <= 180 ? (h1 + h2) / 2 : h1 + h2 < 360 ? (h1 + h2 + 360) / 2 : (h1 + h2 - 360) / 2;

        var t = 1 - 0.17 * Math.Cos(Radians(h - 30)) + 0.24 * Math.Cos(Radians(2 * h)) + 0.32 * Math.Cos(Radians(3 * h + 6)) - 0.20 * Math.Cos(Radians(4 * h - 63));
        var sL = 1 + 0.015 * Math.Pow(l - 50, 2) / Math.Sqrt(20 + Math.Pow(l - 50, 2));
        var sC = 1 + 0.045 * c;
        var sH = 1 + 0.015 * c * t;
        var rT = -2 * Seventh(c) * Math.Sin(Radians(60 * Math.Exp(-Math.Pow((h - 275) / 25, 2))));
        return Math.Sqrt(Math.Pow(dL / sL, 2) + Math.Pow(dC / sC, 2) + Math.Pow(dH / sH, 2) + rT * (dC / sC) * (dH / sH));
    }

    /// <summary>The WCAG contrast ratio of a colour (blended over the background if translucent) against the background.</summary>
    private static double Contrast(string color, string background)
    {
        var fore = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color);
        var back = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(background);
        var alpha = fore.A / 255.0;
        double Blend(byte f, byte b) => f * alpha + b * (1 - alpha);
        var lighter = Luminance(Blend(fore.R, back.R), Blend(fore.G, back.G), Blend(fore.B, back.B));
        var darker = Luminance(back.R, back.G, back.B);
        if (lighter < darker)
            (lighter, darker) = (darker, lighter);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(double r, double g, double b)
    {
        static double Linear(double channel)
        {
            channel /= 255;
            return channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);
    }

    [Fact]
    public void A_file_named_like_a_built_in_theme_is_ignored()
    {
        var library = Library();
        library.EnsureCreated();
        File.WriteAllText(Path.Combine(library.Directory!, "Ice.json"), """{ "accent": "#010203" }""");

        Assert.Empty(library.ListSaved());
        Assert.NotEqual("#010203", library.Resolve(new JsonObject { ["base"] = "Ice" }).Accent);
    }

    [Fact]
    public void Changing_a_saved_theme_restyles_every_layout_that_uses_it()
    {
        var library = Library();
        library.Save("Neon", new JsonObject { ["accent"] = "#FF00FF" });
        var first = Parse("""{ "theme": "Neon" }""");
        var second = Parse("""{ "theme": { "base": "Neon", "foreground": "#EEEEEE" } }""");

        library.Save("neon", new JsonObject { ["accent"] = "#00FFFF" });

        Assert.Equal(new[] { "Neon" }, library.ListSaved());
        Assert.Equal("#00FFFF", library.Resolve(first.Theme).Accent);
        Assert.Equal("#00FFFF", library.Resolve(second.Theme).Accent);
        Assert.Equal("#EEEEEE", library.Resolve(second.Theme).Foreground);
    }

    [Fact]
    public void Validator_reports_unknown_theme_names_and_settings()
    {
        var library = Library();
        var unknown = Assert.Single(LayoutValidator.Validate(Parse("""{ "theme": "Neon" }"""), library));
        Assert.Contains("no theme called \"Neon\"", unknown);

        library.Save("Neon", new JsonObject { ["accent"] = "#FF00FF" });
        Assert.Empty(LayoutValidator.Validate(Parse("""{ "theme": "neon" }"""), library));

        var typo = Assert.Single(LayoutValidator.Validate(Parse("""{ "theme": { "acent": "#FF00FF" } }"""), library));
        Assert.Contains("did you mean 'accent'", typo);

        Assert.Contains(LayoutValidator.Validate(Parse("""{ "theme": { "backgroundOpacity": "half" } }"""), library), i => i.StartsWith("Theme:"));

        File.WriteAllText(library.PathOf("Broken"), "{ not json");
        Assert.Contains(LayoutValidator.Validate(Parse("""{ "theme": "Broken" }"""), library), i => i.Contains("couldn't read the theme"));
    }

    [Fact]
    public void Editor_can_pick_detach_and_save_themes()
    {
        var session = new LayoutSession(Parse("""{ "theme": { "accent": "#00FF00" } }"""), Library());

        session.UseTheme("Terminal");
        Assert.Equal("Terminal", session.ThemeName);
        Assert.False(session.HasThemeOverrides);
        var terminal = session.ResolvedTheme;
        Assert.NotEqual("#00FF00", terminal.Accent);

        // Unset settings show the theme's value; resetting one goes back to it.
        Assert.Equal(terminal.FontFamily, session.GetResolvedThemeValue("fontFamily")!.GetValue<string>());
        session.SetValue(SettingTarget.Theme, "accent", JsonValue.Create("#ABCDEF"), null);
        Assert.Equal("#ABCDEF", session.ResolvedTheme.Accent);
        Assert.True(session.HasThemeOverrides);
        session.SetValue(SettingTarget.Theme, "accent", null, null);
        Assert.Equal(terminal.Accent, session.ResolvedTheme.Accent);

        session.DetachTheme();
        Assert.Null(session.ThemeName);
        Assert.Equal(JsonSerializer.Serialize(terminal), JsonSerializer.Serialize(session.ResolvedTheme));

        session.SetValue(SettingTarget.Theme, "accent", JsonValue.Create("#ABCDEF"), null);
        session.SaveThemeAs("Mine");
        Assert.Equal("Mine", session.ThemeName);
        Assert.False(session.HasThemeOverrides);
        Assert.Equal("#ABCDEF", session.ResolvedTheme.Accent);
        Assert.Equal("#ABCDEF", Library().Resolve(new JsonObject { ["base"] = "Mine" }).Accent);

        session.Undo();
        Assert.Null(session.ThemeName);
    }

    [Fact]
    public void Game_links_can_name_a_theme()
    {
        AppPaths.UseDataDirectory(_directory);
        File.WriteAllText(AppPaths.SettingsFile, """
            {
              "gameLayouts": [
                { "process": "eldenring", "theme": "Elden Ring" },
                { "process": "doom", "layout": "Shooter", "theme": "Ice" },
                { "process": "nothing" },
              ],
            }
            """);

        var settings = AppSettings.Load();

        Assert.Equal(new[] { new GameLayoutRule("eldenring", null, "Elden Ring"), new GameLayoutRule("doom", "Shooter", "Ice") }, settings.GameLayouts);
        Assert.Equal("theme Elden Ring", settings.GameLayouts[0].Describe());
        Assert.Equal("Shooter, theme Ice", settings.GameLayouts[1].Describe());
        var saved = JsonSerializer.Serialize(settings, JsonDefaults.Options);
        Assert.DoesNotContain("\"layout\": null", saved);

        // Switching from one game's theme to another's counts as a change even with no layouts involved.
        var switcher = new GameLayoutSwitcher(TimeSpan.Zero);
        var now = DateTime.UtcNow;
        Assert.True(switcher.Update([new GameLayoutRule("a", null, "Ice"), new GameLayoutRule("b", null, "Terminal")], "a", now));
        Assert.True(switcher.Update([new GameLayoutRule("a", null, "Ice"), new GameLayoutRule("b", null, "Terminal")], "b", now));
        Assert.Equal("Terminal", switcher.Current?.Theme);
    }

    [Fact]
    public void A_game_theme_restyles_the_layout_without_being_remembered()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            using var store = new LayoutStore(new AppSettings());
            var layout = Parse("""{ "theme": { "accent": "#00FF00" } }""");
            var changes = 0;
            store.ThemeChanged += (_, _) => changes++;

            store.ShowThemeTemporarily("ice");
            Assert.Equal("Ice", store.ThemeOverride);
            Assert.Equal(store.Themes.Resolve(new JsonObject { ["base"] = "Ice" }).Accent, store.ResolveTheme(layout).Accent);

            store.ShowThemeTemporarily(null);
            Assert.Equal("#00FF00", store.ResolveTheme(layout).Accent);
            Assert.Equal(2, changes);

            Assert.Throws<FileNotFoundException>(() => store.ShowThemeTemporarily("Missing"));
            Assert.Null(store.ThemeOverride);
        });
    }
}
