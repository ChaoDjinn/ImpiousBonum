using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Tests;

public sealed class MixerWidgetTests
{
    private static readonly Theme Plain = Theme.From(new ThemeSettings());

    private static readonly OutputVolume Headphones = new("Headphones", 0.45, Muted: false);

    private static readonly IReadOnlyList<AppVolume> Apps =
    [
        new("discord", "Discord", null, 0.8, Muted: false, Playing: true),
        new("chrome", "Google Chrome", null, 0.4, Muted: true, Playing: false),
        new("spotify", "Spotify", null, 0.3, Muted: false, Playing: true),
    ];

    private static MetricStore Store(IReadOnlyList<AppVolume>? apps = null)
    {
        var store = new MetricStore();
        using var provider = new SoundProvider(_ => [], () => null);
        provider.Publish(store, Headphones, apps ?? Apps);
        return store;
    }

    private static Widget Create(JsonObject? settings = null)
    {
        settings ??= [];
        settings["type"] = "mixer";
        return WidgetFactory.Create(settings, Plain);
    }

    private static List<string> Texts(DependencyObject widget) =>
        [.. Descendants<TextBlock>(widget).Select(t => t.Text)];

    [Fact]
    public void The_device_comes_first_then_the_apps_chosen_and_playing_up_to_the_most()
    {
        var state = new MixerState(Headphones, Apps);

        Assert.Equal(["Headphones", "Discord", "Google Chrome", "Spotify"],
            MixerWidget.Entries(state, PickList.Parse(PickList.AllValue, true), output: true, playingOnly: false, max: 6).Select(e => e.Name));
        Assert.Equal(["Discord", "Spotify"],
            MixerWidget.Entries(state, PickList.Parse(PickList.AllValue, true), output: false, playingOnly: true, max: 6).Select(e => e.Name));
        Assert.Equal(["Headphones", "Spotify", "Discord"],
            MixerWidget.Entries(state, PickList.Parse("Spotify, Gone, discord", true), output: true, playingOnly: false, max: 6).Select(e => e.Name));
        Assert.Equal(["Discord"],
            MixerWidget.Entries(state, PickList.Parse(PickList.AllValue, true), output: false, playingOnly: false, max: 1).Select(e => e.Name));
        Assert.Equal(AppVolumeControl.Output, MixerWidget.Entries(state, PickList.Parse(PickList.AllValue, true), true, false, 6)[0].Key);
    }

    [Fact]
    public void Each_row_has_its_name_and_volume_and_a_muted_app_says_so()
    {
        Sta.Run(() =>
        {
            var widget = Create();
            var store = new MetricStore();
            widget.Refresh(store, DateTime.Now);
            // Not read yet: nothing rather than "no apps".
            Assert.Empty(Texts(widget));
            Assert.NotNull(store.GetAttachment("sound.mixer.wanted"));

            widget.Refresh(Store(), DateTime.Now);
            Assert.Equal(["Headphones", "45 %", "Discord", "80 %", "Google Chrome", "Muted", "Spotify", "30 %"], Texts(widget));

            widget.Refresh(Store([]), DateTime.Now);
            Assert.Equal(["Headphones", "45 %"], Texts(widget));
            Assert.Equal([MixerWidget.Empty], Texts(Refreshed(Create(new JsonObject { ["output"] = false }), Store([]))));
        });
    }

