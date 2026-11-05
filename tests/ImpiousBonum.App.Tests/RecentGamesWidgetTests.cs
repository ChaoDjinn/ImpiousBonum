using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Games;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Tests;

public sealed class RecentGamesWidgetTests
{
    private const string FortniteLink = "com.epicgames.launcher://apps/Fortnite?action=launch&silent=true";

    private static readonly Theme Plain = Theme.From(new ThemeSettings());
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 22, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 1];
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G'];

    private static readonly RecentGame HollowKnight = Steam(367520, "Hollow Knight", Now.AddHours(-3), 0.5);
    private static readonly RecentGame Fortnite = new(
        new StoreGame(GameStore.Epic, "Fortnite", "Fortnite", @"C:\Games\Fortnite", FortniteLink), Now.AddHours(-5), null, new GameArt(null, null, null, Png));
    private static readonly RecentGame Hades = Steam(1145360, "Hades", Now.AddDays(-2), 41);
    private static readonly RecentGame UnrealGold = new(
        new StoreGame(GameStore.Gog, "1207658924", "Unreal Gold", @"C:\GOG Games\Unreal Gold", @"C:\GOG Games\Unreal Gold\System\Unreal.exe", "-windowed"),
        Now.AddDays(-3), 2, new GameArt(null, null, null, Png));
    private static readonly RecentGame Terraria = Steam(105600, "Terraria", Now.AddDays(-9), 11);

    [Fact]
    public void Tiles_show_the_games_played_lately_newest_first_and_a_tap_starts_one()
    {
        Sta.Run(() =>
        {
            var store = Store(HollowKnight, Fortnite, Hades, UnrealGold, Terraria);
            var widget = WidgetFactory.Create(new JsonObject { ["type"] = "games" }, Plain);
            var actions = new List<WidgetAction>();
            widget.AddHandler(Widget.ActionEvent, new EventHandler<WidgetActionEventArgs>((_, e) =>
            {
                actions.Add(e.Action);
                e.Handled = true;
            }));
            widget.Refresh(store, Now.UtcDateTime);

            var tiles = Tiles(widget);
            Assert.Equal(["Hollow Knight", "Fortnite", "Hades", "Unreal Gold"], tiles.Select(t => Name(t).Text));
            // Steam's art speaks for itself; a game with only its icon needs its name.
            Assert.Equal([Visibility.Collapsed, Visibility.Visible, Visibility.Collapsed, Visibility.Visible], tiles.Select(t => Name(t).Visibility));
            Assert.Equal(Visibility.Visible, widget.Visibility);

            ((TapTarget)tiles[1]).Tap();
            ((TapTarget)tiles[3]).Tap();
            Assert.Equal(
                [
                    new WidgetAction(WidgetAction.Open, FortniteLink),
                    new WidgetAction(WidgetAction.Open, @"C:\GOG Games\Unreal Gold\System\Unreal.exe", "-windowed"),
                ], actions);
        });
    }

    [Fact]
    public void Launchers_count_and_names_can_be_chosen_and_taps_turned_off()
    {
        Sta.Run(() =>
        {
            var store = Store(HollowKnight, Fortnite, Hades, UnrealGold, Terraria);
            var widget = WidgetFactory.Create(new JsonObject
            {
                ["type"] = "games",
                ["launchers"] = "gog, Epic",
                ["count"] = 8,
                ["rows"] = 2,
                ["showNames"] = true,
                ["launch"] = false,
            }, Plain);
            widget.Refresh(store, Now.UtcDateTime);

            var tiles = Tiles(widget);
            Assert.Equal(["Fortnite", "Unreal Gold"], tiles.Select(t => Name(t).Text));
            Assert.All(tiles, t => Assert.Equal(Visibility.Visible, Name(t).Visibility));
            Assert.DoesNotContain(tiles, t => t is TapTarget);
            // Slots for all eight, four to a row, so the tiles keep their size.
            var grid = (UniformGrid)widget.Children[0];
            Assert.Equal((2, 4), (grid.Rows, grid.Columns));
        });
    }

    [Fact]
    public void The_same_games_keep_their_tiles_and_none_hides_the_widget()
    {
        Sta.Run(() =>
        {
            var store = Store(HollowKnight, Hades);
            var widget = WidgetFactory.Create(new JsonObject { ["type"] = "games", ["showPlayed"] = true }, Plain);
            widget.Refresh(store, Now.UtcDateTime);
            var tile = Tiles(widget)[0];
            Assert.Equal("3h 0m ago · <1 h", Played(tile).Text);

            // Played again: the same tiles, saying so.
            store.SetAttachment(RecentGamesProvider.Recent, new List<RecentGame> { HollowKnight with { LastPlayed = Now, Hours = 1.2 }, Hades });
            widget.Refresh(store, Now.UtcDateTime);
            Assert.Same(tile, Tiles(widget)[0]);
            Assert.Equal("Now · 1 h", Played(tile).Text);

            store.SetAttachment(RecentGamesProvider.Recent, null);
            widget.Refresh(store, Now.UtcDateTime);
            Assert.Empty(Tiles(widget));
            Assert.Equal(Visibility.Collapsed, widget.Visibility);
            widget.Editing = true;
            Assert.Equal(Visibility.Visible, widget.Visibility);
        });
    }

    [Fact]
    public void When_a_game_was_played_reads_simply()
    {
        var now = Now.UtcDateTime;
        Assert.Equal("2d 0h ago · 41 h", RecentGamesWidget.Played(Hades, now));
        Assert.Equal("5h 0m ago", RecentGamesWidget.Played(Fortnite, now));
        Assert.Equal("Now · <1 h", RecentGamesWidget.Played(HollowKnight with { LastPlayed = Now.AddSeconds(-20) }, now));
    }

    private static RecentGame Steam(int appId, string name, DateTimeOffset played, double hours) =>
        new(new StoreGame(GameStore.Steam, appId.ToString(), name, $@"D:\Steam\steamapps\common\{name}", $"steam://rungameid/{appId}"),
            played, hours, new GameArt(Jpeg, null, null, null));

    private static MetricStore Store(params RecentGame[] games)
    {
        var store = new MetricStore();
        store.SetAttachment(RecentGamesProvider.Recent, games.ToList());
        return store;
    }

    private static List<FrameworkElement> Tiles(Widget widget) =>
        ((UniformGrid)widget.Children[0]).Children.Cast<FrameworkElement>().ToList();

    private static Grid Content(FrameworkElement tile) => (Grid)((Border)tile).Child;

    private static TextBlock Name(FrameworkElement tile) => Content(tile).Children.OfType<TextBlock>().First();

    private static TextBlock Played(FrameworkElement tile) => Content(tile).Children.OfType<TextBlock>().Last();
}
