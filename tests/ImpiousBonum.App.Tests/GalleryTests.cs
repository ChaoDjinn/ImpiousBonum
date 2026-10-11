using System.IO;
using System.Text;
using System.Text.Json.Nodes;
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
        }
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

    /// <summary>The repository's gallery folder, found by walking up from the test's output folder.</summary>
    private static string RepositoryGallery()
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
