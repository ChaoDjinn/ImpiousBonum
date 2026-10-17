using System.Text.Json.Nodes;
using ImpiousBonum.App.Editor;
using ImpiousBonum.App.Layout;

namespace ImpiousBonum.App.Tests;

public sealed class LayoutGroupsTests
{
    /// <summary>Text widgets A, B, C and D, drawn in that order.</summary>
    private static LayoutSession FourWidgets() => new(new LayoutDocument
    {
        Widgets = [.. "ABCD".Select(c => new JsonObject { ["type"] = "text", ["x"] = 0, ["y"] = 0, ["width"] = 10, ["height"] = 10, ["text"] = c.ToString() })],
    });

    /// <summary>The drawing order, back to front, with each widget's group after it.</summary>
    private static string Order(LayoutSession session) =>
        string.Join(" ", session.Document.Widgets.Select(w => w.GetString("text") + (w.GetString(LayoutSession.GroupKey) is { } g ? $":{g}" : "")));

    private static string Texts(LayoutSession session, IEnumerable<int> indices) => string.Concat(indices.Select(i => session.Document.Widgets[i].GetString("text")));

    [Fact]
    public void Grouping_puts_the_widgets_together_where_the_topmost_is_and_keeps_them_selected()
    {
        var session = FourWidgets();
        session.Select([2, 0]);

        session.GroupSelected();

        // A comes up to just under C; B and D stay where they were among the rest.
        Assert.Equal("B A:Group 1 C:Group 1 D", Order(session));
        Assert.Equal("CA", Texts(session, session.Selection));
        Assert.Equal("Group 1", session.SelectedGroup);
        session.Undo();
        Assert.Equal("A B C D", Order(session));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Grouping_needs_two_and_each_new_group_has_a_name_of_its_own()
    {
        var session = FourWidgets();
        session.Select(0);
        session.GroupSelected();
        Assert.False(session.CanUndo);

        session.Select([0, 1]);
        session.GroupSelected();
        session.Select([2, 3]);
        session.GroupSelected();

        Assert.Equal("A:Group 1 B:Group 1 C:Group 2 D:Group 2", Order(session));
        Assert.Equal("Group 3", session.NewGroupName());
    }

    [Fact]
    public void Clicking_a_grouped_widget_selects_its_group_then_on_its_own()
    {
        var session = FourWidgets();
        session.Select([1, 2]);
        session.GroupSelected();
        session.Select(-1);

        session.SelectWithGroup(2);
        Assert.Equal("CB", Texts(session, session.Selection));

        // With one of the group on its own, another of it is on its own too.
        session.Select(2);
        session.SelectWithGroup(1);
        Assert.Equal("B", Texts(session, session.Selection));

        // A widget in no group is just itself.
        session.SelectWithGroup(3);
        Assert.Equal("D", Texts(session, session.Selection));
    }

    [Fact]
    public void Ungrouping_and_renaming_leave_the_widgets_where_they_are()
    {
        var session = FourWidgets();
        session.Select([1, 2]);
        session.GroupSelected("Weather");

        Assert.Equal("There's a group called that already.", session.CheckGroupName("weather"));
        Assert.Null(session.CheckGroupName("weather", "Weather"));
        Assert.Equal("A group needs a name.", session.CheckGroupName("  "));
        session.RenameGroup("Weather", " Forecast ");
        Assert.Equal("A B:Forecast C:Forecast D", Order(session));

        // Ungrouping one of it ungroups all of it.
        session.Select(1);
        session.UngroupSelected();
        Assert.Equal("A B C D", Order(session));
        Assert.Null(session.SelectedGroup);
    }

    [Fact]
    public void Duplicating_a_whole_group_makes_a_new_group_above_it()
    {
        var session = FourWidgets();
        session.Select([1, 2]);
        session.GroupSelected("Weather");

        session.DuplicateSelected();

        Assert.Equal("A B:Weather C:Weather B:Weather copy C:Weather copy D", Order(session));
        Assert.Equal([3, 4], session.Selection);
        Assert.Equal("Weather copy", session.SelectedGroup);

        // Part of a group copied stays in it.
        session.Select(1);
        session.DuplicateSelected();
        Assert.Equal("A B:Weather B:Weather C:Weather B:Weather copy C:Weather copy D", Order(session));
    }

    [Fact]
    public void Restacking_reorders_and_regroups_as_one_step()
    {
        var session = FourWidgets();

        // D to the back, in a group with A.
        session.Restack([3, 0, 1, 2], ["W", null, null, "W"], [3]);

        Assert.Equal("D:W A:W B C", Order(session));
        Assert.Equal([0], session.Selection);
        session.Undo();
        Assert.Equal("A B C D", Order(session));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void A_group_is_not_an_unknown_setting()
    {
        var document = new LayoutDocument
        {
            Widgets = [new JsonObject { ["type"] = "text", ["x"] = 0, ["y"] = 0, ["width"] = 10, ["height"] = 10, ["group"] = "Weather" }],
        };

        Assert.Empty(LayoutValidator.Validate(document));
    }
}
