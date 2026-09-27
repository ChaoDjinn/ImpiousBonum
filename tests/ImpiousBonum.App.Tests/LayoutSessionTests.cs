using System.Text.Json.Nodes;
using System.Windows;
using ImpiousBonum.App.Editor;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;

namespace ImpiousBonum.App.Tests;

public sealed class LayoutSessionTests
{
    private static LayoutSession Session() => new(new LayoutDocument
    {
        Widgets =
        [
            new JsonObject { ["type"] = "text", ["x"] = 0, ["y"] = 0, ["width"] = 100, ["height"] = 50, ["text"] = "A" },
            new JsonObject { ["type"] = "text", ["x"] = 200, ["y"] = 0, ["width"] = 100, ["height"] = 50, ["text"] = "B" },
        ],
    });

    private static string Text(LayoutSession session, int index) => session.Document.Widgets[index].GetString("text")!;

    [Fact]
    public void Starts_clean_and_becomes_dirty_on_edit()
    {
        var session = Session();
        Assert.False(session.IsDirty);
        session.SetValue(SettingTarget.Widget(0), "text", JsonValue.Create("C"), null);
        Assert.True(session.IsDirty);
        session.MarkSaved();
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Undo_and_redo_restore_exact_states()
    {
        var session = Session();
        session.SetValue(SettingTarget.Widget(0), "text", JsonValue.Create("changed"), null);
        session.Undo();
        Assert.Equal("A", Text(session, 0));
        Assert.False(session.IsDirty);
        session.Redo();
        Assert.Equal("changed", Text(session, 0));
    }

    [Fact]
    public void Typing_in_one_field_is_one_undo_step()
    {
        var session = Session();
        foreach (var text in new[] { "H", "He", "Hel", "Hello" })
            session.SetValue(SettingTarget.Widget(0), "text", JsonValue.Create(text), null);

        session.Undo();
        Assert.Equal("A", Text(session, 0));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Separate_fields_are_separate_undo_steps()
    {
        var session = Session();
        session.SetValue(SettingTarget.Widget(0), "text", JsonValue.Create("X"), null);
        session.SetValue(SettingTarget.Widget(1), "text", JsonValue.Create("Y"), null);

        session.Undo();
        Assert.Equal("X", Text(session, 0));
        Assert.Equal("B", Text(session, 1));
    }

    [Fact]
    public void A_drag_is_one_undo_step_and_reports_geometry_changes()
    {
        var session = Session();
        var kinds = new List<ChangeKind>();
        session.Changed += (_, change) => kinds.Add(change.Kind);

        for (var x = 10; x <= 50; x += 10)
            session.SetGeometry(0, new Rect(x, 5, 100, 50), "drag:1", null);

        Assert.All(kinds, k => Assert.Equal(ChangeKind.Geometry, k));
        Assert.Equal(50, session.Document.Widgets[0].GetDouble("x", 0));
        session.Undo();
        Assert.Equal(0, session.Document.Widgets[0].GetDouble("x", -1));
    }

    [Fact]
    public void Setting_null_removes_the_value_so_the_default_applies()
    {
        var session = Session();
        session.SetValue(SettingTarget.Widget(0), "fontSize", JsonValue.Create(20), null);
        session.SetValue(SettingTarget.Widget(0), "fontSize", null, null);
        Assert.Null(session.GetValue(SettingTarget.Widget(0), "fontSize"));
    }

    [Fact]
    public void Add_duplicate_delete_and_reorder_keep_the_selection_sensible()
    {
        var session = Session();

        session.AddWidget(ClockWidget.Descriptor);
        Assert.Equal(2, session.SelectedIndex);
        Assert.Equal("clock", session.SelectedWidget!.GetString("type"));

        session.Select(0);
        session.DuplicateSelected();
        Assert.Equal(1, session.SelectedIndex);
        Assert.Equal("A", Text(session, 1));
        Assert.Equal(20, session.SelectedWidget!.GetDouble("x", 0));

        session.MoveSelected(+1);
        Assert.Equal(2, session.SelectedIndex);
        Assert.Equal("A", Text(session, 2));
        Assert.Equal("B", Text(session, 1));

        session.DeleteSelected();
        Assert.Equal(-1, session.SelectedIndex);
        Assert.Equal(3, session.Document.Widgets.Count);
    }

    [Fact]
    public void Undo_past_a_delete_clears_a_selection_that_no_longer_exists()
    {
        var session = Session();
        session.AddWidget(TextWidget.Descriptor);
        session.Undo();
        Assert.Equal(-1, session.SelectedIndex);
    }

    [Fact]
    public void Theme_and_canvas_edits_round_trip()
    {
        var session = Session();
        session.SetValue(SettingTarget.Theme, "accent", JsonValue.Create("#00FF00"), null);
        session.SetValue(SettingTarget.Canvas, "width", JsonValue.Create(800), null);

        Assert.Equal("#00FF00", session.Document.Theme.Accent);
        Assert.Equal(800, session.Document.Width);

        // Resetting a theme colour restores the built-in default.
        session.SetValue(SettingTarget.Theme, "accent", null, null);
        Assert.Equal(new ThemeSettings().Accent, session.Document.Theme.Accent);
    }

    [Fact]
    public void List_items_can_be_added_and_edited()
    {
        var session = new LayoutSession(new LayoutDocument { Widgets = [new JsonObject { ["type"] = "rows", ["x"] = 0, ["y"] = 0, ["width"] = 10, ["height"] = 10 }] });

        session.EditItems(0, "rows", items => items.Add(new JsonObject()));
        session.SetValue(SettingTarget.Item(0, "rows", 0), "label", JsonValue.Create("Ping"), null);

        var rows = Assert.IsType<JsonArray>(session.Document.Widgets[0]["rows"]);
        Assert.Equal("Ping", rows[0]!["label"]!.GetValue<string>());
    }

    [Fact]
    public void Writing_the_current_value_is_not_an_edit()
    {
        var session = Session();
        var changes = 0;
        session.Changed += (_, _) => changes++;

        session.SetValue(SettingTarget.Widget(0), "text", JsonValue.Create("A"), null);
        session.SetValue(SettingTarget.Theme, "accent", JsonValue.Create(session.Document.Theme.Accent), null);
        session.SetGeometry(0, new Rect(0, 0, 100, 50), "drag:1", null);

        Assert.Equal(0, changes);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Whole_numbers_are_stored_as_integers() =>
        Assert.Equal("12", LayoutSession.Number(12.0).ToJsonString());
}
