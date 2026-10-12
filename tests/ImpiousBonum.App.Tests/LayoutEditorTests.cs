using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
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
}
