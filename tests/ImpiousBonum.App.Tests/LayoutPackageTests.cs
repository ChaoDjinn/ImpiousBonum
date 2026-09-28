using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using ImpiousBonum.App.Layout;

namespace ImpiousBonum.App.Tests;

public sealed class LayoutPackageTests : IDisposable
{
    private static readonly byte[] FontBytes = Encoding.ASCII.GetBytes("not really a font");
    private static readonly byte[] ImageBytes = Encoding.ASCII.GetBytes("not really a picture");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ImpiousBonum.Tests", Guid.NewGuid().ToString("N"));

    public LayoutPackageTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    /// <summary>A clean profile, like running with --data-dir on another PC.</summary>
    private LayoutLibrary NewProfile(string name)
    {
        var library = new LayoutLibrary(Path.Combine(_root, name));
        library.EnsureSeeded();
        return library;
    }

    private LayoutDocument LayoutWithFiles()
    {
        var source = Path.Combine(_root, "my files");
        Directory.CreateDirectory(source);
        var font = Path.Combine(source, "Fancy Font.TTF");
        var image = Path.Combine(source, "wallpaper.jpg");
        File.WriteAllBytes(font, FontBytes);
        File.WriteAllBytes(image, ImageBytes);
        return new LayoutDocument
        {
            Width = 800,
            Height = 200,
            Theme = new JsonObject { ["fontFamily"] = "Fancy", ["fontFile"] = font, ["backgroundImage"] = image, ["backgroundFit"] = "tile", ["accent"] = "#123456" },
            Widgets = [new JsonObject { ["type"] = "text", ["x"] = 1, ["y"] = 2, ["width"] = 100, ["height"] = 50, ["text"] = "{hw/some-sensor-only-I-have}" }],
        };
    }

    private static ThemeSettings Resolved(LayoutDocument layout) => new ThemeLibrary(null).Resolve(layout.Theme);

