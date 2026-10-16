using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using ImpiousBonum.App.Editor;
using ImpiousBonum.App.Layout;

namespace ImpiousBonum.App.Tests;

public sealed class GalleryTests
{
    [Fact]
    public void The_index_lists_entries_and_skips_unsafe_or_incomplete_ones()
    {
        var entries = Gallery.ParseIndex("""
            // comments are fine
            { "layouts": [
              { "name": "Good", "file": "good.ibl", "author": "Sam", "description": "Nice", "width": 1920, "height": 480 },
              { "name": "Bare", "file": "bare.json" },
              { "name": "Escape", "file": "../secrets.ibl" },
              { "name": "Elsewhere", "file": "https://example.com/x.ibl" },
              { "name": "Program", "file": "run.exe" },
              { "file": "nameless.ibl" },
            ] }
            """);

        Assert.Equal(["Good", "Bare"], entries.Select(e => e.Name));
        Assert.Equal(new GalleryEntry("Good", "good.ibl", "Sam", "Nice", 1920, 480), entries[0]);
        Assert.Equal("1920 × 480 · by Sam", entries[0].Details);
        Assert.Equal("", entries[1].Details);
        Assert.Empty(Gallery.ParseIndex("{}"));
    }

    [Theory]
    [InlineData("layout.ibl", true)]
    [InlineData("My Layout.json", true)]
    [InlineData("a/b.ibl", false)]
    [InlineData("a\\b.ibl", false)]
    [InlineData("..ibl", false)]
    [InlineData("c:x.ibl", false)]
    [InlineData("x.ibl?y", false)]
    [InlineData("x.zip", false)]
    public void Only_plain_file_names_are_allowed(string file, bool allowed) => Assert.Equal(allowed, Gallery.IsPlainFileName(file));

    [Fact]
    public void A_bare_layout_drops_paths_to_files_on_someone_elses_pc()
    {
        var json = """
            { "width": 800, "height": 480,
              "theme": { "base": "Ice", "fontFile": "C:\\Users\\sam\\font.ttf", "backgroundImage": "C:\\Users\\sam\\bg.png" },
              "widgets": [ { "type": "image", "file": "C:\\Users\\sam\\cat.png" }, { "type": "text" } ] }
            """;
        var package = Gallery.Open(new GalleryEntry("Shared", "shared.json", null, null, 0, 0), Encoding.UTF8.GetBytes(json));

        Assert.Equal("Shared", package.Name);
        Assert.Equal(800, package.Layout.Width);
        Assert.Null(ThemeLibrary.GetString(package.Layout.Theme, "fontFile"));
        Assert.Null(ThemeLibrary.GetString(package.Layout.Theme, "backgroundImage"));
        Assert.Null(package.Layout.Widgets[0]["file"]);
        Assert.Throws<InvalidDataException>(() => Gallery.Open(new GalleryEntry("Bad", "bad.json", null, null, 0, 0), "not json"u8.ToArray()));
    }

    [Fact]
    public void A_gallery_layout_leaves_out_font_names_that_are_addresses()
    {
        // Left out as it opens, since the gallery shows a layout as soon as it's picked, before it's added.
        var json = """
            { "theme": { "fontFamily": "\\\\nas\\fonts\\#Evil" },
              "widgets": [ { "type": "clock", "fontFamily": "file://nas/fonts/#Evil" }, { "type": "clock", "fontFamily": "Consolas" } ] }
            """;
        var bare = Gallery.Open(new GalleryEntry("Shared", "shared.json", null, null, 0, 0), Encoding.UTF8.GetBytes(json));

        Assert.Null(ThemeLibrary.GetString(bare.Layout.Theme, "fontFamily"));
        Assert.Null(bare.Layout.Widgets[0]["fontFamily"]);
        Assert.Equal("Consolas", bare.Layout.Widgets[1].GetString("fontFamily"));
        Assert.Equal(2, bare.Skipped.Count);

        // A package from the gallery too.
        using var stream = new MemoryStream();
        LayoutPackage.Write(new LayoutDocument
        {
            Theme = new JsonObject { ["fontFamily"] = "file://nas/fonts/#Evil" },
            Widgets = [new JsonObject { ["type"] = "clock", ["fontFamily"] = "\\\\nas\\fonts\\#Evil" }],
        }, "Shared", stream);
        var package = Gallery.Open(new GalleryEntry("Shared", "shared.ibl", null, null, 0, 0), stream.ToArray());

        Assert.Equal("Segoe UI", new ThemeLibrary(null).Resolve(package.Layout.Theme).FontFamily);
        Assert.Null(package.Layout.Widgets[0]["fontFamily"]);
        Assert.Equal(2, package.Skipped.Count(s => s.Contains("isn't the name of a font")));
    }

