using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Games;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// The games played lately (<see cref="RecentGamesProvider"/>), newest first, as tiles of Steam's art or the game's icon.
/// A tap starts the game through its launcher. The tiles keep their size however many games there are, and the widget
/// hides while there are none.
/// </summary>
public sealed class RecentGamesWidget : Widget
{
    public static readonly IReadOnlyList<string> Launchers = ["steam", "epic", "gog", "battlenet"];

    public static WidgetDescriptor Descriptor { get; } = new(
        "games", "Recent games",
        "The games you've played lately, newest first, as Steam's or GOG Galaxy's art or the game's icon. Tap one to start it through its launcher. Games from Steam, the Epic Games launcher, GOG and Battle.net, read from their own records on this PC and GOG Galaxy's.",
        600, 300,
        [
            Setting.Number("count", "Games", 4, 1, RecentGamesProvider.Kept, group: Setting.Content),
            Setting.Number("rows", "Rows", 1, 1, 4, group: Setting.Content, help: "The games are spread over this many rows, newest first along each."),
            Setting.Choice("art", "Picture", GameArt.CoverKind, [GameArt.CoverKind, GameArt.HeaderKind], group: Setting.Content,
                help: "For a Steam game: cover is the tall box art, header the wide banner. An Epic Games or GOG game with GOG Galaxy installed has Galaxy's cover, and its background for header. Other games show their icon."),
            Setting.Picks("launchers", "Launchers", PickList.AllValue, choices: Launchers, all: "All launchers",
                help: "Games from all of them, or just the ones chosen: steam, epic (the Epic Games launcher), gog or battlenet."),
            Setting.Toggle("launch", "Tap to play", true, group: Setting.Content,
                help: "A tap starts the game through its launcher, as its desktop shortcut does: a GOG game through GOG Galaxy if Galaxy installed it, else by its own exe, and a Battle.net game through Battle.net."),
            Setting.Number("fontSize", "Font size", 18, 6, 200),
            Setting.Toggle("showNames", "Show names", false, help: "Each game's name under its picture. A game with only its icon always shows its name."),
            Setting.Toggle("showPlayed", "Show when played", false,
                help: "How long ago it was played, and the hours altogether where known, e.g. 2d 3h ago · 41 h."),
            Setting.Number("spacing", "Spacing", 12, 0, 200, group: Setting.Appearance),
            Setting.Number("cornerRadius", "Corner radius", 8, 0, 200, group: Setting.Appearance),
            Setting.Color("tileColor", "Tile colour", "track", help: "Behind a game's icon. track is the theme's bar track."),
            Setting.Color("labelColor", "Name colour", "foreground"),
            Setting.Color("valueColor", "Played colour", "secondary"),
        ],
        (settings, theme) => new RecentGamesWidget(settings, theme));

    /// <summary>Decode pictures in steps of this many pixels, so resizing in the editor doesn't decode them at every size.</summary>
    private const int DecodeStep = 64;

    private readonly UniformGrid _grid = new();
    private readonly int _count;
    private readonly string _art;
    private readonly PickList _launchers;
    private readonly bool _launch;
    private readonly double _fontSize;
    private readonly bool _showNames;
    private readonly bool _showPlayed;
    private readonly double _spacing;
    private readonly double _cornerRadius;
    private readonly Brush _tile;
    private readonly Brush _label;
    private readonly Brush _value;
    private readonly List<Tile> _tiles = [];
    // Not null at first, so the first refresh sets the widget up even without a list.
    private object? _list = new();
    private string? _shown;
    private DateTime _playedAt;

    public RecentGamesWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _count = (int)settings.Number("count");
        var rows = (int)settings.Number("rows");
        _art = settings.String("art");
        _launchers = settings.Picks("launchers");
        _launch = settings.Bool("launch");
        _fontSize = settings.Number("fontSize");
        _showNames = settings.Bool("showNames");
        _showPlayed = settings.Bool("showPlayed");
        _spacing = settings.Number("spacing");
        _cornerRadius = settings.Number("cornerRadius");
        _tile = theme.Resolve(settings.String("tileColor"), theme.Track);
        _label = theme.Resolve(settings.String("labelColor"), theme.Foreground);
        _value = theme.Resolve(settings.String("valueColor"), theme.Secondary);

