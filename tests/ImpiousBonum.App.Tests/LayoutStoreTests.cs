using System.IO;
using System.Text.Json.Nodes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Tests;

[Collection("AppPaths")]
public sealed class LayoutStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ImpiousBonum.Tests", Guid.NewGuid().ToString("N"));

    public LayoutStoreTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private LayoutLibrary Library() => new(_directory);

    private static LayoutDocument WithText(string text) => new()
    {
        Widgets = [new JsonObject { ["type"] = "text", ["x"] = 0, ["y"] = 0, ["width"] = 100, ["height"] = 50, ["text"] = text }],
    };

    private static string? TextOf(LayoutDocument layout) => (string?)layout.Widgets[0]["text"];

    [Fact]
    public void First_run_writes_the_built_in_default()
    {
        var library = Library();
        library.EnsureSeeded();
        Assert.Equal(new[] { LayoutLibrary.DefaultName }, library.List());
        Assert.Equal(LayoutStore.LoadDefault().Widgets.Count, library.Load(LayoutLibrary.DefaultName).Widgets.Count);
    }

    [Fact]
    public void An_existing_layout_json_becomes_Default_with_hand_edits_intact()
    {
        const string handWritten = "// my notes\n{ \"width\": 800, \"height\": 200, \"widgets\": [] }\n";
        var library = Library();
        File.WriteAllText(library.LegacyFile, handWritten);

        library.EnsureSeeded();

        Assert.False(File.Exists(library.LegacyFile));
        Assert.Equal(new[] { LayoutLibrary.DefaultName }, library.List());
        Assert.Equal(handWritten, File.ReadAllText(library.PathOf(LayoutLibrary.DefaultName)));
        Assert.Equal(800, library.Load(LayoutLibrary.DefaultName).Width);
    }

    [Fact]
    public void Migration_never_overwrites_an_existing_Default()
    {
        var library = Library();
        library.EnsureSeeded();
        library.Save(LayoutLibrary.DefaultName, WithText("kept"));
        File.WriteAllText(library.LegacyFile, "{ \"widgets\": [] }");

        library.EnsureSeeded();

        Assert.Equal("kept", TextOf(library.Load(LayoutLibrary.DefaultName)));
        Assert.True(File.Exists(library.LegacyFile));
    }

    [Fact]
    public void Lists_saved_layouts_sorted_and_finds_them_ignoring_case()
    {
        var library = Library();
        library.EnsureSeeded();
        library.Save("gaming", WithText("g"));
        library.Save("Desktop", WithText("d"));

        Assert.Equal(new[] { "Default", "Desktop", "gaming" }, library.List());
        Assert.Equal("gaming", library.Find("GAMING"));
        Assert.Null(library.Find("missing"));
        Assert.Equal("gaming", library.Resolve("Gaming"));
        Assert.Equal("Default", library.Resolve("missing"));
    }

    [Fact]
    public void Rename_moves_the_file_and_keeps_its_contents()
    {
        var library = Library();
        library.EnsureSeeded();
        library.Save("Gaming", WithText("g"));

        library.Rename("Gaming", "Games");

        Assert.Equal(new[] { "Default", "Games" }, library.List());
        Assert.Equal("g", TextOf(library.Load("Games")));
        Assert.Throws<ArgumentException>(() => library.Rename("Games", "default"));
    }

    [Fact]
    public void Duplicate_copies_the_saved_file()
    {
        var library = Library();
        library.EnsureSeeded();
        library.Save("Gaming", WithText("g"));

        library.Duplicate("Gaming", "Gaming copy");

        Assert.Equal("g", TextOf(library.Load("Gaming copy")));
        Assert.Equal(File.ReadAllText(library.PathOf("Gaming")), File.ReadAllText(library.PathOf("Gaming copy")));
        Assert.Throws<ArgumentException>(() => library.Duplicate("Gaming", "gaming COPY"));
    }

    [Fact]
    public void Delete_removes_a_layout_but_never_the_last_one()
    {
        var library = Library();
        library.EnsureSeeded();
        library.Save("Gaming", WithText("g"));

        library.Delete("Default");
        Assert.Equal(new[] { "Gaming" }, library.List());
        Assert.Throws<InvalidOperationException>(() => library.Delete("Gaming"));
        Assert.Throws<FileNotFoundException>(() => library.Delete("Default"));
    }

    [Theory]
    [InlineData("Gaming")]
    [InlineData("Desk 2 (quiet)")]
    [InlineData("Übersicht")]
    public void Accepts_ordinary_names(string name) => Assert.Null(LayoutLibrary.ValidateName(name));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" padded")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("what?")]
    [InlineData("C:")]
    [InlineData("tab\there")]
    [InlineData("ends.")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("com1")]
    public void Rejects_unsafe_file_names(string name) => Assert.NotNull(LayoutLibrary.ValidateName(name));

    [Fact]
    public void New_names_must_not_clash_except_with_the_layout_being_renamed()
    {
        var library = Library();
        library.EnsureSeeded();
        Assert.NotNull(library.CheckNewName("default"));
        Assert.Null(library.CheckNewName("default", except: "Default"));
        Assert.Null(library.CheckNewName("Gaming"));
    }

    [Fact]
    public void Store_remembers_the_active_layout_and_switches_away_from_a_deleted_one()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            var settings = new AppSettings();
            using (var store = new LayoutStore(settings))
            {
                Assert.Equal("Default", store.Active);
                store.Save("Gaming", WithText("g"));

                var switched = 0;
                store.ActiveChanged += (_, _) => switched++;
                store.SetActive("gaming");
                Assert.Equal("Gaming", store.Active);
                Assert.Equal("g", TextOf(store.Load()));
                Assert.Equal(1, switched);

                store.Rename("Gaming", "Games");
                Assert.Equal("Games", store.Active);
            }

            // The choice survives a restart.
            using (var store = new LayoutStore(AppSettings.Load()))
            {
                Assert.Equal("Games", store.Active);
                store.Delete("Games");
                Assert.Equal("Default", store.Active);
                Assert.Equal("Default", AppSettings.Load().ActiveLayout);
            }
        });
    }
}