    [Fact]
    public void A_package_is_named_after_its_gallery_entry()
    {
        using var stream = new MemoryStream();
        LayoutPackage.Write(new LayoutDocument { Widgets = [new JsonObject { ["type"] = "clock" }] }, "Old name", stream);
        var package = Gallery.Open(new GalleryEntry("New name", "x.ibl", null, null, 0, 0), stream.ToArray());

        Assert.Equal("New name", package.Name);
        Assert.Equal("clock", package.Layout.Widgets[0].GetString("type"));
    }

    [Fact]
    public void Every_layout_in_the_repository_gallery_opens_and_is_valid()
    {
        var folder = RepositoryGallery();
        var entries = Gallery.ParseIndex(File.ReadAllText(Path.Combine(folder, Gallery.IndexFile)));
        Assert.NotEmpty(entries);
        Assert.Equal(entries.Count, entries.Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var entry in entries)
        {
            var path = Path.Combine(folder, entry.File);
            Assert.True(File.Exists(path), $"{entry.Name}: {entry.File} isn't in the gallery folder.");
            var package = Gallery.Open(entry, File.ReadAllBytes(path));
            Assert.Equal(entry.Name, package.Name);
            Assert.Equal((entry.Width, entry.Height), (package.Layout.Width, package.Layout.Height));
            Assert.True(entry.Description is { Length: > 0 }, $"{entry.Name} has no description.");
            Assert.Empty(LayoutValidator.Validate(package.Layout));
            // What the gallery's "how much it shows" filter goes by.
            Assert.Equal(package.Layout.Widgets.Count(w => !GalleryFit.Decorations.Contains(w.GetString("type") ?? "")), entry.Widgets);
        }
    }

    [Fact]
    public void Layouts_suit_screens_of_their_shape_closest_in_size_first()
    {
        GalleryEntry Entry(string name, int width, int height, int widgets = 0) => new(name, $"{name}.json", null, null, width, height, widgets);
        var strip = Entry("Strip", 1920, 480);
        var hd = Entry("HD", 1920, 1080, 12);
        var uhd = Entry("4K", 3840, 2160, 17);
        var tv = Entry("TV", 1920, 1080, 4);
        var small = Entry("Small", 800, 480, 4);
        var portrait = Entry("Portrait", 1080, 1920, 9);
        GalleryEntry[] all = [strip, hd, uhd, tv, small, portrait];

        // A 4K TV: the 16:9 layouts, the 4K one first, and 800 × 480 (5:3) is near enough; not the strip or the portrait one.
        Assert.Equal([uhd, hd, tv, small], GalleryFit.For(all, (3840, 2160), GalleryDetail.Any));
        // The same TV across the room: only the simple ones.
        Assert.Equal([tv, small], GalleryFit.For(all, (3840, 2160), GalleryDetail.Few));
        Assert.Equal([strip], GalleryFit.For(all, (1920, 480), GalleryDetail.Any));
        Assert.Equal([portrait], GalleryFit.For(all, (1080, 1920), GalleryDetail.Lots));
        // Any screen: everything, in the gallery's order; a layout without a count shows whatever the filter.
        Assert.Equal(all, GalleryFit.For(all, null, GalleryDetail.Any));
        Assert.Equal([strip, hd, uhd, portrait], GalleryFit.For(all, null, GalleryDetail.Lots));
    }

