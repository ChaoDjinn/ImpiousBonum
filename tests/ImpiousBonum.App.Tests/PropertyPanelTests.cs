using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ImpiousBonum.App.Controls;
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
    public void List_entries_are_dragged_by_a_handle_rather_than_moved_with_arrows()
    {
        Sta.Run(() =>
        {
            var session = new LayoutSession(new LayoutDocument { Widgets = [Rows("A", "B")] });
            var (panel, window) = Show(session);
            session.Select(0);
            Pump();

            var handles = Handles(panel);
            var arrows = Descendants<Button>(panel).Where(b => b.ToolTip is "Move up" or "Move down").ToList();
            var removes = Descendants<Button>(panel).Count(b => b.ToolTip is "Remove");
            window.Close();

            Assert.Equal(2, handles.Count);
            Assert.All(handles, h => Assert.True(h.Focusable));
            Assert.Empty(arrows);
            Assert.Equal(2, removes);
        });
    }

    [Fact]
    public void Moving_a_list_entry_from_the_keyboard_keeps_focus_on_it()
    {
        Sta.Run(() =>
        {
            var session = new LayoutSession(new LayoutDocument { Widgets = [Rows("A", "B", "C")] });
            var (panel, window) = Show(session);
            session.Select(0);
            Pump();

            // What Alt+Down does with focus on the first row's handle.
            Handles(panel)[0].Focus();
            Assert.True(DragReorder.Move(Handles(panel)[0], +1));
            Pump();
            var handleFocused = Handles(panel)[1].IsKeyboardFocused;

            // And Alt+Up while typing in the last row's label: still typing in it afterwards.
            var label = LabelBox(panel, "C");
            label.Focus();
            Assert.True(DragReorder.Move(label, -1));
            Pump();
            var labelFocused = LabelBox(panel, "C").IsKeyboardFocused;
            window.Close();

            Assert.Equal(["B", "C", "A"], Labels(session));
            Assert.True(handleFocused);
            Assert.True(labelFocused);
            session.Undo();
            Assert.Equal(["B", "A", "C"], Labels(session));
            session.Undo();
            Assert.Equal(["A", "B", "C"], Labels(session));
            Assert.False(session.CanUndo);
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

    /// <summary>A rows widget with a row for each label.</summary>
    private static JsonObject Rows(params string[] labels)
    {
        var widget = Widget("rows");
        widget["rows"] = new JsonArray([.. labels.Select(l => (JsonNode)new JsonObject { ["label"] = l })]);
        return widget;
    }

    private static List<string> Labels(LayoutSession session) =>
        [.. ((JsonArray)session.Document.Widgets[0]["rows"]!).Select(r => r!["label"]!.GetValue<string>())];

    /// <summary>The list entries' drag handles, top to bottom.</summary>
    private static List<FrameworkElement> Handles(PropertyPanel panel) => [.. Descendants<FrameworkElement>(panel).Where(DragReorder.GetIsHandle)];

    private static TextBox LabelBox(PropertyPanel panel, string label) => Descendants<TextBox>(panel).Single(b => b.Text == label);

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
