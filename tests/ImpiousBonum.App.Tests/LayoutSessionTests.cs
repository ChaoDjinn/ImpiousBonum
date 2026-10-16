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

    /// <summary>Text widgets A, B, C and D, drawn in that order.</summary>
    private static LayoutSession FourWidgets() => new(new LayoutDocument
    {
        Widgets = [.. "ABCD".Select(c => new JsonObject { ["type"] = "text", ["x"] = 0, ["y"] = 0, ["width"] = 10, ["height"] = 10, ["text"] = c.ToString() })],
    });

    private static string Order(LayoutSession session) => string.Concat(session.Document.Widgets.Select(w => w.GetString("text")));

    [Fact]
    public void Moving_a_widget_changes_the_drawing_order_and_selects_it()
    {
        var session = FourWidgets();
        session.Select(3);

        // Dragged from the top of the list to below the third: drawn after B and C, so on top of them.
        session.MoveWidget(0, 2);
        Assert.Equal("BCAD", Order(session));
        Assert.Equal(2, session.SelectedIndex);

        // And back up to the top.
        session.MoveWidget(3, 0);
        Assert.Equal("DBCA", Order(session));
        Assert.Equal("D", session.SelectedWidget!.GetString("text"));
    }

    [Fact]
    public void Moving_a_widget_tells_views_what_is_selected_once_it_has_moved()
    {
        var session = FourWidgets();
        string? selectedWhenChanged = null;
        var selectionChanges = 0;
        session.Changed += (_, change) =>
        {
            Assert.Equal(ChangeKind.Structure, change.Kind);
            selectedWhenChanged = session.SelectedWidget?.GetString("text");
        };
        session.SelectionChanged += (_, _) => selectionChanges++;

        session.MoveWidget(1, 3);

        Assert.Equal("B", selectedWhenChanged);
        Assert.Equal(1, selectionChanges);
    }

    [Fact]
    public void Moving_a_widget_is_one_undo_step()
    {
        var session = FourWidgets();
        session.MoveWidget(0, 3);

        session.Undo();
        Assert.Equal("ABCD", Order(session));
        Assert.False(session.CanUndo);
        Assert.False(session.IsDirty);

        session.Redo();
        Assert.Equal("BCDA", Order(session));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(4, 0)]
    [InlineData(0, 4)]
    public void Moving_a_widget_nowhere_or_off_the_list_does_nothing(int from, int to)
    {
        var session = FourWidgets();
        session.Select(2);
        var changes = 0;
        session.Changed += (_, _) => changes++;

        session.MoveWidget(from, to);

        Assert.Equal("ABCD", Order(session));
        Assert.Equal(0, changes);
        Assert.False(session.CanUndo);
        Assert.Equal(2, session.SelectedIndex);
    }

    [Fact]
    public void Bringing_forward_and_sending_back_stop_at_the_ends()
    {
        var session = FourWidgets();
        session.Select(3);
        session.MoveSelected(+1);
        Assert.Equal("ABCD", Order(session));
        Assert.False(session.CanUndo);

        session.MoveSelected(-1);
        Assert.Equal("ABDC", Order(session));
        Assert.Equal(2, session.SelectedIndex);
    }

    // ---- Selecting several -------------------------------------------------------------------------

    /// <summary>Text widgets A, B, C and D of different sizes in different places.</summary>
    private static LayoutSession Scattered() => new(new LayoutDocument
    {
        Widgets =
        [
            Text("A", 10, 10, 100, 50),
            Text("B", 200, 40, 60, 20),
            Text("C", 120, 100, 30, 90),
            Text("D", 400, 300, 50, 50),
        ],
    });

    private static JsonObject Text(string text, double x, double y, double width, double height) =>
        new() { ["type"] = "text", ["x"] = x, ["y"] = y, ["width"] = width, ["height"] = height, ["text"] = text };

    private static Rect Geometry(LayoutSession session, int index) => LayoutSession.GeometryOf(session.Document.Widgets[index]);

    [Fact]
    public void Several_widgets_can_be_selected_in_the_order_they_were_picked()
    {
        var session = FourWidgets();
        var changes = 0;
        session.SelectionChanged += (_, _) => changes++;

        session.Select([2, 0]);
        Assert.Equal([2, 0], session.Selection);
        Assert.Equal(2, session.SelectedIndex);

        session.ToggleSelected(1);
        Assert.Equal([2, 0, 1], session.Selection);
        session.ToggleSelected(2);
        Assert.Equal([0, 1], session.Selection);
        Assert.Equal(0, session.SelectedIndex);
        Assert.True(session.IsSelected(1));
        Assert.False(session.IsSelected(2));

        // Ones that don't exist, and repeats, are left out; selecting what's selected already isn't a change.
        session.Select([7, -1, 3, 3]);
        Assert.Equal([3], session.Selection);
        session.Select(3);
        Assert.Equal(4, changes);

        session.Select(-1);
        Assert.Empty(session.Selection);
        Assert.Equal(-1, session.SelectedIndex);
    }

    [Fact]
    public void Several_widgets_move_as_one_undo_step_and_views_hear_about_each()
    {
        var session = Scattered();
        var changes = new List<(ChangeKind, int?)>();
        session.Changed += (_, change) => changes.Add((change.Kind, change.WidgetIndex));

        // A drag of two widgets, as the editor's preview does it: the same merge key all the way.
        for (var moved = 1; moved <= 3; moved++)
            session.SetGeometry([(0, new Rect(10 + moved, 10, 100, 50)), (2, new Rect(120 + moved, 100, 30, 90))], "drag:1", null);

        (ChangeKind, int?)[] step = [(ChangeKind.Geometry, 0), (ChangeKind.Geometry, 2)];
        Assert.Equal([.. step, .. step, .. step], changes);
        Assert.Equal((13.0, 123.0), (Geometry(session, 0).X, Geometry(session, 2).X));
        session.Undo();
        Assert.Equal((10.0, 120.0), (Geometry(session, 0).X, Geometry(session, 2).X));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Nudging_several_moves_them_all_and_a_run_of_nudges_is_one_undo_step()
    {
        var session = Scattered();
        session.Select([1, 3]);
        session.NudgeSelected(1, 0);
        session.NudgeSelected(1, 0);
        session.NudgeSelected(0, -10);

        Assert.Equal(new Rect(202, 30, 60, 20), Geometry(session, 1));
        Assert.Equal(new Rect(402, 290, 50, 50), Geometry(session, 3));
        Assert.Equal(new Rect(10, 10, 100, 50), Geometry(session, 0));
        session.Undo();
        Assert.Equal(new Rect(200, 40, 60, 20), Geometry(session, 1));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Duplicating_several_copies_each_just_above_itself_and_selects_the_copies()
    {
        var session = FourWidgets();
        session.Select([2, 0]);
        session.DuplicateSelected();

        Assert.Equal("AABCCD", Order(session));
        // The first picked's copy first, still.
        Assert.Equal([4, 1], session.Selection);
        Assert.All(session.Selection, i => Assert.Equal(new Rect(20, 20, 10, 10), Geometry(session, i)));
        session.Undo();
        Assert.Equal("ABCD", Order(session));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Deleting_several_is_one_undo_step()
    {
        var session = FourWidgets();
        session.Select([3, 1]);
        session.DeleteSelected();

        Assert.Equal("AC", Order(session));
        Assert.Empty(session.Selection);
        session.Undo();
        Assert.Equal("ABCD", Order(session));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Bringing_forward_needs_just_one_widget_selected()
    {
        var session = FourWidgets();
        session.Select([0, 1]);
        session.MoveSelected(+1);

        Assert.Equal("ABCD", Order(session));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Aligning_the_selected_widgets_is_one_undo_step()
    {
        var session = Scattered();
        session.Select([0, 1, 2]);
        var changes = 0;
        session.Changed += (_, _) => changes++;

        session.ArrangeSelected(Arrangement.AlignLeft);

        Assert.All([0, 1, 2], i => Assert.Equal(10, Geometry(session, i).X));
        // The one not selected stays where it was.
        Assert.Equal(400, Geometry(session, 3).X);
        // A view hears of each that moved: the first was already there.
        Assert.Equal(2, changes);
        Assert.Equal([0, 1, 2], session.Selection);

        session.Undo();
        Assert.Equal([10.0, 200, 120], new[] { 0, 1, 2 }.Select(i => Geometry(session, i).X));
        Assert.False(session.CanUndo);
        session.Redo();
        Assert.Equal(10, Geometry(session, 1).X);
    }

    [Fact]
    public void Same_width_matches_the_widget_picked_first()
    {
        var session = Scattered();
        session.Select([2, 0, 1]);
        session.ArrangeSelected(Arrangement.SameWidth);

        Assert.Equal([30.0, 30, 30], new[] { 0, 1, 2 }.Select(i => Geometry(session, i).Width));
    }

    [Fact]
    public void Arranging_too_few_widgets_changes_nothing()
    {
        var session = Scattered();
        var changes = 0;
        session.Changed += (_, _) => changes++;

        session.Select(1);
        session.ArrangeSelected(Arrangement.AlignLeft);
        session.ArrangeSelected(Arrangement.SameHeight);
        session.Select([0, 1]);
        session.ArrangeSelected(Arrangement.DistributeHorizontally);
        session.Select(-1);
        session.ArrangeSelected(Arrangement.AlignTop);

        Assert.Equal(0, changes);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Undo_keeps_the_selected_widgets_the_layout_still_has()
    {
        var session = FourWidgets();
        session.AddWidget(TextWidget.Descriptor);
        session.Select([4, 1]);

        session.Undo();
        Assert.Equal([1], session.Selection);
    }

    private static LayoutSession RowsSession(params string[] labels) => new(new LayoutDocument
    {
        Widgets =
        [
            new JsonObject
            {
                ["type"] = "rows", ["x"] = 0, ["y"] = 0, ["width"] = 10, ["height"] = 10,
                ["rows"] = new JsonArray([.. labels.Select(l => (JsonNode)new JsonObject { ["label"] = l })]),
            },
        ],
    });

    private static string Rows(LayoutSession session) =>
        string.Concat(((JsonArray)session.Document.Widgets[0]["rows"]!).Select(r => r!["label"]!.GetValue<string>()));

    [Fact]
    public void Moving_a_list_entry_reorders_the_list_as_one_undo_step()
    {
        var session = RowsSession("A", "B", "C");

        session.MoveItem(0, "rows", 0, 2);
        Assert.Equal("BCA", Rows(session));
        session.MoveItem(0, "rows", 2, 1);
        Assert.Equal("BAC", Rows(session));

        session.Undo();
        Assert.Equal("BCA", Rows(session));
        session.Undo();
        Assert.Equal("ABC", Rows(session));
        Assert.False(session.CanUndo);

        session.Redo();
        Assert.Equal("BCA", Rows(session));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(0, 3)]
    [InlineData(-1, 0)]
    public void Moving_a_list_entry_nowhere_does_nothing(int from, int to)
    {
        var session = RowsSession("A", "B", "C");
        session.MoveItem(0, "rows", from, to);
        session.MoveItem(0, "missing", 0, 1);
        session.MoveItem(5, "rows", 0, 1);

        Assert.Equal("ABC", Rows(session));
        Assert.False(session.CanUndo);
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

        Assert.Equal("#00FF00", session.ResolvedTheme.Accent);
        Assert.Equal(800, session.Document.Width);

        // Resetting a theme colour restores the built-in default.
        session.SetValue(SettingTarget.Theme, "accent", null, null);
        Assert.Equal(new ThemeSettings().Accent, session.ResolvedTheme.Accent);
        Assert.Empty(session.Document.Theme);
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
    public void New_widgets_start_with_their_default_list_entries()
    {
        var session = Session();
        session.AddWidget(BarsWidget.ClaudeDescriptor);

        var bars = Assert.IsType<JsonArray>(session.SelectedWidget!["bars"]);
        Assert.Equal(["claude.session", "claude.week"], bars.Select(b => b!["metric"]!.GetValue<string>()));
    }

    [Fact]
    public void Editing_a_list_left_out_of_the_file_starts_from_its_defaults()
    {
        var session = new LayoutSession(new LayoutDocument { Widgets = [new JsonObject { ["type"] = "claude", ["x"] = 0, ["y"] = 0, ["width"] = 10, ["height"] = 10 }] });

        session.EditItems(0, "bars", items => items.RemoveAt(1));

        var bars = Assert.IsType<JsonArray>(session.Document.Widgets[0]["bars"]);
        Assert.Equal("Session", Assert.Single(bars)!["label"]!.GetValue<string>());
    }

    [Fact]
    public void Writing_the_current_value_is_not_an_edit()
    {
        var session = Session();
        var changes = 0;
        session.Changed += (_, _) => changes++;

        session.SetValue(SettingTarget.Widget(0), "text", JsonValue.Create("A"), null);
        session.SetValue(SettingTarget.Theme, "accent", null, null);
        session.SetGeometry(0, new Rect(0, 0, 100, 50), "drag:1", null);

        Assert.Equal(0, changes);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Switching_layouts_starts_clean_with_no_undo_history()
    {
        var session = Session();
        session.SetValue(SettingTarget.Widget(0), "text", JsonValue.Create("edited"), null);
        session.Select(1);

        session.Reset(new LayoutDocument { Widgets = [new JsonObject { ["type"] = "text", ["x"] = 0, ["y"] = 0, ["width"] = 10, ["height"] = 10, ["text"] = "Other" }] });

        Assert.Equal("Other", Text(session, 0));
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.Equal(-1, session.SelectedIndex);
    }

    [Fact]
    public void Whole_numbers_are_stored_as_integers() =>
        Assert.Equal("12", LayoutSession.Number(12.0).ToJsonString());

    // ---- Typing and runs of edits ----------------------------------------------------------------

    [Fact]
    public void Typing_after_a_pause_is_a_new_undo_step()
    {
        var clock = new FakeClock();
        var session = new LayoutSession(Session().Document, clock: clock);
        session.SetValue(SettingTarget.Widget(0), "text", JsonValue.Create("H"), null);
        clock.Advance(TimeSpan.FromSeconds(10));
        session.SetValue(SettingTarget.Widget(0), "text", JsonValue.Create("Hi"), null);

        session.Undo();
        Assert.Equal("H", Text(session, 0));
    }

    [Fact]
    public void A_run_of_edits_is_one_undo_step_however_long_the_pauses()
    {
        var clock = new FakeClock();
        var session = new LayoutSession(Session().Document, clock: clock);
        foreach (var font in new[] { "Arial", "Bahnschrift", "Calibri" })
        {
            session.SetValue(SettingTarget.Widget(0), "fontFamily", JsonValue.Create(font), null, run: "1");
            clock.Advance(TimeSpan.FromSeconds(10));
        }

        Assert.Equal("Calibri", session.Document.Widgets[0].GetString("fontFamily"));
        session.Undo();
        Assert.Null(session.Document.Widgets[0]["fontFamily"]);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Another_edit_ends_a_run()
    {
        var session = Session();
        session.SetValue(SettingTarget.Widget(0), "fontFamily", JsonValue.Create("Arial"), null, run: "1");
        session.SetValue(SettingTarget.Widget(1), "text", JsonValue.Create("Y"), null);
        session.SetValue(SettingTarget.Widget(0), "fontFamily", JsonValue.Create("Calibri"), null, run: "1");

        session.Undo();
        Assert.Equal("Arial", session.Document.Widgets[0].GetString("fontFamily"));
        Assert.Equal("Y", Text(session, 1));
    }

    // ---- Previews --------------------------------------------------------------------------------

    private static string Json(LayoutSession session) => System.Text.Json.JsonSerializer.Serialize(session.Document, JsonDefaults.Options);

    private static string? Font(LayoutSession session, int index) => session.Document.Widgets[index].GetString("fontFamily");

    private static void Preview(LayoutSession session, int index, string? font) =>
        session.Preview(SettingTarget.Widget(index), "fontFamily", font is null ? null : JsonValue.Create(font), null);

    [Fact]
    public void A_preview_redraws_without_an_undo_step_or_unsaved_changes()
    {
        var session = Session();
        var changes = new List<LayoutChange>();
        session.Changed += (_, change) => changes.Add(change);

        Preview(session, 0, "Arial");
        Preview(session, 0, "Georgia");

        Assert.Equal("Georgia", Font(session, 0));
        Assert.Equal([ChangeKind.Content, ChangeKind.Content], changes.Select(c => c.Kind));
        Assert.All(changes, c => Assert.Equal(0, c.WidgetIndex));
        Assert.True(session.IsPreviewing);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void A_preview_not_kept_leaves_the_layout_exactly_as_it_was()
    {
        var session = Session();
        // Something to redo, which a preview mustn't lose.
        session.SetValue(SettingTarget.Widget(1), "text", JsonValue.Create("Z"), null);
        session.Undo();
        var before = Json(session);

        // A value the widget has (its place among the others must stay) and one it doesn't.
        session.Preview(SettingTarget.Widget(0), "text", JsonValue.Create("Previewed"), null);
        session.Preview(SettingTarget.Widget(0), "text", JsonValue.Create("Again"), null);
        session.EndPreview(keep: false, null);
        Preview(session, 1, "Arial");
        session.EndPreview(keep: false, null);

        Assert.Equal(before, Json(session));
        Assert.False(session.IsPreviewing);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.True(session.CanRedo);
    }

    /// <summary>A widget with its own font among its other settings, not last.</summary>
    private static LayoutSession SessionWithFont() => new(new LayoutDocument
    {
        Theme = new JsonObject { ["fontFamily"] = "Verdana", ["foreground"] = "#FFFFFF" },
        Widgets =
        [
            new JsonObject { ["type"] = "text", ["fontFamily"] = "Arial", ["x"] = 0, ["y"] = 0, ["width"] = 100, ["height"] = 50, ["text"] = "A" },
        ],
    });

    [Theory]
    [InlineData("Georgia")]
    [InlineData("Arial")]
    public void A_preview_through_no_font_and_not_kept_leaves_the_setting_in_its_place(string last)
    {
        // Showing no font takes the setting out, and showing one again would add it at the end.
        var session = SessionWithFont();
        var before = Json(session);
        Preview(session, 0, null);
        Preview(session, 0, last);
        session.EndPreview(keep: false, null);

        Assert.Equal(before, Json(session));
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void A_preview_of_the_themes_font_not_kept_leaves_it_in_its_place()
    {
        var session = SessionWithFont();
        var before = Json(session);
        session.Preview(SettingTarget.Theme, "fontFamily", null, null);
        session.Preview(SettingTarget.Theme, "fontFamily", JsonValue.Create("Georgia"), null);
        session.EndPreview(keep: false, null);

        Assert.Equal(before, Json(session));
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Undoing_a_preview_kept_after_no_font_is_back_to_the_saved_layout()
    {
        var session = SessionWithFont();
        var before = Json(session);
        Preview(session, 0, null);
        Preview(session, 0, "Georgia");
        session.EndPreview(keep: true, null);
        Assert.Equal("Georgia", Font(session, 0));

        session.Undo();
        Assert.Equal(before, Json(session));
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void A_preview_kept_is_one_undo_step()
    {
        var session = Session();
        Preview(session, 0, "Arial");
        Preview(session, 0, "Bahnschrift");
        Preview(session, 0, "Georgia");
        session.EndPreview(keep: true, null);

        Assert.Equal("Georgia", Font(session, 0));
        Assert.False(session.IsPreviewing);
        Assert.True(session.IsDirty);

        session.Undo();
        Assert.Null(Font(session, 0));
        Assert.False(session.CanUndo);
        Assert.False(session.IsDirty);
        session.Redo();
        Assert.Equal("Georgia", Font(session, 0));
    }

    [Fact]
    public void Keeping_a_preview_back_on_the_value_it_had_is_not_an_edit()
    {
        var session = Session();
        var before = Json(session);
        Preview(session, 0, "Arial");
        Preview(session, 0, null);
        session.EndPreview(keep: true, null);

        Assert.Equal(before, Json(session));
        Assert.False(session.CanUndo);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void An_edit_during_a_preview_keeps_the_preview_as_a_step_of_its_own()
    {
        var session = Session();
        Preview(session, 0, "Arial");
        session.SetValue(SettingTarget.Widget(1), "text", JsonValue.Create("Y"), null);

        Assert.False(session.IsPreviewing);
        session.Undo();
        Assert.Equal(("Arial", "B"), (Font(session, 0), Text(session, 1)));
        session.Undo();
        Assert.Null(Font(session, 0));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Previewing_another_setting_keeps_the_first()
    {
        var session = Session();
        Preview(session, 0, "Arial");
        Preview(session, 1, "Georgia");
        session.EndPreview(keep: false, null);

        Assert.Equal("Arial", Font(session, 0));
        Assert.Null(Font(session, 1));
        Assert.True(session.CanUndo);
    }

    [Fact]
    public void Undo_during_a_preview_drops_it_and_undoes_the_last_edit()
    {
        var session = Session();
        session.SetValue(SettingTarget.Widget(0), "text", JsonValue.Create("X"), null);
        Preview(session, 0, "Arial");

        session.Undo();
        Assert.False(session.IsPreviewing);
        Assert.Equal("A", Text(session, 0));
        Assert.Null(Font(session, 0));
        session.Redo();
        Assert.Equal("X", Text(session, 0));
        Assert.Null(Font(session, 0));
    }

    [Fact]
    public void Saving_during_a_preview_keeps_it()
    {
        var session = Session();
        Preview(session, 0, "Arial");
        session.MarkSaved();

        Assert.False(session.IsPreviewing);
        Assert.False(session.IsDirty);
        Assert.Equal("Arial", Font(session, 0));
        // Undo still goes back to before it, which is then a change from what was saved.
        session.Undo();
        Assert.Null(Font(session, 0));
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void Switching_layouts_drops_a_preview()
    {
        var session = Session();
        Preview(session, 0, "Arial");
        session.Reset(Session().Document);

        Assert.False(session.IsPreviewing);
        session.EndPreview(keep: true, null);
        Assert.Null(Font(session, 0));
        Assert.False(session.CanUndo);
    }

    /// <summary>A clock that only moves when told, for how far apart edits are.</summary>
    private sealed class FakeClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}
