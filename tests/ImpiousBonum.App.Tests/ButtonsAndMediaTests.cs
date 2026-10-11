using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Tests;

public sealed class ButtonsAndMediaTests
{
    [Theory]
    [InlineData("Ctrl+Shift+M", new ushort[] { 0x11, 0x10 }, 0x4D)]
    [InlineData("win + d", new ushort[] { 0x5B }, 0x44)]
    [InlineData("F13", new ushort[0], 0x7C)]
    [InlineData("Alt+5", new ushort[] { 0x12 }, 0x35)]
    [InlineData("Ctrl+Esc", new ushort[] { 0x11 }, 0x1B)]
    [InlineData("VolumeMute", new ushort[0], 0xAD)]
    [InlineData("MediaPlayPause", new ushort[0], 0xB3)]
    [InlineData("Ctrl+PgDn", new ushort[] { 0x11 }, 0x22)]
    [InlineData("Shift", new ushort[0], 0x10)]
    public void Shortcuts_parse_into_modifiers_and_a_key(string text, ushort[] modifiers, int key)
    {
        var chord = KeyChord.Parse(text, out var problem);
        Assert.Null(problem);
        Assert.Equal(modifiers, chord!.Modifiers);
        Assert.Equal((ushort)key, chord.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("+")]
    [InlineData("Hyper+A")]
    [InlineData("Ctrl+Banana")]
    [InlineData("13")]
    public void Shortcuts_that_make_no_sense_say_why(string text)
    {
        Assert.Null(KeyChord.Parse(text, out var problem));
        Assert.False(string.IsNullOrEmpty(problem));
    }

    [Fact]
    public void Navigation_media_and_windows_keys_are_sent_as_extended_keys()
    {
        Assert.True(ActionRunner.IsExtended(0x22));  // Page Down
        Assert.True(ActionRunner.IsExtended(0x5B));  // Windows
        Assert.True(ActionRunner.IsExtended(0xB3));  // play/pause
        Assert.False(ActionRunner.IsExtended(0x41)); // A
        Assert.False(ActionRunner.IsExtended(0x10)); // Shift
    }

    [Fact]
    public void Buttons_without_a_target_explain_themselves_instead_of_doing_nothing()
    {
        var runner = new ActionRunner(_ => null);
        Assert.NotNull(runner.Run(new WidgetAction(WidgetAction.Open)));
        Assert.NotNull(runner.Run(new WidgetAction(WidgetAction.Layout)));
        Assert.NotNull(runner.Run(new WidgetAction(WidgetAction.Keys, "Ctrl+Banana")));
        Assert.NotNull(runner.Run(new WidgetAction("teleport")));
        Assert.Equal("missing", new ActionRunner(_ => "missing").Run(new WidgetAction(WidgetAction.Layout, "Gaming")));
    }

    [Fact]
    public void Tapping_a_button_asks_the_app_for_its_action()
    {
        Sta.Run(() =>
        {
            var widget = WidgetFactory.Create(new JsonObject
            {
                ["type"] = "buttons",
                ["buttons"] = new JsonArray(
                    new JsonObject { ["label"] = "Calc", ["action"] = "open", ["target"] = " calc.exe ", ["arguments"] = "" },
                    new JsonObject { ["label"] = "Gaming", ["icon"] = "none", ["action"] = "layout", ["target"] = "Gaming" }),
            }, Theme.From(new ThemeSettings()));
            var host = new Grid();
            host.Children.Add(widget);
            var requested = new List<WidgetAction>();
            host.AddHandler(Widget.ActionEvent, new EventHandler<WidgetActionEventArgs>((_, e) => requested.Add(e.Action)));

            var targets = ((Panel)widget.Children[0]).Children.OfType<TapTarget>().ToList();
            Assert.Equal(2, targets.Count);
            targets[0].Tap();
            targets[1].Tap();

            Assert.Equal(new[] { new WidgetAction("open", "calc.exe"), new WidgetAction("layout", "Gaming") }, requested);
        });
    }

    [Fact]
    public void Imports_can_list_and_clear_what_buttons_open()
    {
        var layout = new LayoutDocument
        {
            Widgets =
            [
                new JsonObject
                {
                    ["type"] = "buttons",
                    ["buttons"] = new JsonArray(
                        new JsonObject { ["action"] = "open", ["target"] = "cmd.exe", ["arguments"] = "/c del stuff" },
                        new JsonObject { ["action"] = "OPEN", ["target"] = "https://example.com" },
                        new JsonObject { ["action"] = "next" }),
                },
                new JsonObject { ["type"] = "text", ["text"] = "open cmd.exe" },
            ],
        };

        Assert.Equal(new[] { "cmd.exe /c del stuff", "https://example.com" }, ButtonsWidget.OpenTargets(layout));
        ButtonsWidget.ClearOpenTargets(layout);
        Assert.Empty(ButtonsWidget.OpenTargets(layout));
        Assert.Equal(3, ((JsonArray)layout.Widgets[0]["buttons"]!).Count);
    }

    [Fact]
    public void Now_playing_hides_while_nothing_plays_and_follows_the_track()
    {
        Sta.Run(() =>
        {
            var store = new MetricStore();
            using (var provider = new MediaProvider())
                provider.Initialize(store);
            var widget = WidgetFactory.Create(new JsonObject { ["type"] = "media" }, Theme.From(new ThemeSettings()));

            widget.Refresh(store, DateTime.Now);
            Assert.Equal(Visibility.Collapsed, widget.Visibility);
            widget.Editing = true;
            Assert.Equal(Visibility.Visible, widget.Visibility);
            widget.Editing = false;

            store.SetText(MediaProvider.Title, "Neon Overpass");
            store.SetText(MediaProvider.Artist, "Browser Test");
            store.SetText(MediaProvider.Status, "Playing");
            store.Set(MediaProvider.Progress, 25);
            widget.Refresh(store, DateTime.Now);

            Assert.Equal(Visibility.Visible, widget.Visibility);
            var texts = Descendants<TextBlock>(widget).Select(t => t.Text).ToList();
            Assert.Contains("Neon Overpass", texts);
            Assert.Contains("Browser Test", texts);
        });
    }

    [Fact]
    public void Album_art_decodes_and_anything_else_is_ignored()
    {
        Sta.Run(() =>
        {
            var bitmap = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, new byte[4 * 4 * 4], 16);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var png = new MemoryStream();
            encoder.Save(png);

            Assert.NotNull(MediaWidget.Decode(png.ToArray(), 100));
            Assert.Null(MediaWidget.Decode([1, 2, 3, 4], 100));
        });
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
