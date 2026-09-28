using System.IO;
using System.Text.Json;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Tests;

// Shares AppPaths (a static) with the layout store tests, so they mustn't run in parallel.
[Collection("AppPaths")]
public sealed class GameLayoutTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
    private static readonly GameLayoutRule Notepad = new("notepad", "Test");
    private static readonly GameLayoutRule EldenRing = new("eldenring.exe", "Elden Ring");
    private static readonly GameLayoutRule[] Rules = [Notepad, EldenRing];

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ImpiousBonum.Tests", Guid.NewGuid().ToString("N"));

    public GameLayoutTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    /// <summary>Feeds one sample a second, starting at <see cref="Start"/>, and returns the layout after each (null = the user's own).</summary>
    private static List<string?> Run(GameLayoutSwitcher switcher, params string?[] apps)
    {
        var layouts = new List<string?>();
        for (var i = 0; i < apps.Length; i++)
        {
            switcher.Update(Rules, apps[i], Start.AddSeconds(i));
            layouts.Add(switcher.Current?.Layout);
        }
        return layouts;
    }

    [Theory]
    [InlineData("notepad", true)]
    [InlineData("Notepad", true)]
    [InlineData("NOTEPAD.EXE", true)]
    [InlineData(" notepad ", true)]
    [InlineData("notepad++", false)]
    [InlineData("note", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Rules_match_process_names_ignoring_case_and_exe(string? process, bool matches) =>
        Assert.Equal(matches, Notepad.Matches(process));

    [Fact]
    public void A_rule_written_with_exe_matches_the_bare_process_name()
    {
        Assert.Same(EldenRing, GameLayoutSwitcher.Match(Rules, "eldenring"));
        Assert.Null(GameLayoutSwitcher.Match(Rules, "explorer"));
        Assert.False(new GameLayoutRule("", "Test").Matches(""));
    }

    [Fact]
    public void Switches_after_the_game_has_been_in_front_for_the_delay_and_back_after_it_has_gone()
    {
        var switcher = new GameLayoutSwitcher(TimeSpan.FromSeconds(3));
        var layouts = Run(switcher, "explorer", "notepad", "notepad", "notepad", "notepad", "chrome", "chrome", "chrome", "chrome");
        Assert.Equal(new[] { null, null, null, null, "Test", "Test", "Test", "Test", null }, layouts);
    }

    [Fact]
    public void Brief_focus_changes_do_not_switch()
    {
        var switcher = new GameLayoutSwitcher(TimeSpan.FromSeconds(3));
        // Notepad flashes up for two seconds, twice.
        var layouts = Run(switcher, "chrome", "notepad", "notepad", "chrome", "notepad", "notepad", "chrome");
        Assert.All(layouts, layout => Assert.Null(layout));
    }

    [Fact]
    public void Alt_tabbing_out_of_a_game_briefly_keeps_its_layout()
    {
        var switcher = new GameLayoutSwitcher(TimeSpan.FromSeconds(3));
        var layouts = Run(switcher, "eldenring", "eldenring", "eldenring", "eldenring", "discord", "discord", "eldenring", "discord", "eldenring");
        Assert.Equal(new[] { null, null, null, "Elden Ring", "Elden Ring", "Elden Ring", "Elden Ring", "Elden Ring", "Elden Ring" }, layouts);
    }

    [Fact]
    public void Going_straight_from_one_game_to_another_switches_once_the_new_one_is_stable()
    {
        var switcher = new GameLayoutSwitcher(TimeSpan.FromSeconds(3));
        var layouts = Run(switcher, "notepad", "notepad", "notepad", "notepad", "eldenring", "eldenring", "eldenring", "eldenring");
        Assert.Equal(new[] { null, null, null, "Test", "Test", "Test", "Test", "Elden Ring" }, layouts);
    }

    [Fact]
    public void Update_reports_only_actual_changes()
    {
        var switcher = new GameLayoutSwitcher(TimeSpan.Zero);
        Assert.True(switcher.Update(Rules, "notepad", Start));
        Assert.False(switcher.Update(Rules, "notepad", Start.AddSeconds(1)));
        // Another app linked to the same layout is no change.
        Assert.False(switcher.Update([Notepad, new GameLayoutRule("wordpad", "test")], "wordpad", Start.AddSeconds(2)));
        Assert.True(switcher.Update(Rules, null, Start.AddSeconds(3)));
        Assert.Null(switcher.Current);
    }

    [Fact]
    public void Reset_goes_back_straight_away()
    {
        var switcher = new GameLayoutSwitcher(TimeSpan.Zero);
        switcher.Update(Rules, "notepad", Start);
        switcher.Reset();
        Assert.Null(switcher.Current);
    }

    [Fact]
    public void Settings_read_game_links_and_drop_broken_ones()
    {
        AppPaths.UseDataDirectory(_directory);
        File.WriteAllText(AppPaths.SettingsFile, """
            {
              "gameLayouts": [
                { "process": "eldenring", "layout": "Elden Ring" },
                { "process": "", "layout": "Nothing" },
                { "layout": "No process" },
                null,
              ],
              "gameLayoutsEnabled": false,
            }
            """);

        var settings = AppSettings.Load();

        Assert.Equal(new[] { new GameLayoutRule("eldenring", "Elden Ring") }, settings.GameLayouts);
        Assert.False(settings.GameLayoutsEnabled);
        Assert.True(new AppSettings().GameLayoutsEnabled);
        Assert.Contains("\"gameLayouts\"", JsonSerializer.Serialize(settings, JsonDefaults.Options));
    }

    [Fact]
    public void A_game_layout_shows_without_changing_the_remembered_choice()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            var settings = new AppSettings();
            using var store = new LayoutStore(settings);
            store.Save("Test", new LayoutDocument());
            store.Save("Desktop", new LayoutDocument());
            store.SetActive("Desktop");

            var switched = 0;
            store.ActiveChanged += (_, _) => switched++;
            store.ShowTemporarily("test");
            Assert.Equal("Test", store.Active);
            Assert.True(store.IsTemporary);
            Assert.Equal("Desktop", AppSettings.Load().ActiveLayout);

            store.ShowTemporarily(null);
            Assert.Equal("Desktop", store.Active);
            Assert.False(store.IsTemporary);
            Assert.Equal(2, switched);

            Assert.Throws<FileNotFoundException>(() => store.ShowTemporarily("Deleted"));
            Assert.Equal("Desktop", store.Active);
        });
    }

    [Fact]
    public void Picking_a_layout_during_a_game_becomes_the_choice_to_return_to()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            using var store = new LayoutStore(new AppSettings());
            store.Save("Test", new LayoutDocument());
            store.Save("Desktop", new LayoutDocument());

            store.ShowTemporarily("Test");
            // Picking the game layout itself from the tray makes it the remembered choice too.
            store.SetActive("Test");
            Assert.False(store.IsTemporary);
            Assert.Equal("Test", AppSettings.Load().ActiveLayout);

            store.ShowTemporarily("Desktop");
            store.SetActive("Default");
            store.ShowTemporarily(null);
            Assert.Equal("Default", store.Active);
        });
    }

    [Fact]
    public void Renaming_a_layout_updates_game_links_and_deleting_a_game_layout_goes_back()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            var settings = new AppSettings { GameLayouts = [new GameLayoutRule("eldenring", "Elden Ring")] };
            using var store = new LayoutStore(settings);
            store.Save("Elden Ring", new LayoutDocument());

            store.ShowTemporarily("Elden Ring");
            store.Rename("Elden Ring", "Lands Between");
            Assert.Equal("Lands Between", store.Active);
            Assert.Equal(new[] { new GameLayoutRule("eldenring", "Lands Between") }, AppSettings.Load().GameLayouts);
            Assert.Equal("Default", AppSettings.Load().ActiveLayout);

            store.Delete("Lands Between");
            Assert.Equal("Default", store.Active);
            Assert.False(store.IsTemporary);
        });
    }
}
