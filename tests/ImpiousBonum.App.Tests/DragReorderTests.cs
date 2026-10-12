using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using ImpiousBonum.App.Controls;

namespace ImpiousBonum.App.Tests;

public sealed class DragReorderTests
{
    /// <summary>Three entries 30 high with 10 between them: 0–30, 40–70 and 80–110.</summary>
    private static readonly DragReorder.Place[] Entries = [new(0, 0, 30), new(1, 40, 70), new(2, 80, 110)];

    [Theory]
    [InlineData(-20, 0)] // above the first
    [InlineData(10, 0)] // the top half of the first: before it
    [InlineData(20, 1)] // the bottom half of the first: after it
    [InlineData(35, 1)] // between the first and second
    [InlineData(60, 2)]
    [InlineData(90, 2)]
    [InlineData(100, 3)] // the bottom half of the last: after it
    [InlineData(500, 3)] // below the last
    public void The_gap_is_before_an_entry_above_its_middle_and_after_it_below(double y, int gap) =>
        Assert.Equal(gap, DragReorder.GapAt(Entries, y));

    [Theory]
    [InlineData(0, 0, 0)] // dropped where it was
    [InlineData(0, 1, 0)] // just below itself: still where it was
    [InlineData(0, 2, 1)] // down past the next entry
    [InlineData(0, 3, 2)] // to the end
    [InlineData(1, 0, 0)] // up to the top
    [InlineData(1, 3, 2)]
    [InlineData(2, 0, 0)] // the last to the top
    [InlineData(2, 2, 2)] // just above itself
    [InlineData(2, 3, 2)] // just below itself
    public void Dropping_in_a_gap_puts_the_entry_there(int from, int gap, int to) =>
        Assert.Equal(to, DragReorder.DropIndex(from, gap));

    [Fact]
    public void Dragging_down_past_itself_counts_the_place_it_left()
    {
        // The first entry dropped on the bottom half of the second goes after it, so second.
        Assert.Equal(1, DragReorder.DropIndex(0, DragReorder.GapAt(Entries, 65)));
        // And below the last, last.
        Assert.Equal(2, DragReorder.DropIndex(0, DragReorder.GapAt(Entries, 105)));
    }

    [Fact]
    public void Dragging_up_above_the_first_makes_it_first() =>
        Assert.Equal(0, DragReorder.DropIndex(2, DragReorder.GapAt(Entries, 5)));

    [Fact]
    public void Entries_a_list_has_not_drawn_are_left_out()
    {
        // A virtualising list scrolled down, showing only entries 5 and 6.
        DragReorder.Place[] shown = [new(5, 0, 30), new(6, 30, 60)];
        Assert.Equal(5, DragReorder.GapAt(shown, 10));
        Assert.Equal(6, DragReorder.GapAt(shown, 20));
        Assert.Equal(7, DragReorder.GapAt(shown, 50));
        Assert.Equal(0, DragReorder.GapAt([], 50));
    }

    [Fact]
    public void The_line_goes_between_entries_or_at_the_ends()
    {
        Assert.Equal(0, DragReorder.LineAt(Entries, 0));
        Assert.Equal(35, DragReorder.LineAt(Entries, 1));
        Assert.Equal(75, DragReorder.LineAt(Entries, 2));
        Assert.Equal(110, DragReorder.LineAt(Entries, 3));
    }

    [Fact]
    public void It_scrolls_only_near_the_edges_and_faster_nearer_them()
    {
        Assert.Equal(0, DragReorder.ScrollStep(200, 400));
        Assert.True(DragReorder.ScrollStep(10, 400) < 0);
        Assert.True(DragReorder.ScrollStep(390, 400) > 0);
        Assert.True(DragReorder.ScrollStep(2, 400) < DragReorder.ScrollStep(20, 400));
        // Past the edge is as fast as it goes, either way.
        Assert.Equal(DragReorder.ScrollStep(-50, 400), DragReorder.ScrollStep(-500, 400));
        Assert.Equal(-DragReorder.ScrollStep(-50, 400), DragReorder.ScrollStep(450, 400));
        Assert.Equal(0, DragReorder.ScrollStep(0, 0));
    }

    [Theory]
    [InlineData(Key.Up, ModifierKeys.Alt, -1)]
    [InlineData(Key.Down, ModifierKeys.Alt, 1)]
    [InlineData(Key.Up, ModifierKeys.None, 0)] // plain arrows belong to whatever has focus
    [InlineData(Key.Down, ModifierKeys.Alt | ModifierKeys.Shift, 0)]
    [InlineData(Key.Left, ModifierKeys.Alt, 0)]
    public void Alt_up_and_down_move_an_entry_a_place(Key key, ModifierKeys modifiers, int step) =>
        Assert.Equal(step, DragReorder.KeyStep(key, modifiers));

