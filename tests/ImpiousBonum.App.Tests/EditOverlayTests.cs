using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Input;
using ImpiousBonum.App.Editor;
using ImpiousBonum.App.Layout;

namespace ImpiousBonum.App.Tests;

/// <summary>
/// Selecting, moving and resizing on the editing surface, driven the way the mouse drives it but with points of the
/// test's choosing. At a zoom of one canvas unit per screen pixel, as on a 1000×600 canvas shown at full size.
/// </summary>
public sealed class EditOverlayTests
{
    /// <summary>
    /// A at 100,100 and B at 400,100 side by side, C under A at 100,300; each 100×50, with a 10 px grid.
    /// </summary>
    private static LayoutSession ThreeWidgets(double bLeft = 400) => new(new LayoutDocument
    {
        Width = 1000,
        Height = 600,
        Widgets = [Text("A", 100, 100), Text("B", bLeft, 100), Text("C", 100, 300)],
    });

    private static JsonObject Text(string text, double x, double y) =>
        new() { ["type"] = "text", ["x"] = x, ["y"] = y, ["width"] = 100, ["height"] = 50, ["text"] = text };

    private static EditOverlay Overlay(LayoutSession session) => new(session, 9, () => 1);

    private static Rect Geometry(LayoutSession session, int index) => LayoutSession.GeometryOf(session.Document.Widgets[index]);

    /// <summary>A press and let go without moving.</summary>
    private static void Click(EditOverlay overlay, double x, double y, ModifierKeys modifiers = ModifierKeys.None)
    {
        overlay.Press(new Point(x, y), modifiers);
        overlay.Release();
    }

    [Fact]
    public void Ctrl_or_Shift_and_a_click_add_a_widget_to_the_selection_or_take_it_out()
    {
        Sta.Run(() =>
        {
            var session = ThreeWidgets();
            var overlay = Overlay(session);

            Click(overlay, 150, 125);
            Assert.Equal([0], session.Selection);
            Click(overlay, 450, 125, ModifierKeys.Control);
            Assert.Equal([0, 1], session.Selection);
            Click(overlay, 150, 325, ModifierKeys.Shift);
            Assert.Equal([0, 1, 2], session.Selection);
            Click(overlay, 450, 125, ModifierKeys.Control);
            Assert.Equal([0, 2], session.Selection);

            // A plain click starts again with just the one.
            Click(overlay, 450, 125);
            Assert.Equal([1], session.Selection);
            Assert.False(session.CanUndo);
        });
    }

    [Fact]
    public void Dragging_one_of_several_moves_them_all_as_one_undo_step()
    {
        Sta.Run(() =>
        {
            var session = ThreeWidgets();
            var overlay = Overlay(session);
            session.Select([0, 2]);

            overlay.Press(new Point(150, 125), ModifierKeys.None);
            overlay.Drag(new Point(160, 125), ModifierKeys.None);
            overlay.Drag(new Point(173, 158), ModifierKeys.None);
            overlay.Release();

            // Moved 23, 33 and onto the grid together: they keep their places among themselves.
            Assert.Equal(new Rect(120, 130, 100, 50), Geometry(session, 0));
            Assert.Equal(new Rect(120, 330, 100, 50), Geometry(session, 2));
            Assert.Equal(new Rect(400, 100, 100, 50), Geometry(session, 1));
            Assert.Equal([0, 2], session.Selection);

            session.Undo();
            Assert.Equal(new Rect(100, 100, 100, 50), Geometry(session, 0));
            Assert.Equal(new Rect(100, 300, 100, 50), Geometry(session, 2));
            Assert.False(session.CanUndo);
        });
    }

    [Fact]
    public void Clicking_one_of_several_without_dragging_selects_just_that_one()
    {
        Sta.Run(() =>
        {
            var session = ThreeWidgets();
            var overlay = Overlay(session);
            session.Select([0, 2]);

            overlay.Press(new Point(150, 325), ModifierKeys.None);
            // Still all selected while pressed, in case it turns into a drag.
            var whilePressed = session.Selection.ToList();
            // Less than Windows' drag distance: still a click.
            overlay.Drag(new Point(152, 327), ModifierKeys.None);
            overlay.Release();

            Assert.Equal([0, 2], whilePressed);
            Assert.Equal([2], session.Selection);
            Assert.Equal(new Rect(100, 300, 100, 50), Geometry(session, 2));
            Assert.False(session.CanUndo);
        });
    }

