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
            Assert.Equal(["Bring forward", "Send back", "Group", "Ungroup", "Rename group…", "Duplicate", "Delete", "Align"], menu.Select(m => (string)m.Header));
            Assert.Equal(["Ctrl+]", "Ctrl+[", "Ctrl+G", "Ctrl+Shift+G", null, "Ctrl+D", "Del", ""], menu.Select(m => m.InputGestureText));
            Assert.Equal((true, false), atTheBack);
            Assert.Equal("BAC", Order(session));
            Assert.Equal(1, session.SelectedIndex);
        });
    }

    [Fact]
    public void The_widget_list_shows_the_one_drawn_on_top_first()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            using var store = new LayoutStore(new AppSettings());
            var session = new LayoutSession(ThreeWidgets());
            var (editor, window) = Show(session, store);

            var rows = editor.WidgetList.Items.Cast<LayoutEditor.ListEntry>().Select(e => e.Detail.Trim()).ToList();
            var note = Descendants<TextBlock>(editor).Any(t => t.Text.StartsWith("The top of the list draws on top"));
            window.Close();

            Assert.Equal(["C", "B", "A"], rows);
            Assert.True(note);
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
            // A, drawn at the back, is the bottom row.
            session.Select(0);
            var back = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(2);
            back.Focus();

            // What dropping it above the one over it does (and Alt+Up, from the keyboard): it comes forward.
            Assert.True(DragReorder.Move(back, -1));
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

            // The top row is C, drawn last, on top of the others.
            var row = (ListBoxItem)editor.WidgetList.ItemContainerGenerator.ContainerFromIndex(0);
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

    [Fact]
    public void The_list_and_the_session_keep_several_selected_widgets_in_step()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            using var store = new LayoutStore(new AppSettings());
            var session = new LayoutSession(ThreeWidgets());
            var (editor, window) = Show(session, store);
            var list = editor.WidgetList;

            // Selected in the preview (or by Ctrl+A), shown in the list.
            session.Select([2, 0]);
            var shown = Selected(list);
            var status = editor.StatusText.Text;
            // Ctrl+click in the list, which adds to what it has selected.
            list.SelectedItems.Add(list.Items[1]);
            var fromList = session.Selection.ToList();
            // Back to one: the status bar says what it is as before.
            session.Select(1);
            var one = Selected(list);
            var oneStatus = editor.StatusText.Text;
            window.Close();

            // The list is top first: C's row, then A's.
            Assert.Equal([0, 2], shown);
            Assert.StartsWith("2 widgets selected", status);
            Assert.Equal([2, 0, 1], fromList);
            Assert.Equal([1], one);
            Assert.StartsWith("Text: B · position 0, 0", oneStatus);
        });
    }

    [Fact]
    public void Widgets_that_read_the_same_are_told_apart_in_the_list()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            using var store = new LayoutStore(new AppSettings());
            // A and its copy have the same row in the list.
            var session = new LayoutSession(ThreeWidgets());
            session.Select(0);
            session.DuplicateSelected();
            var (editor, window) = Show(session, store);
            var list = editor.WidgetList;

            // Top first: C, B, the copy, then A.
            var copy = Selected(list);
            list.SelectedItems.Add(list.Items[3]);
            var both = session.Selection.ToList();
            window.Close();

            Assert.Equal([2], copy);
            Assert.Equal([1, 0], both);
        });
    }

    [Fact]
    public void Align_is_offered_once_two_or_more_are_selected_and_each_is_one_undo_step()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            using var store = new LayoutStore(new AppSettings());
            var document = ThreeWidgets();
            for (var i = 0; i < 3; i++)
            {
                document.Widgets[i]["x"] = i * 100;
                document.Widgets[i]["y"] = i * 20;
            }
            var session = new LayoutSession(document);
            var (editor, window) = Show(session, store);
            var toolbar = editor.AlignButton.ContextMenu.Items.OfType<MenuItem>().ToList();
            var listMenu = editor.WidgetList.ContextMenu.Items.OfType<MenuItem>().Last();

            session.Select(0);
            var withOne = (editor.AlignButton.IsEnabled, listMenu.IsEnabled);
            session.Select([0, 1]);
            var withTwo = (editor.AlignButton.IsEnabled, listMenu.IsEnabled, Enabled: toolbar.Where(i => i.IsEnabled).Select(i => (string)i.Header).ToList());
            session.Select([0, 1, 2]);
            var withThree = toolbar.Count(i => i.IsEnabled);
            toolbar.Single(i => (string)i.Header == "Align top").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var tops = session.Document.Widgets.Select(w => w.GetDouble("y", -1)).ToList();
            window.Close();

            string[] headers =
            [
                "Align left", "Align centres", "Align right", "Align top", "Align middles", "Align bottom",
                "Distribute horizontally", "Distribute vertically", "Same width", "Same height",
            ];
            Assert.Equal(headers, toolbar.Select(i => (string)i.Header));
            Assert.Equal(headers, listMenu.Items.OfType<MenuItem>().Select(i => (string)i.Header));
            Assert.Equal((false, false), withOne);
            Assert.True(withTwo.Item1 && withTwo.Item2);
            Assert.Equal(headers.Where(h => !h.StartsWith("Distribute")), withTwo.Enabled);
            Assert.Equal(10, withThree);
            Assert.Equal([0.0, 0, 0], tops);
            session.Undo();
            Assert.Equal([0.0, 20, 40], session.Document.Widgets.Select(w => w.GetDouble("y", -1)));
            Assert.False(session.CanUndo);
        });
    }

    [Fact]
    public void Keys_act_on_every_selected_widget()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            using var store = new LayoutStore(new AppSettings());
            var session = new LayoutSession(ThreeWidgets());
            var (editor, window) = Show(session, store);

            editor.HandleKey(Key.A, ModifierKeys.Control);
            var all = session.Selection.ToList();
            editor.HandleKey(Key.Right, ModifierKeys.Shift);
            editor.HandleKey(Key.Down, ModifierKeys.None);
            var moved = session.Document.Widgets.Select(w => (w.GetDouble("x", 0), w.GetDouble("y", 0))).ToList();
            editor.HandleKey(Key.Delete, ModifierKeys.None);
            var left = session.Document.Widgets.Count;
            window.Close();

            Assert.Equal([0, 1, 2], all);
            Assert.All(moved, at => Assert.Equal((10.0, 1.0), at));
            Assert.Equal(0, left);
            session.Undo();
            Assert.Equal("ABC", Order(session));
            // The nudges were one step.
            session.Undo();
            Assert.False(session.CanUndo);
        });
    }

    [Fact]
    public void Grouped_widgets_are_under_their_groups_row_which_selects_them_all()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            using var store = new LayoutStore(new AppSettings());
            var session = new LayoutSession(ThreeWidgets());
            var (editor, window) = Show(session, store);
            var list = editor.WidgetList;

            session.Select([2, 0]);
            editor.HandleKey(Key.G, ModifierKeys.Control);
            var rows = Rows(list);
            var indents = list.Items.Cast<LayoutEditor.ListEntry>().Select(e => e.Indent.Left).ToList();
            var status = editor.StatusText.Text;
            // Clicking the group's row: its widgets' rows are the ones that show selected.
            session.Select(-1);
            list.SelectedItems.Add(list.Items[0]);
            var fromRow = (session.SelectedGroup, Selected(list).Order().ToList());
            editor.HandleKey(Key.G, ModifierKeys.Control | ModifierKeys.Shift);
            var ungrouped = Rows(list);
            window.Close();

            Assert.Equal(["Group 1 · 2 widgets", "C", "A", "B"], rows);
            Assert.Equal([0.0, 18, 18, 0], indents);
            Assert.StartsWith("Group: Group 1 · 2 widgets", status);
            Assert.Equal("Group 1", fromRow.SelectedGroup);
            Assert.Equal([1, 2], fromRow.Item2);
            Assert.Equal(["C", "A", "B"], ungrouped);
        });
    }

    [Fact]
    public void Moving_rows_takes_widgets_into_and_out_of_groups_and_groups_whole()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            using var store = new LayoutStore(new AppSettings());
            var session = new LayoutSession(ThreeWidgets());
            session.Select([1, 2]);
            session.GroupSelected("W");
            var (editor, window) = Show(session, store);
            var list = editor.WidgetList;
            ListBoxItem Row(int index) => (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(index);

            // [W] C B A: A up a place, between C and B, into the group.
            DragReorder.Move(Row(3), -1);
            var joined = Rows(list);
            // The group's row down a place: there's nothing under it, so nowhere to go. Up instead, from the top: none.
            var stuck = DragReorder.Move(Row(0), -1);
            // C, the group's first, up a place: out above the group's row.
            session.Select(2);
            editor.HandleKey(Key.OemCloseBrackets, ModifierKeys.Control);
            var left = Rows(list);
            window.Close();

            Assert.Equal(["W · 3 widgets", "C", "A", "B"], joined);
            Assert.False(stuck);
            Assert.Equal(["C", "W · 2 widgets", "A", "B"], left);
            Assert.Equal("C", session.SelectedWidget!.GetString("text"));
        });
    }

    [Fact]
    public void Renaming_a_group_keeps_it_selected()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            using var store = new LayoutStore(new AppSettings());
            var session = new LayoutSession(ThreeWidgets());
            session.Select([0, 1, 2]);
            session.GroupSelected("W");
            var (editor, window) = Show(session, store);
            var list = editor.WidgetList;
            // Clicked on its row, so the list has focus, as when the rename is chosen from its menu.
            Click(Descendants<TextBlock>((ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0)).Last());
            var before = session.Selection.Order().ToList();

            session.RenameGroup("W", "Weather area");
            Pump();
            var after = session.Selection.Order().ToList();
            window.Close();

            Assert.Equal([0, 1, 2], before);
            Assert.Equal([0, 1, 2], after);
            Assert.Equal("Weather area", session.SelectedGroup);
        });
    }

    /// <summary>What each row of the list says.</summary>
    private static List<string> Rows(ListBox list) => [.. list.Items.Cast<LayoutEditor.ListEntry>().Select(e => e.IsGroup ? e.Name + e.Detail : e.Detail.Trim())];

    private static List<int> Selected(ListBox list) => [.. list.SelectedItems.Cast<object>().Select(list.Items.IndexOf)];

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