    [Fact]
    public void Dropping_a_dragged_entry_reports_where_it_goes()
    {
        Sta.Run(() =>
        {
            var (panel, window) = ShowStack();
            var moves = new List<(int, int)>();
            var reorder = DragReorder.Attach(panel, (from, to) => moves.Add((from, to)));

            // The first of three 30-high entries, dropped on the lower half of the last.
            Assert.True(reorder.Begin((FrameworkElement)panel.Children[0]));
            var line = AdornerLayer.GetAdornerLayer(panel)!.GetAdorners(panel);
            reorder.Update(new Point(50, 80));
            reorder.End(drop: true);
            var after = AdornerLayer.GetAdornerLayer(panel)!.GetAdorners(panel);
            window.Close();

            Assert.Equal([(0, 2)], moves);
            Assert.Single(line!);
            Assert.Null(after);
            Assert.Equal(1.0, panel.Children[0].Opacity);
        });
    }

    [Fact]
    public void A_drop_where_it_started_or_off_to_the_side_moves_nothing()
    {
        Sta.Run(() =>
        {
            var (panel, window) = ShowStack();
            var moves = new List<(int, int)>();
            var reorder = DragReorder.Attach(panel, (from, to) => moves.Add((from, to)));

            reorder.Begin((FrameworkElement)panel.Children[1]);
            reorder.Update(new Point(50, 55));
            reorder.End(drop: true);

            reorder.Begin((FrameworkElement)panel.Children[1]);
            reorder.Update(new Point(-200, 5));
            reorder.End(drop: true);
            window.Close();

            Assert.Empty(moves);
        });
    }

    [Fact]
    public void Esc_or_losing_the_mouse_cancels_a_drag()
    {
        Sta.Run(() =>
        {
            var (panel, window) = ShowStack();
            var moves = new List<(int, int)>();
            var reorder = DragReorder.Attach(panel, (from, to) => moves.Add((from, to)));

            reorder.Begin((FrameworkElement)panel.Children[0]);
            reorder.Update(new Point(50, 80));
            window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            var cancelledByEsc = !reorder.IsDragging;

            reorder.Begin((FrameworkElement)panel.Children[0]);
            reorder.Update(new Point(50, 80));
            Mouse.Capture(null);
            var cancelledByCapture = !reorder.IsDragging;
            reorder.End(drop: true);
            window.Close();

            Assert.True(cancelledByEsc);
            Assert.True(cancelledByCapture);
            Assert.Empty(moves);
        });
    }

    [Fact]
    public void A_list_box_entry_can_be_dragged_too()
    {
        Sta.Run(() =>
        {
            var list = new ListBox { ItemsSource = new[] { "A", "B", "C" } };
            var window = Offscreen(list);
            var moves = new List<(int, int)>();
            var reorder = DragReorder.Attach(list, (from, to) => moves.Add((from, to)));
            var first = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(0);
            var last = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(2);

            // The last entry dropped on the upper half of the first.
            Assert.True(reorder.Begin(last));
            reorder.Update(new Point(10, first.TranslatePoint(new Point(), list).Y + 2));
            reorder.End(drop: true);
            window.Close();

            Assert.Equal([(2, 0)], moves);
        });
    }

    /// <summary>Three entries 30 high, 0–30, 30–60 and 60–90, in a window off screen.</summary>
    private static (StackPanel Panel, Window Window) ShowStack()
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        for (var i = 0; i < 3; i++)
            panel.Children.Add(new Border { Height = 30, Child = new TextBlock { Text = $"Entry {i + 1}" } });
        return (panel, Offscreen(panel));
    }

    private static Window Offscreen(FrameworkElement content)
    {
        var window = new Window
        {
            Content = content, Width = 300, Height = 300, Left = -30000, Top = -30000,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
        };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    [Fact]
    public void Moving_from_the_keyboard_reports_the_entry_with_focus()
    {
        Sta.Run(() =>
        {
            var panel = new StackPanel();
            for (var i = 0; i < 3; i++)
                panel.Children.Add(new Border { Child = new TextBox() });
            var moves = new List<(int, int)>();
            DragReorder.Attach(panel, (from, to) => moves.Add((from, to)));

            var inSecond = ((Border)panel.Children[1]).Child;
            Assert.True(DragReorder.Move(inSecond, -1));
            Assert.True(DragReorder.Move(inSecond, +1));
            // Already at the top, and not in a list at all.
            Assert.False(DragReorder.Move(((Border)panel.Children[0]).Child, -1));
            Assert.False(DragReorder.Move(new TextBox(), +1));

            Assert.Equal([(1, 0), (1, 2)], moves);
        });
    }
}