        // A slot for every game asked for, so tiles stay the same size however many there are.
        _grid.Rows = Math.Min(rows, _count);
        _grid.Columns = (int)Math.Ceiling(_count / (double)_grid.Rows);
        _grid.Margin = new Thickness(-_spacing / 2);
        Children.Add(_grid);
    }

    /// <summary>A tap on a tile starts the game on the PC, from a tablet too.</summary>
    public override bool PressableFromTablet => true;

    public override void Refresh(MetricStore store, DateTime now)
    {
        var list = store.GetAttachment(RecentGamesProvider.Recent);
        if (!ReferenceEquals(list, _list))
        {
            _list = list;
            var games = Pick(list as IReadOnlyList<RecentGame> ?? [], _launchers).Take(_count).ToList();
            // The same games in the same order keep their tiles; only what they say changes.
            var key = string.Join("\n", games.Select(g => g.Game.Key));
            if (key != _shown)
            {
                _shown = key;
                Rebuild(games);
            }
            for (var i = 0; i < _tiles.Count; i++)
                _tiles[i].Game = games[i];
            SetUnavailable(games.Count == 0);
            _playedAt = default;
        }

        // How long ago changes slowly: once a minute is plenty.
        if (_showPlayed && now - _playedAt >= TimeSpan.FromMinutes(1))
        {
            _playedAt = now;
            foreach (var tile in _tiles)
            {
                if (tile.Played is not null)
                    tile.Played.Text = Played(tile.Game, now);
            }
        }
    }

    /// <summary>The games from the launchers chosen, in the order they were played.</summary>
    internal static IEnumerable<RecentGame> Pick(IReadOnlyList<RecentGame> games, PickList launchers) =>
        launchers.All ? games : games.Where(g => launchers.Names.Contains(g.Game.Store.ToString(), StringComparer.OrdinalIgnoreCase));

    /// <summary>How long ago a game was played, and its hours where known: "2d 3h ago · 41 h", "43m ago · <1 h".</summary>
    internal static string Played(RecentGame game, DateTime now)
    {
        var ago = now.ToUniversalTime() - game.LastPlayed.UtcDateTime;
        var when = ago < TimeSpan.FromMinutes(1) ? "Now" : $"{DurationText.Format(ago)} ago";
        return game.Hours switch
        {
            null => when,
            < 1 => $"{when} · <1 h",
            { } hours => $"{when} · {hours.ToString("0", CultureInfo.CurrentCulture)} h",
        };
    }

    private void Rebuild(IReadOnlyList<RecentGame> games)
    {
        _grid.Children.Clear();
        _tiles.Clear();
        foreach (var game in games)
        {
            var picture = new Border { CornerRadius = new CornerRadius(_cornerRadius), Background = _tile, ClipToBounds = true };
            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition());
            content.Children.Add(picture);

            var name = CreateText(_fontSize, _label, HorizontalAlignment.Center);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.Margin = new Thickness(0, _fontSize * 0.25, 0, 0);
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            SetRow(name, 1);
            content.Children.Add(name);

            TextBlock? played = null;
            if (_showPlayed)
            {
                played = CreateText(_fontSize * 0.85, _value, HorizontalAlignment.Center);
                played.TextTrimming = TextTrimming.CharacterEllipsis;
                content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                SetRow(played, 2);
                content.Children.Add(played);
            }

            var tile = new Tile(picture, name, played, _art, _showNames);
            FrameworkElement holder;
            if (_launch)
            {
                var target = new TapTarget { Child = content, Background = Brushes.Transparent };
                target.Tapped += (_, _) =>
                {
                    if (tile.Game.Game is { Launch: { } launch } game)
                        RequestAction(new WidgetAction(WidgetAction.Open, launch, game.Arguments) { WorkingDirectory = game.WorkingDirectory ?? "" });
                };
                holder = target;
            }
            else
            {
                holder = new Border { Child = content };
            }
            holder.Margin = new Thickness(_spacing / 2);
            _grid.Children.Add(holder);
            tile.Game = game;
            _tiles.Add(tile);
        }
    }

    /// <summary>One game's tile: its picture, decoded at the size it's shown, its name and when it was played.</summary>
    private sealed class Tile
    {
        private readonly Border _picture;
        private readonly TextBlock _name;
        private readonly string _art;
        private readonly bool _showNames;
        private RecentGame? _game;
        private (int Width, int Height) _decoded;

        public Tile(Border picture, TextBlock name, TextBlock? played, string art, bool showNames)
        {
            _picture = picture;
            _name = name;
            Played = played;
            _art = art;
            _showNames = showNames;
            _picture.SizeChanged += (_, _) => Paint();
        }

        public TextBlock? Played { get; }

        public RecentGame Game
        {
            get => _game!;
            set
            {
                var art = _game?.Art;
                _game = value;
                _name.Text = value.Game.Name;
                // A game with only its icon needs its name to be told apart.
                _name.Visibility = _showNames || !value.Art.HasSteamArt ? Visibility.Visible : Visibility.Collapsed;
                if (!ReferenceEquals(art, value.Art))
                {
                    _decoded = default;
                    Paint();
                }
            }
        }

        private void Paint()
        {
            if (_game is null || _picture.ActualWidth <= 0 || _picture.ActualHeight <= 0)
                return;
            var size = (Step(_picture.ActualWidth), Step(_picture.ActualHeight));
            if (size == _decoded)
                return;
            _decoded = size;

            var bytes = _game.Art.Picture(_art);
            var icon = _game.Art.IsIcon(bytes);
            // An icon is small and square, so it's shown whole in the middle of the tile; Steam's art fills it.
            var fit = icon ? "fit" : "fill";
            var bitmap = bytes is null ? null : BackgroundImage.Load(bytes, fit, size.Item1, size.Item2);
            _picture.Child = null;
            if (bitmap is null)
                return;
            if (icon)
            {
                var inset = Math.Min(_picture.ActualWidth, _picture.ActualHeight) * 0.2;
                _picture.Child = new Image { Source = bitmap, Stretch = Stretch.Uniform, Margin = new Thickness(inset) };
            }
            else
            {
                var brush = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
                brush.Freeze();
                _picture.Child = new Border { Background = brush, CornerRadius = _picture.CornerRadius };
            }
        }

        private static int Step(double pixels) => (int)Math.Ceiling(pixels / DecodeStep) * DecodeStep;
    }
}