    private static MemoryStream Pack(LayoutDocument layout, string name, out IReadOnlyList<string> warnings)
    {
        var stream = new MemoryStream();
        warnings = LayoutPackage.Write(layout, name, stream);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream Zip(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(content);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private const string MinimalLayout = "{ \"width\": 300, \"height\": 100, \"widgets\": [] }";

    [Fact]
    public void Export_then_import_on_a_clean_profile_reproduces_the_layout_with_its_font_and_image()
    {
        var original = LayoutWithFiles();
        using var package = Pack(original, "Shared", out var warnings);
        Assert.Empty(warnings);

        var contents = LayoutPackage.Read(package, "fallback");
        var other = NewProfile("other pc");
        var name = other.Import(contents, contents.Name);

        Assert.Equal("Shared", name);
        Assert.Contains("Shared", other.List());
        var imported = other.Load(name);
        Assert.Equal(800, imported.Width);
        Assert.Equal(200, imported.Height);
        Assert.Equal("tile", Resolved(imported).BackgroundFit);
        Assert.Equal("#123456", Resolved(imported).Accent);

        var folder = Path.Combine(other.Directory, "Shared");
        Assert.Equal(Path.Combine(folder, "font.ttf"), Resolved(imported).FontFile);
        Assert.Equal(Path.Combine(folder, "background.jpg"), Resolved(imported).BackgroundImage);
        Assert.Equal(FontBytes, File.ReadAllBytes(Resolved(imported).FontFile!));
        Assert.Equal(ImageBytes, File.ReadAllBytes(Resolved(imported).BackgroundImage!));

        // Metric ids the other PC doesn't have come through untouched; the widget shows "—" for them as usual.
        Assert.Equal("{hw/some-sensor-only-I-have}", (string?)imported.Widgets[0]["text"]);
        Assert.Empty(LayoutValidator.Validate(imported));
    }

    [Fact]
    public void Exported_paths_are_relative_to_the_package()
    {
        var original = LayoutWithFiles();
        using var package = Pack(original, "Shared", out _);
        using var zip = new ZipArchive(package, ZipArchiveMode.Read);

        var names = zip.Entries.Select(e => e.FullName).Order().ToList();
        Assert.Equal(new[] { "assets/background.jpg", "assets/font.ttf", "layout.json", "manifest.json" }, names);

        using var reader = new StreamReader(zip.GetEntry("layout.json")!.Open());
        var theme = JsonNode.Parse(reader.ReadToEnd())!["theme"]!;
        Assert.Equal("assets/font.ttf", (string?)theme["fontFile"]);
        Assert.Equal("assets/background.jpg", (string?)theme["backgroundImage"]);

        // The layout being exported is left alone.
        Assert.EndsWith("Fancy Font.TTF", Resolved(original).FontFile);
    }

    [Fact]
    public void A_layout_using_a_saved_theme_carries_the_theme_with_it()
    {
        var files = LayoutWithFiles();
        var themes = new ThemeLibrary(Path.Combine(_root, "mine"));
        themes.Save("Neon", files.Theme);
        var layout = new LayoutDocument { Theme = new JsonObject { ["base"] = "Neon", ["foreground"] = "#EEEEEE" } };

        var package = new MemoryStream();
        LayoutPackage.Write(layout, "Shared", package, themes);
        package.Position = 0;
        var contents = LayoutPackage.Read(package, "x");

        // The other PC has no "Neon": its values come along, with the font and image packed.
        Assert.Null(ThemeLibrary.BaseName(contents.Layout.Theme));
        Assert.Equal("#123456", Resolved(contents.Layout).Accent);
        Assert.Equal("#EEEEEE", Resolved(contents.Layout).Foreground);
        Assert.Equal(FontBytes, contents.Font!.Data);
        Assert.Equal(ImageBytes, contents.Background!.Data);

        // Built-in themes exist everywhere, so they stay as names.
        var builtIn = new MemoryStream();
        LayoutPackage.Write(new LayoutDocument { Theme = new JsonObject { ["base"] = "Ice" } }, "Cool", builtIn, themes);
        builtIn.Position = 0;
        Assert.Equal("Ice", ThemeLibrary.BaseName(LayoutPackage.Read(builtIn, "x").Layout.Theme));
    }

    [Fact]
    public void Missing_files_are_left_out_with_a_warning()
    {
        var layout = LayoutWithFiles();
        File.Delete(Resolved(layout).FontFile!);

        using var package = Pack(layout, "Shared", out var warnings);

        Assert.Contains(warnings, w => w.Contains("font file") && w.Contains("wasn't found"));
        var contents = LayoutPackage.Read(package, "fallback");
        Assert.Null(contents.Font);
        Assert.NotNull(contents.Background);
    }

    [Fact]
    public void A_layout_without_files_imports_without_a_folder()
    {
        var library = NewProfile("pc");
        using var package = Pack(new LayoutDocument { Width = 640 }, "Plain", out _);

        var name = library.Import(LayoutPackage.Read(package, "x"), "Plain");

        Assert.Equal(640, library.Load(name).Width);
        Assert.Empty(Directory.GetDirectories(library.Directory));
    }

    [Theory]
    [InlineData("../evil.json")]
    [InlineData("assets/../../evil.ttf")]
    [InlineData("assets\\..\\..\\evil.ttf")]
    [InlineData("/etc/evil.png")]
    [InlineData("\\Windows\\evil.png")]
    [InlineData("C:/Windows/evil.png")]
    [InlineData("C:evil.png")]
    public void Unsafe_entry_names_reject_the_package(string entryName)
    {
        Assert.False(LayoutPackage.IsSafeEntryName(entryName));
        using var package = Zip(("layout.json", MinimalLayout), (entryName, "x"));

        var ex = Assert.Throws<InvalidDataException>(() => LayoutPackage.Read(package, "x"));
        Assert.Contains("unsafe path", ex.Message);
    }

    [Theory]
    [InlineData("layout.json")]
    [InlineData("assets/font.ttf")]
    [InlineData("assets/My Font v1.2.otf")]
    public void Ordinary_entry_names_are_safe(string entryName) => Assert.True(LayoutPackage.IsSafeEntryName(entryName));

    [Fact]
    public void Unexpected_file_types_and_unreferenced_entries_are_skipped()
    {
        const string layout = "{ \"theme\": { \"fontFile\": \"assets/evil.exe\", \"backgroundImage\": \"assets/background.png\" }, \"widgets\": [] }";
        using var package = Zip(("layout.json", layout), ("assets/evil.exe", "MZ"), ("assets/background.png", "png"), ("readme.bat", "echo"));

        var contents = LayoutPackage.Read(package, "x");

        Assert.Null(contents.Font);
        Assert.Null(Resolved(contents.Layout).FontFile);
        Assert.Equal(".png", contents.Background!.Extension);
        Assert.Contains(contents.Skipped, s => s.Contains("evil.exe"));
        Assert.Contains(contents.Skipped, s => s.Contains("readme.bat"));

        var library = NewProfile("pc");
        var name = library.Import(contents, "x");
        var folder = Path.Combine(library.Directory, name);
        Assert.Equal(new[] { "background.png", "imported.txt" }, Directory.GetFiles(folder).Select(f => Path.GetFileName(f)).Order().ToArray());
    }

    [Fact]
    public void Paths_outside_the_package_are_not_kept()
    {
        const string layout = "{ \"theme\": { \"fontFile\": \"C:\\\\Windows\\\\Fonts\\\\arial.ttf\", \"backgroundImage\": \"\\\\\\\\server\\\\share\\\\a.png\" }, \"widgets\": [] }";
        using var package = Zip(("layout.json", layout));

        var contents = LayoutPackage.Read(package, "x");

        Assert.Null(Resolved(contents.Layout).FontFile);
        Assert.Null(Resolved(contents.Layout).BackgroundImage);
        Assert.Equal(2, contents.Skipped.Count);
    }

    [Fact]
    public void Oversized_entries_are_rejected_by_their_real_size()
    {
        // Compresses to almost nothing, like a zip bomb would.
        var huge = "{ \"widgets\": [] }" + new string(' ', (int)LayoutPackage.MaxLayoutBytes);
        using var package = Zip(("layout.json", huge));

        var ex = Assert.Throws<InvalidDataException>(() => LayoutPackage.Read(package, "x"));
        Assert.Contains("bigger than", ex.Message);
    }

    [Fact]
    public void Files_that_are_not_packages_are_rejected()
    {
        using var notZip = new MemoryStream(Encoding.UTF8.GetBytes(MinimalLayout));
        Assert.Throws<InvalidDataException>(() => LayoutPackage.Read(notZip, "x"));

        using var noLayout = Zip(("something.json", "{}"));
        Assert.Throws<InvalidDataException>(() => LayoutPackage.Read(noLayout, "x"));

        using var badJson = Zip(("layout.json", "{ not json"));
        Assert.Throws<InvalidDataException>(() => LayoutPackage.Read(badJson, "x"));
    }

    [Fact]
    public void The_name_comes_from_the_manifest_then_the_file_name()
    {
        using (var named = Pack(new LayoutDocument(), "Racing sim", out _))
            Assert.Equal("Racing sim", LayoutPackage.Read(named, "download (1)").Name);

        using (var badName = Zip(("layout.json", MinimalLayout), ("manifest.json", "{ \"name\": \"..\\\\..\\\\x\" }")))
            Assert.Equal("download (1)", LayoutPackage.Read(badName, "download (1)").Name);

        using (var neither = Zip(("layout.json", MinimalLayout)))
            Assert.Equal("Imported layout", LayoutPackage.Read(neither, "CON").Name);
    }

    [Fact]
    public void A_byte_order_mark_is_accepted()
    {
        using var package = Zip(("layout.json", "\uFEFF" + MinimalLayout));
        Assert.Equal(300, LayoutPackage.Read(package, "x").Layout.Width);
    }

    [Fact]
    public void Importing_over_a_layout_replaces_it_and_drops_its_old_files()
    {
        var library = NewProfile("pc");
        using (var first = Pack(LayoutWithFiles(), "Shared", out _))
            library.Import(LayoutPackage.Read(first, "x"), "Shared");
        var firstFolder = Path.GetDirectoryName(Resolved(library.Load("Shared")).FontFile)!;

        var second = LayoutWithFiles();
        second.Width = 1234;
        using (var package = Pack(second, "Shared", out _))
            library.Import(LayoutPackage.Read(package, "x"), "shared");

        var replaced = library.Load("Shared");
        Assert.Equal(1234, replaced.Width);
        Assert.Single(library.List(), n => n.Equals("Shared", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Resolved(replaced).FontFile));
        Assert.NotEqual(firstFolder, Path.GetDirectoryName(Resolved(replaced).FontFile));
        Assert.False(Directory.Exists(firstFolder));
    }

    [Fact]
    public void Imported_files_stay_while_a_copy_uses_them_and_go_with_the_last_layout()
    {
        var library = NewProfile("pc");
        using (var package = Pack(LayoutWithFiles(), "Shared", out _))
            library.Import(LayoutPackage.Read(package, "x"), "Shared");
        var folder = Path.GetDirectoryName(Resolved(library.Load("Shared")).FontFile)!;
        library.Duplicate("Shared", "Shared copy");

        library.Delete("Shared");
        Assert.True(Directory.Exists(folder));

        library.Delete("Shared copy");
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Folders_the_app_did_not_make_are_never_cleaned_up()
    {
        var library = NewProfile("pc");
        var mine = Directory.CreateDirectory(Path.Combine(library.Directory, "my stuff")).FullName;
        File.WriteAllText(Path.Combine(mine, "notes.txt"), "keep");

        library.DeleteUnusedImportFolders();

        Assert.True(File.Exists(Path.Combine(mine, "notes.txt")));
    }

    [Fact]
    public void Writing_to_a_file_replaces_it_whole()
    {
        var path = Path.Combine(_root, "Shared.ibl");
        File.WriteAllText(path, "old");

        LayoutPackage.Write(LayoutWithFiles(), "Shared", path);

        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal("Shared", LayoutPackage.Read(path).Name);
    }
}
