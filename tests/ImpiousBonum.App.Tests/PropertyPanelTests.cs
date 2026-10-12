using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ImpiousBonum.App.Editor;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Tests;

public sealed class PropertyPanelTests
{
    public static TheoryData<string> WidgetTypes() => [.. WidgetFactory.Descriptors.Select(d => d.Type)];

    /// <summary>
    /// Looking at a widget's settings mustn't change it. Editable drop-downs (colours, fonts) fill in their text a moment
    /// after they're shown, which used to write each default into the layout, so the editor asked to save changes nobody
    /// had made.
    /// </summary>
    [Theory]
    [MemberData(nameof(WidgetTypes))]
    public void Selecting_a_widget_with_only_its_position_changes_nothing(string type)
    {
        Sta.Run(() =>
        {
            var session = new LayoutSession(new LayoutDocument { Widgets = [Widget(type)] });
            var (panel, window) = Show(session);

            session.Select(0);
            Pump();
            panel.RefreshLive();
            session.Select(-1);
            Pump();
            session.Select(0);
            Pump();
            window.Close();

            Assert.False(session.IsDirty, $"Selecting a {type} widget changed the layout to: {session.Document.Widgets[0].ToJsonString()}");
        });
    }

    [Fact]
    public void Looking_at_the_canvas_and_theme_changes_nothing()
    {
        Sta.Run(() =>
        {
            var session = new LayoutSession(LayoutStore.LoadDefault());
            var (_, window) = Show(session);
            session.Select(-1);
            Pump();
            window.Close();

            Assert.False(session.IsDirty);
        });
    }

    [Fact]
    public void Typing_a_colour_still_sets_it()
    {
        Sta.Run(() =>
        {
            var session = new LayoutSession(new LayoutDocument { Widgets = [Widget("text")] });
            var (panel, window) = Show(session);
            session.Select(0);
            Pump();

            var colour = Descendants<ComboBox>(panel).First(c => c.IsEditable && c.Text == "foreground");
            colour.Text = "#FF0000";
            Pump();
            window.Close();

            Assert.Equal("#FF0000", (string?)session.Document.Widgets[0]["color"]);
            Assert.True(session.IsDirty);
        });
    }

    [Fact]
    public void The_editor_opens_centred_on_the_main_screen_and_fits_it()
    {
        // A 4K main screen at 150% scaling, less the taskbar.
        Assert.Equal((530.0, 130.0, 1500.0, 900.0), ImpiousBonum.App.Settings.SettingsWindow.Centred(new Rect(0, 0, 2560, 1160), 1500, 900));
        // A laptop screen too small for the editor's usual size: it shrinks to fit, with a margin.
        var (left, top, width, height) = ImpiousBonum.App.Settings.SettingsWindow.Centred(new Rect(0, 0, 1280, 680), 1500, 900);
        Assert.Equal((32, 17, 1216, 646), (left, top, width, height));
    }

    private static JsonObject Widget(string type) => new() { ["type"] = type, ["x"] = 10, ["y"] = 10, ["width"] = 400, ["height"] = 200 };

    /// <summary>The panel in a window off screen, as in the editor: some controls only fill in their text once shown.</summary>
    private static (PropertyPanel Panel, Window Window) Show(LayoutSession session)
    {
        var panel = new PropertyPanel(session, () => (1920, 480), () => new MetricStore());
        var window = new Window
        {
            Content = panel, Width = 440, Height = 3000, Left = -30000, Top = -30000,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
        };
        window.Show();
        Pump();
        return (panel, window);
    }

    /// <summary>Lets the window lay itself out and run what it queued, as the editor's would between clicks.</summary>
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(() => frame.Continue = false, DispatcherPriority.ContextIdle);
        Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var inner in Descendants<T>(child))
                yield return inner;
        }
    }
}
