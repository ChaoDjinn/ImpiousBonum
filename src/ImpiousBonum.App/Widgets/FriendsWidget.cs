using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Steam;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// One row per Steam friend who's online (<see cref="SteamFriendsProvider"/>), those playing first: their name, and the
/// game they're playing or whether they're around. The widget hides while no friends are online.
/// </summary>
public sealed class FriendsWidget : Widget
{
    /// <summary>How faded a friend who's away or busy is, beside those who are around.</summary>
    internal const double AwayOpacity = 0.6;

    public static WidgetDescriptor Descriptor { get; } = new(
        "friends", "Steam friends",
        "One row per Steam friend who's online, those playing first: their name, and the game they're playing or whether they're away. It needs your own Steam Web API key, on the Steam page. Shows nothing while no friends are online.",
        400, 260,
        [
            Setting.Toggle("playingOnly", "Only those playing", false, group: Setting.Content, help: "Leaves out friends who are online but not in a game."),
            Setting.Toggle("showAway", "Show those away", true, group: Setting.Content, help: "Friends who are away or busy, faded. Off leaves them out."),
            Setting.Number("fontSize", "Font size", 24, 6, 200),
            Setting.ShrinkToFit(),
            Setting.Number("spacing", "Row spacing", 6, 0, 200, group: Setting.Appearance),
            Setting.Color("labelColor", "Name colour", "foreground"),
            Setting.Color("playingColor", "Game colour", "accent", help: "The game a friend is playing."),
            Setting.Color("valueColor", "Status colour", "secondary", help: "Online, Away or Busy, for a friend who isn't playing."),
        ],
        (settings, theme) => new FriendsWidget(settings, theme));

    private readonly StackPanel _rows = new();
    private readonly bool _playingOnly;
    private readonly bool _showAway;
    private readonly double _fontSize;
    private readonly double _spacing;
    private readonly Brush _label;
    private readonly Brush _playing;
    private readonly Brush _value;
    private string? _shown;

    public FriendsWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _playingOnly = settings.Bool("playingOnly");
        _showAway = settings.Bool("showAway");
        _fontSize = settings.Number("fontSize");
        _spacing = settings.Number("spacing");
        _label = theme.Resolve(settings.String("labelColor"), theme.Foreground);
        _playing = theme.Resolve(settings.String("playingColor"), theme.Accent);
        _value = theme.Resolve(settings.String("valueColor"), theme.Secondary);
        Children.Add(Fit(_rows, FitDirection.Height));
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        var friends = Pick(store.GetAttachment(SteamFriendsProvider.Friends) as IReadOnlyList<SteamFriend>, _playingOnly, _showAway);
        // Steam is asked once a minute: rebuild only when what's on show changes.
        var key = string.Join("\n", friends.Select(f => $"{f.Name}|{f.Presence}|{f.Game}"));
        if (key != _shown)
        {
            _shown = key;
            Rebuild(friends);
        }
        SetUnavailable(friends.Count == 0);
    }

    /// <summary>The friends to show, in the provider's order: all who are online, or just those playing, with or without those away.</summary>
    internal static IReadOnlyList<SteamFriend> Pick(IReadOnlyList<SteamFriend>? friends, bool playingOnly, bool showAway) =>
        friends is null
            ? []
            : [.. friends.Where(f => (!playingOnly || f.Game is not null) && (showAway || f.Presence == SteamPresence.Online))];

    /// <summary>What's on the right of a friend's row: the game they're playing, or Online, Away or Busy.</summary>
    internal static string Status(SteamFriend friend) => friend.Game ?? friend.Presence switch
    {
        SteamPresence.Away => "Away",
        SteamPresence.Busy => "Busy",
        _ => "Online",
    };

    private void Rebuild(IReadOnlyList<SteamFriend> friends)
    {
        _rows.Children.Clear();
        for (var i = 0; i < friends.Count; i++)
        {
            var friend = friends[i];
            var name = CreateText(_fontSize, _label);
            name.Text = friend.Name;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            var status = CreateText(_fontSize * 0.85, friend.Game is null ? _value : _playing, HorizontalAlignment.Right);
            status.Text = Status(friend);
            status.TextTrimming = TextTrimming.CharacterEllipsis;
            status.VerticalAlignment = VerticalAlignment.Center;
            var row = LabelAndValue(name, status);
            row.Margin = new Thickness(0, 0, 0, i < friends.Count - 1 ? _spacing : 0);
            row.Opacity = friend.Presence == SteamPresence.Online ? 1 : AwayOpacity;
            _rows.Children.Add(row);
        }
    }
}