    [Fact]
    public void A_box_on_empty_canvas_selects_the_widgets_wholly_inside_it()
    {
        Sta.Run(() =>
        {
            var session = ThreeWidgets();
            var overlay = Overlay(session);
            session.Select(1);

            overlay.Press(new Point(50, 50), ModifierKeys.None);
            var pressed = session.Selection.ToList();
            overlay.Drag(new Point(250, 400), ModifierKeys.None);
            var around = session.Selection.ToList();
            // Half way across A and C: neither is inside any more.
            overlay.Drag(new Point(150, 400), ModifierKeys.None);
            var across = session.Selection.ToList();
            overlay.Drag(new Point(250, 400), ModifierKeys.None);
            overlay.Release();

            Assert.Empty(pressed);
            Assert.Equal([0, 2], around);
            Assert.Empty(across);
            Assert.Equal([0, 2], session.Selection);
            Assert.False(session.CanUndo);

            // With Ctrl, it adds to what's selected.
            session.Select(1);
            overlay.Press(new Point(50, 50), ModifierKeys.Control);
            overlay.Drag(new Point(250, 200), ModifierKeys.Control);
            overlay.Release();
            Assert.Equal([1, 0], session.Selection);
        });
    }

    [Fact]
    public void Dragging_near_another_widget_snaps_to_it_with_a_guide_and_Alt_places_freely()
    {
        Sta.Run(() =>
        {
            var session = ThreeWidgets();
            var overlay = Overlay(session);

            // A's right edge to 397, three short of B's left edge.
            overlay.Press(new Point(150, 125), ModifierKeys.None);
            overlay.Drag(new Point(347, 125), ModifierKeys.None);
            var guides = overlay.Guides.ToList();
            overlay.Release();

            Assert.Equal(new Rect(300, 100, 100, 50), Geometry(session, 0));
            Assert.Contains(new SnapGuide(true, 400, 100, 150), guides);
            // Level with B too, so its top, middle and bottom have guides along to it.
            Assert.Contains(new SnapGuide(false, 100, 300, 500), guides);
            Assert.Empty(overlay.Guides);

            // Alt: exactly where the pointer puts it, neither on the grid nor lined up with anything.
            overlay.Press(new Point(350, 125), ModifierKeys.None);
            overlay.Drag(new Point(343, 125), ModifierKeys.Alt);
            var freeGuides = overlay.Guides.ToList();
            overlay.Release();

            Assert.Equal(new Rect(293, 100, 100, 50), Geometry(session, 0));
            Assert.Empty(freeGuides);
        });
    }

    [Fact]
    public void A_click_does_not_move_a_widget_sitting_near_a_line()
    {
        Sta.Run(() =>
        {
            // B's left edge is 3 from A's right edge, in snapping's reach.
            var session = ThreeWidgets(bLeft: 203);
            var overlay = Overlay(session);

            overlay.Press(new Point(150, 125), ModifierKeys.None);
            overlay.Drag(new Point(151, 126), ModifierKeys.None);
            overlay.Release();

            Assert.Equal([0], session.Selection);
            Assert.Equal(new Rect(100, 100, 100, 50), Geometry(session, 0));
            Assert.False(session.CanUndo);
        });
    }

    [Fact]
    public void Resizing_by_a_handle_snaps_the_edge_it_drags()
    {
        Sta.Run(() =>
        {
            var session = ThreeWidgets();
            var overlay = Overlay(session);
            session.Select(0);

            // A's right-hand handle, dragged to 397: three short of B's left edge.
            overlay.Press(new Point(200, 125), ModifierKeys.None);
            overlay.Drag(new Point(397, 125), ModifierKeys.None);
            var guides = overlay.Guides.ToList();
            overlay.Release();

            Assert.Equal(new Rect(100, 100, 300, 50), Geometry(session, 0));
            Assert.Equal([new SnapGuide(true, 400, 100, 150)], guides);
        });
    }

    [Fact]
    public void Several_selected_have_no_handles_so_a_press_on_an_edge_moves_them()
    {
        Sta.Run(() =>
        {
            var session = ThreeWidgets();
            var overlay = Overlay(session);
            session.Select([0, 1]);

            // Where A's right-hand handle would be.
            overlay.Press(new Point(200, 125), ModifierKeys.None);
            overlay.Drag(new Point(250, 125), ModifierKeys.None);
            overlay.Release();

            Assert.Equal(new Rect(150, 100, 100, 50), Geometry(session, 0));
            Assert.Equal(new Rect(450, 100, 100, 50), Geometry(session, 1));
        });
    }
}
