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
