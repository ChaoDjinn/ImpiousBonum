using ImpiousBonum.App.Editor;

namespace ImpiousBonum.App.Tests;

public sealed class WidgetRowsTests
{
    /// <summary>
    /// Widgets A to E, back to front, with B, C and D in the group W: the list reads E, W (D, C, B), A.
    /// </summary>
    private static readonly string?[] Grouped = [null, "W", "W", "W", null];

    private static string Rows(IReadOnlyList<string?> groups) => string.Join(" ", WidgetRows.For(groups).Select(r => r.IsGroup ? $"[{r.Group}]" : Name(r.Widget)));

    private static string Name(int widget) => ((char)('A' + widget)).ToString();

    /// <summary>The drawing order, back to front, with each widget's group after it.</summary>
    private static string After((List<int> Order, string?[] Groups)? moved) =>
        moved is not var (order, groups) ? "unchanged" : string.Join(" ", order.Select(i => Name(i) + (groups[i] is { } g ? $":{g}" : "")));

    [Fact]
    public void The_list_is_top_first_with_each_group_under_its_own_row()
    {
        Assert.Equal("E [W] D C B A", Rows(Grouped));
        Assert.Equal("C B A", Rows([null, null, null]));
        // Two groups one on another each have their row.
        Assert.Equal("[X] D C [W] B A", Rows(["W", "W", "X", "X"]));
    }

    [Fact]
    public void A_widget_dropped_among_a_groups_widgets_or_just_under_its_row_joins_it()
    {
        // E, from the top, to between D and C.
        Assert.Equal("A B:W C:W E:W D:W", After(WidgetRows.Move(Grouped, 0, 2)));
        // E to just under the group's row.
        Assert.Equal("A B:W C:W D:W E:W", After(WidgetRows.Move(Grouped, 0, 1)));
        // A, from the bottom, to between C and B.
        Assert.Equal("B:W A:W C:W D:W E", After(WidgetRows.Move(Grouped, 5, 4)));
    }

    [Fact]
    public void A_widget_dropped_outside_its_group_leaves_it()
    {
        // D (under the group's row) up a place, as Alt+Up does: above the group's row, so out of it, though still drawn where it was.
        Assert.Equal("A B:W C:W D E", After(WidgetRows.Move(Grouped, 2, 1)));
        // B (the group's last) to the bottom, under A.
        Assert.Equal("B A C:W D:W E", After(WidgetRows.Move(Grouped, 4, 5)));
        // Moving within the group keeps it in: D below C.
        Assert.Equal("A B:W D:W C:W E", After(WidgetRows.Move(Grouped, 2, 3)));
    }

    [Fact]
    public void A_groups_row_takes_its_widgets_with_it()
    {
        // Dragged to the bottom, below A.
        Assert.Equal("B:W C:W D:W A E", After(WidgetRows.Move(Grouped, 1, 5)));
        // Up a place, above E.
        Assert.Equal("A E B:W C:W D:W", After(WidgetRows.Move(Grouped, 1, 0)));
        // Down a place, as Alt+Down does: past A, not into itself.
        Assert.Equal("B:W C:W D:W A E", After(WidgetRows.Move(Grouped, 1, 2)));
        // Into itself, by dragging: nothing.
        Assert.Equal("unchanged", After(WidgetRows.Move(Grouped, 1, 3)));
    }

    [Fact]
    public void A_groups_row_goes_above_or_below_another_group_rather_than_into_it()
    {
        // Back to front A, B (W), C, D (X); the list reads [X] D C [W] B A.
        string?[] two = ["W", "W", "X", "X"];
        // W's row dragged up to between D and C: above X, going up.
        Assert.Equal("C:X D:X A:W B:W", After(WidgetRows.Move(two, 3, 1)));
        // X's row dragged down to between B and A: below W, going down.
        Assert.Equal("C:X D:X A:W B:W", After(WidgetRows.Move(two, 0, 4)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 6)]
    public void Moving_nowhere_or_off_the_list_does_nothing(int from, int to) =>
        Assert.Null(WidgetRows.Move(Grouped, from, to));
}