    [Fact]
    public void Gallery_layouts_build_on_a_dashboard()
    {
        Sta.Run(() =>
        {
            var folder = RepositoryGallery();
            var view = new DashboardView();
            foreach (var entry in Gallery.ParseIndex(File.ReadAllText(Path.Combine(folder, Gallery.IndexFile))))
            {
                var package = Gallery.Open(entry, File.ReadAllBytes(Path.Combine(folder, entry.File)));
                view.Build(package.Layout, new ThemeLibrary(null).Resolve(package.Layout.Theme));
                view.Refresh(new Core.Metrics.MetricStore(), DateTime.Now);
            }
        });
    }

    [Fact]
    public void The_index_lists_themes_and_skips_unsafe_or_unusable_ones()
    {
        var json = """
            { "layouts": [ { "name": "Strip", "file": "strip.json" } ],
              "themes": [
                { "name": "Deep Sea", "file": "deep-sea.json", "author": "Sam", "description": "Teal" },
                { "name": "Bare", "file": "bare.json" },
                { "name": "Escape", "file": "../secrets.json" },
                { "name": "Package", "file": "theme.ibl" },
                { "name": "CON", "file": "con.json" },
                { "name": "Half/way", "file": "half.json" },
                { "file": "nameless.json" },
              ] }
            """;

        var themes = Gallery.ParseThemes(json);

        Assert.Equal(["Deep Sea", "Bare"], themes.Select(t => t.Name));
        Assert.Equal(new GalleryTheme("Deep Sea", "deep-sea.json", "Sam", "Teal"), themes[0]);
        Assert.Equal("by Sam", themes[0].Details);
        Assert.Equal("", themes[1].Details);
        // The layouts beside them are read as ever.
        Assert.Equal(["Strip"], Gallery.ParseIndex(json).Select(e => e.Name));
    }

    [Fact]
    public void An_index_from_before_themes_has_none()
    {
        var old = """{ "layouts": [ { "name": "Strip", "file": "strip.json" } ] }""";

        Assert.Empty(Gallery.ParseThemes(old));
        Assert.Single(Gallery.ParseIndex(old));
        Assert.Empty(Gallery.ParseThemes("""{ "themes": "not a list" }"""));
    }

    [Fact]
    public void A_shared_theme_leaves_out_files_its_base_and_settings_it_doesnt_know()
    {
        var theme = Gallery.OpenTheme(Encoding.UTF8.GetBytes("""
            // A theme from someone else.
            { "base": "Ice", "Accent": "#2EF2D0", "background": "#020A18", "glow": 0.5,
              "fontFile": "C:\\Users\\sam\\font.ttf", "backgroundImage": "\\\\nas\\pictures\\sea.png", "cornerRadius": 8, }
            """));

        Assert.Equal(["accent", "background", "glow"], theme.Select(p => p.Key));
        Assert.Equal("#2EF2D0", ThemeLibrary.GetString(theme, "accent"));

        // Notepad's byte order mark is fine too.
        Assert.Single(Gallery.OpenTheme([.. "\uFEFF"u8, .. """{ "accent": "#2EF2D0" }"""u8]));
    }

    [Theory]
    [InlineData("""{ "accent": "teal-ish" }""", "'accent' should be a colour")]
    [InlineData("""{ "accent2": "#12" }""", "'accent2' should be a colour")]
    [InlineData("""{ "glow": 3 }""", "'glow' should be between 0 and 1")]
    [InlineData("""{ "glow": "lots" }""", "'glow' should be a number")]
    [InlineData("""{ "foreground": 255 }""", "'foreground' should be text")]
    [InlineData("""{ "backgroundFit": "zoom" }""", "'backgroundFit' should be one of")]
    [InlineData("""{ "fontFamily": "file://nas/fonts/#Evil" }""", "'fontFamily' should be the name of a font")]
    [InlineData("""[ "accent", "#FF9800" ]""", "should be a { … } object")]
    [InlineData("""{ "accent": """, "can't be read")]
    public void A_shared_theme_with_a_value_that_wont_work_is_refused(string json, string why)
    {
        var problem = Assert.Throws<InvalidDataException>(() => Gallery.OpenTheme(Encoding.UTF8.GetBytes(json)));
        Assert.Contains(why, problem.Message);
    }