    [Fact]
    public void Dragging_a_bar_asks_for_the_volume_and_shows_it_until_Windows_catches_up()
    {
        Sta.Run(() =>
        {
            var widget = Refreshed(Create(new JsonObject { ["output"] = false }), Store());
            var actions = new List<WidgetAction>();
            widget.AddHandler(Widget.ActionEvent, new EventHandler<WidgetActionEventArgs>((_, e) => actions.Add(e.Action)));
            var bars = Descendants<MixerWidget.VolumeBar>(widget).ToList();
            widget.Measure(new Size(460, 230));
            widget.Arrange(new Rect(0, 0, 460, 230));
            var spotify = bars[2];
            Assert.Equal(0, spotify.VolumeAt(-10));
            Assert.Equal(1, spotify.VolumeAt(spotify.ActualWidth + 10));

            spotify.DragTo(spotify.ActualWidth * 0.75);


            var action = Assert.Single(actions);
            Assert.Equal((WidgetAction.AppVolume, "spotify"), (action.Kind, action.Target));
            Assert.True(double.TryParse(action.Arguments, System.Globalization.CultureInfo.InvariantCulture, out var asked));
            Assert.Equal(asked, spotify.Value);

            // Windows still says 30% a moment later: the bar keeps what was asked.
            widget.Refresh(Store(), DateTime.Now);
            Assert.Equal(asked, spotify.Value);
            widget.Refresh(Store(), DateTime.Now + MixerWidget.Settle);
            Assert.Equal(0.3, spotify.Value);
        });
    }

    [Fact]
    public void Tapping_an_icon_mutes_its_app_or_the_device()
    {
        Sta.Run(() =>
        {
            var widget = Refreshed(Create(), Store());
            var actions = new List<WidgetAction>();
            widget.AddHandler(Widget.ActionEvent, new EventHandler<WidgetActionEventArgs>((_, e) => actions.Add(e.Action)));
            var icons = Descendants<TapTarget>(widget).ToList();

            icons[0].Tap();
            icons[1].Tap();

            Assert.Equal([(WidgetAction.AppMute, AppVolumeControl.Output), (WidgetAction.AppMute, "discord")], actions.Select(a => (a.Kind, a.Target)));
            Assert.Contains("Muted", Texts(widget)[..4]);
            Assert.True(widget.PressableFromTablet);
        });
    }

    [Fact]
    public void Volumes_are_set_one_at_a_time_ending_where_the_slider_did()
    {
        var set = new List<(string App, double Volume)>();
        var first = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var runner = new ActionRunner(_ => null)
        {
            SetAppVolume = (app, volume) =>
            {
                lock (set)
                    set.Add((app, volume));
                first.Set();
                release.Wait(TimeSpan.FromSeconds(10));
                return true;
            },
        };

        Assert.Null(runner.Run(new WidgetAction(WidgetAction.AppVolume, "spotify", "0.10")));
        Assert.True(first.Wait(TimeSpan.FromSeconds(10)));
        // While the first is being set, more moves come: only the last of them is set next.
        runner.Run(new WidgetAction(WidgetAction.AppVolume, "spotify", "0.20"));
        runner.Run(new WidgetAction(WidgetAction.AppVolume, "spotify", "0.35"));
        release.Set();
        for (var i = 0; i < 100 && set.Count < 2; i++)
            Thread.Sleep(20);

        Assert.Equal([("spotify", 0.10), ("spotify", 0.35)], set);
    }

    [Fact]
    public void An_app_mute_button_needs_an_app_and_says_what_it_does()
    {
        var muted = new List<string>();
        var runner = new ActionRunner(_ => null) { ToggleAppMute = app => { lock (muted) muted.Add(app); return true; } };

        Assert.Contains("no app to mute", runner.Run(new WidgetAction(WidgetAction.AppMute)));
        Assert.Null(runner.Run(new WidgetAction(WidgetAction.AppMute, "Spotify")));
        for (var i = 0; i < 100 && muted.Count == 0; i++)
            Thread.Sleep(20);
        Assert.Equal(["Spotify"], muted);
        Assert.Equal("Mutes or unmutes Spotify", ButtonsWidget.Describe(new WidgetAction(WidgetAction.AppMute, "Spotify")));
        Assert.Contains(WidgetAction.AppMute, WidgetAction.Kinds);
        Assert.DoesNotContain(WidgetAction.AppVolume, WidgetAction.Kinds);
    }

    private static Widget Refreshed(Widget widget, MetricStore store)
    {
        widget.Refresh(store, DateTime.Now);
        return widget;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var deeper in Descendants<T>(child))
                yield return deeper;
        }
    }
}
