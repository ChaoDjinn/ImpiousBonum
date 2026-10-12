using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ImpiousBonum.App.Controls;
using ImpiousBonum.App.Editor;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Tests;

[Collection("AppPaths")]
public sealed class LayoutEditorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ImpiousBonum.Tests", Guid.NewGuid().ToString("N"));

    public LayoutEditorTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void The_widget_list_has_a_menu_in_place_of_the_toolbar_arrows()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            using var store = new LayoutStore(new AppSettings());
            var session = new LayoutSession(ThreeWidgets());
            var (editor, window) = Show(session, store);

            var menu = editor.WidgetList.ContextMenu.Items.OfType<MenuItem>().ToList();
            session.Select(0);
            var atTheBack = (menu[0].IsEnabled, menu[1].IsEnabled);
            menu[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            window.Close();

            Assert.Null(editor.FindName("ForwardButton"));
            Assert.Null(editor.FindName("BackwardButton"));
            Assert.Equal(["Bring forward", "Send back", "Duplicate", "Delete"], menu.Select(m => (string)m.Header));
            Assert.Equal(["Ctrl+]", "Ctrl+[", "Ctrl+D", "Del"], menu.Select(m => m.InputGestureText));
            Assert.Equal((true, false), atTheBack);
            Assert.Equal("BAC", Order(session));
            Assert.Equal(1, session.SelectedIndex);
        });
    }

    [Fact]
    public void Moving_a_widget_in_the_list_keeps_it_selected_and_focused()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            using var store = new LayoutStore(new AppSettings());
            var session = new LayoutSession(ThreeWidgets());
            var (editor, window) = Show(session, store);
            var list = editor.WidgetList;
            session.Select(0);
            var first = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            first.Focus();

            // What dropping it below the second does (and Alt+Down, from the keyboard).
            Assert.True(DragReorder.Move(first, +1));
            var focused = list.ItemContainerGenerator.ContainerFromIndex(1) is ListBoxItem { IsKeyboardFocused: true };
            var selected = list.SelectedIndex;
            window.Close();

            Assert.Equal("BAC", Order(session));
            Assert.Equal(1, session.SelectedIndex);
            Assert.Equal(1, selected);
            Assert.True(focused);
            session.Undo();
            Assert.Equal("ABC", Order(session));
            Assert.False(session.CanUndo);
        });
    }

    [Fact]
    public void Ctrl_brackets_bring_the_selected_widget_forward_and_send_it_back()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            using var store = new LayoutStore(new AppSettings());
            var session = new LayoutSession(ThreeWidgets());
            var (editor, window) = Show(session, store);
            session.Select(0);

            var forward = editor.HandleKey(Key.OemCloseBrackets, ModifierKeys.Control);
            var afterForward = Order(session);
            var back = editor.HandleKey(Key.OemOpenBrackets, ModifierKeys.Control);
            var afterBack = Order(session);
            // Without Ctrl they're left to whatever has focus.
            var plain = editor.HandleKey(Key.OemCloseBrackets, ModifierKeys.None);
            window.Close();

            Assert.True(forward);
            Assert.Equal("BAC", afterForward);
            Assert.True(back);
            Assert.Equal("ABC", afterBack);
            Assert.False(plain);
            Assert.Equal("ABC", Order(session));
            Assert.Equal(0, session.SelectedIndex);
        });
    }

    [Fact]
    public void Clicking_a_widget_in_the_list_selects_it_without_moving_it()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            using var store = new LayoutStore(new AppSettings());
            var session = new LayoutSession(ThreeWidgets());
            var (editor, window) = Show(session, store);

            var row = (ListBoxItem)editor.WidgetList.ItemContainerGenerator.ContainerFromIndex(2);
            Click(Descendants<TextBlock>(row).First());
            window.Close();

            Assert.Equal(2, session.SelectedIndex);
            Assert.Equal("ABC", Order(session));
            Assert.False(session.CanUndo);
        });
    }

    [Fact]
    public void Clicking_a_list_entry_handle_leaves_focus_on_it_for_alt_up_and_down()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            using var store = new LayoutStore(new AppSettings());
            var rows = new JsonObject { ["type"] = "rows", ["x"] = 0, ["y"] = 0, ["width"] = 400, ["height"] = 200 };
            rows["rows"] = new JsonArray([.. "ABC".Select(c => (JsonNode)new JsonObject { ["label"] = c.ToString() })]);
            var session = new LayoutSession(new LayoutDocument { Widgets = [rows] });
            var (editor, window) = Show(session, store);
            session.Select(0);
            Pump();

            // Clicked on its glyph: the properties' scroll viewer, around it, mustn't take focus from it afterwards.
            var handle = (Border)Descendants<FrameworkElement>(editor).First(DragReorder.GetIsHandle);
            handle.Child.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
            var focused = handle.IsKeyboardFocused;
            window.Close();

            Assert.True(focused);
        });
    }

    /// <summary>Text widgets A, B and C, drawn in that order.</summary>
    private static LayoutDocument ThreeWidgets() => new()
    {
        Widgets = [.. "ABC".Select(c => new JsonObject { ["type"] = "text", ["x"] = 0, ["y"] = 0, ["width"] = 10, ["height"] = 10, ["text"] = c.ToString() })],
    };

    private static string Order(LayoutSession session) => string.Concat(session.Document.Widgets.Select(w => w.GetString("text")));

    /// <summary>The editor in a window off screen, laid out so its list has rows.</summary>
    private static (LayoutEditor Editor, Window Window) Show(LayoutSession session, LayoutStore store)
    {
        var editor = new LayoutEditor(session, store, () => store.Active, () => null);
        var window = new Window
        {
            Content = editor, Width = 1500, Height = 900, Left = -30000, Top = -30000,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
        };
        window.Show();
        window.UpdateLayout();
        return (editor, window);
    }

    /// <summary>Presses the left button on an element and lets go, without moving, as Windows sends it.</summary>
    private static void Click(UIElement element)
    {
        foreach (var routed in new[] { Mouse.PreviewMouseDownEvent, Mouse.MouseDownEvent, Mouse.PreviewMouseUpEvent, Mouse.MouseUpEvent })
            element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = routed });
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
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var inner in Descendants<T>(child))
                yield return inner;
        }
    }
}