    [Fact]
    public void Every_theme_in_the_repository_gallery_opens_unchanged_and_has_a_picture()
    {
        var folder = RepositoryGallery();
        var themes = Gallery.ParseThemes(File.ReadAllText(Path.Combine(folder, Gallery.IndexFile)));
        Assert.NotEmpty(themes);
        Assert.Equal(themes.Count, themes.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var theme in themes)
        {
            Assert.False(ThemeLibrary.IsBuiltIn(theme.Name), $"{theme.Name} has a built-in theme's name.");
            Assert.True(theme.Description is { Length: > 0 }, $"{theme.Name} has no description.");
            var path = Path.Combine(folder, "themes", theme.File);
            Assert.True(File.Exists(path), $"{theme.Name}: {theme.File} isn't in the gallery's themes folder.");

            // Nothing in it is left out: no files, no settings the app doesn't know.
            var values = Gallery.OpenTheme(File.ReadAllBytes(path));
            var written = (JsonObject)JsonNode.Parse(File.ReadAllText(path), documentOptions: new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip })!;
            Assert.Equal(written.ToJsonString(), values.ToJsonString());

            var picture = Path.Combine(folder, "images", "themes", Path.ChangeExtension(theme.File, ".png"));
            Assert.True(File.Exists(picture), $"{theme.Name} has no picture in images/themes.");
        }
    }

    [Fact]
    public void A_gallery_theme_named_like_one_of_yours_or_a_built_in_one_gets_a_name_of_its_own()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ImpiousBonum.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var themes = new ThemeLibrary(directory);
            var deepSea = new JsonObject { ["accent"] = "#2EF2D0" };
            themes.Save("Deep Sea", deepSea);

            // A built-in theme can't be replaced, so its name is taken; one of yours can be, or kept beside it.
            Assert.True(ThemeLibrary.IsBuiltIn(themes.Find("ice")));
            Assert.Throws<ArgumentException>(() => themes.Save("Ice", deepSea));
            Assert.Equal("Ice (2)", LayoutTransfer.SuggestName(themes, "Ice"));
            Assert.Equal("Deep Sea", themes.Find("deep sea"));
            Assert.False(ThemeLibrary.IsBuiltIn(themes.Find("deep sea")));
            Assert.Equal("Deep Sea (2)", LayoutTransfer.SuggestName(themes, "Deep Sea"));

            themes.Save("deep sea", new JsonObject { ["accent"] = "#3E8BFF" });
            Assert.Equal(["Deep Sea"], themes.ListSaved());
            Assert.Equal("#3E8BFF", ThemeLibrary.GetString(themes.Load("Deep Sea"), "accent"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The themes in the repository's gallery, by name, as the gallery opens them.</summary>
    internal static IEnumerable<(string Name, JsonObject Values)> RepositoryThemes()
    {
        var folder = RepositoryGallery();
        return Gallery.ParseThemes(File.ReadAllText(Path.Combine(folder, Gallery.IndexFile)))
            .Select(t => (t.Name, Gallery.OpenTheme(File.ReadAllBytes(Path.Combine(folder, "themes", t.File)))));
    }

    /// <summary>The repository's gallery folder, found by walking up from the test's output folder.</summary>
    internal static string RepositoryGallery()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "gallery");
            if (File.Exists(Path.Combine(candidate, Gallery.IndexFile)))
                return candidate;
        }
        throw new DirectoryNotFoundException("The repository's gallery folder wasn't found.");
    }
}
