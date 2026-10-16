using System.Windows;
using ImpiousBonum.App.Editor;

namespace ImpiousBonum.App.Tests;

public sealed class EditGeometryTests
{
    private static readonly Rect Box = new(100, 100, 200, 100);

    [Fact]
    public void Move_snaps_the_corner_to_the_grid()
    {
        Assert.Equal(new Rect(110, 90, 200, 100), EditGeometry.Move(Box, 13, -8, snap: 10));
        Assert.Equal(new Rect(113, 92, 200, 100), EditGeometry.Move(Box, 13, -8, snap: 1));
    }

    [Theory]
    // handle, dx, dy, expected left, top, right, bottom
    [InlineData(4, 50, 20, 100, 100, 350, 220)]  // bottom-right grows both ways
    [InlineData(0, -20, -30, 80, 70, 300, 200)]  // top-left moves the opposite corner's partners
    [InlineData(3, 40, 99, 100, 100, 340, 200)]  // right edge ignores vertical movement
    [InlineData(5, 99, 30, 100, 100, 300, 230)]  // bottom edge ignores horizontal movement
    public void Resize_moves_only_the_edges_the_handle_owns(int handle, double dx, double dy, double left, double top, double right, double bottom)
    {
        var r = EditGeometry.Resize(Box, handle, dx, dy, snap: 10);
        Assert.Equal((left, top, right, bottom), (r.Left, r.Top, r.Right, r.Bottom));
    }

    [Fact]
    public void Resize_never_lets_edges_cross()
    {
        var r = EditGeometry.Resize(Box, 3, -500, 0, snap: 1);
        Assert.Equal(EditGeometry.MinimumSize, r.Width);
        Assert.Equal(Box.Left, r.Left);
    }

    [Fact]
    public void Handle_hit_test_prefers_the_nearest_within_reach()
    {
        // Top-left handle sits at (100,100); top-middle at (200,100).
        Assert.Equal(0, EditGeometry.HandleAt(Box, new Point(104, 97), reach: 10));
        Assert.Equal(1, EditGeometry.HandleAt(Box, new Point(195, 104), reach: 10));
        Assert.Equal(-1, EditGeometry.HandleAt(Box, new Point(150, 150), reach: 10));

        // On a tiny widget with big touch handles, reaches overlap; the nearest handle wins.
        var tiny = new Rect(0, 0, 20, 20);
        Assert.Equal(4, EditGeometry.HandleAt(tiny, new Point(19, 19), reach: 30));
    }

    [Fact]
    public void Edge_handles_hide_on_widgets_too_small_for_them()
    {
        // 300×80 with 48-unit handles: too short for left/right middles, wide enough for top/bottom middles.
        var visible = EditGeometry.VisibleHandles(new Rect(0, 0, 300, 80), 48);
        Assert.Equal([true, true, true, false, true, true, true, false], visible);

        // A hidden handle can't be grabbed; the nearest visible one is used instead.
        Assert.NotEqual(3, EditGeometry.HandleAt(new Rect(0, 0, 300, 80), new Point(300, 40), reach: 40, handleSize: 48));
    }

    [Fact]
    public void Topmost_hit_prefers_later_widgets()
    {
        Rect[] rects = [new(0, 0, 100, 100), new(50, 50, 100, 100)];
        Assert.Equal(1, EditGeometry.TopmostAt(rects, new Point(75, 75)));
        Assert.Equal(0, EditGeometry.TopmostAt(rects, new Point(10, 10)));
        Assert.Equal(-1, EditGeometry.TopmostAt(rects, new Point(500, 500)));
    }

    // ---- Snapping to other widgets -----------------------------------------------------------------

    /// <summary>A canvas so big its own lines are nowhere near the box, for tests about other widgets.</summary>
    private static readonly Size Far = new(10000, 10000);

    /// <summary>A widget well below the box whose left edge is at 500, centre 550, right edge 600.</summary>
    private static readonly Rect At500 = new(500, 1000, 100, 100);

    [Fact]
    public void Moving_pulls_the_nearest_edge_in_reach_onto_another_widgets()
    {
        // The box's right edge comes to 497, three short of the other widget's left edge.
        var snapped = EditGeometry.SnapMove(Box, 197, 0, 10, [At500], Far, reach: 6);
        Assert.Equal(new Rect(300, 100, 200, 100), snapped.Rect);
        // Down from the box to the widget it lined up with.
        Assert.Equal([new SnapGuide(true, 500, 100, 1100)], snapped.Guides);

        // Another widget's edge at 498 is nearer, so it wins.
        snapped = EditGeometry.SnapMove(Box, 197, 0, 10, [At500, new(0, 2000, 498, 10)], Far, reach: 6);
        Assert.Equal(new Rect(298, 100, 200, 100), snapped.Rect);
        Assert.Equal([new SnapGuide(true, 498, 100, 2010)], snapped.Guides);
    }

    [Theory]
    // dx, expected x: the right edge lands at 300 + dx before snapping, and the other widget's left edge is at 500.
    [InlineData(194, 300)]  // 6 short: just in reach
    [InlineData(206, 300)]  // 6 past: just in reach
    [InlineData(193, 290)]  // 7 short: out of reach, so the grid
    [InlineData(207, 310)]  // 7 past: out of reach, so the grid
    public void Snapping_reaches_as_far_as_it_is_told_and_no_further(double dx, double x)
    {
        var snapped = EditGeometry.SnapMove(Box, dx, 0, 10, [At500], Far, reach: 6);
        Assert.Equal(x, snapped.Rect.X);
        Assert.Equal(x == 300, snapped.Guides.Count > 0);
    }

    [Fact]
    public void Ties_go_to_edges_before_the_centre_left_before_right_and_the_canvas_before_widgets()
    {
        // Left edge 2 from 96 and right edge 2 from 300: the left edge wins.
        var snapped = EditGeometry.SnapMove(Box, -2, 0, 10, [new(0, 2000, 96, 10), new(300, 3000, 50, 50)], Far, reach: 6);
        Assert.Equal(96, snapped.Rect.X);
        Assert.Equal([new SnapGuide(true, 96, 100, 2010)], snapped.Guides);

        // Right edge 3 from 306 and centre 3 from 200: the edge wins.
        snapped = EditGeometry.SnapMove(Box, 3, 0, 10, [new(190, 2000, 20, 10), new(306, 3000, 10, 10)], Far, reach: 6);
        Assert.Equal(106, snapped.Rect.X);

        // Right edge 2 from the canvas's middle (500) and 2 from a widget's edge (496): the canvas wins.
        snapped = EditGeometry.SnapMove(Box, 198, 0, 10, [new(380, 2000, 116, 10)], new Size(1000, 600), reach: 6);
        Assert.Equal(300, snapped.Rect.X);
        Assert.Equal([new SnapGuide(true, 500, 0, 600)], snapped.Guides);
    }

    [Fact]
    public void Centres_snap_to_widgets_centres_and_the_canvas_middle()
    {
        // The box's centre comes to 297, three short of a wide widget's centre at 300.
        var snapped = EditGeometry.SnapMove(Box, 97, 0, 10, [new(0, 1000, 600, 10)], Far, reach: 6);
        Assert.Equal(new Rect(200, 100, 200, 100), snapped.Rect);
        Assert.Equal([new SnapGuide(true, 300, 100, 1010)], snapped.Guides);

        // On a 1000×600 canvas: centre to 502 and middle to 298, both pulled to the canvas's middle lines, which the
        // guides cross the whole canvas along.
        snapped = EditGeometry.SnapMove(Box, 302, 148, 10, [], new Size(1000, 600), reach: 6);
        Assert.Equal(new Rect(400, 250, 200, 100), snapped.Rect);
        Assert.Equal([new SnapGuide(true, 500, 0, 600), new SnapGuide(false, 300, 0, 1000)], snapped.Guides);

        // And to the canvas's edge.
        Assert.Equal(0, EditGeometry.SnapMove(Box, -97, 0, 10, [], new Size(1000, 600), reach: 6).Rect.X);
    }

    [Fact]
    public void Top_and_bottom_edges_snap_too_and_win_over_the_grid()
    {
        // The bottom edge comes to 295, two short of a widget's top at 297; the grid alone would have made it 200.
        var snapped = EditGeometry.SnapMove(Box, 0, 95, 10, [new(1000, 297, 50, 50)], Far, reach: 6);
        Assert.Equal(new Rect(100, 197, 200, 100), snapped.Rect);
        Assert.Equal([new SnapGuide(false, 297, 100, 1050)], snapped.Guides);
    }

    [Fact]
    public void Resizing_snaps_and_shows_guides_only_for_the_edges_it_drags()
    {
        Rect[] others =
        [
            new(503, 1000, 50, 50),   // a left edge at 503, for the right edge to snap to
            new(100, 2000, 10, 10),   // a left edge exactly in line with the box's own, which isn't moving
            new(2000, 203, 10, 10),   // a top edge 3 below the box's bottom, which the right handle doesn't move
        ];

        var snapped = EditGeometry.SnapResize(Box, 3, 200, 2, 10, others, Far, reach: 6);
        Assert.Equal(new Rect(100, 100, 403, 100), snapped.Rect);
        Assert.Equal([new SnapGuide(true, 503, 100, 1050)], snapped.Guides);

        // The bottom-right corner drags both, so both snap.
        snapped = EditGeometry.SnapResize(Box, 4, 200, 2, 10, others, Far, reach: 6);
        Assert.Equal(new Rect(100, 100, 403, 103), snapped.Rect);
        Assert.Equal([new SnapGuide(true, 503, 100, 1050), new SnapGuide(false, 203, 100, 2010)], snapped.Guides);

        // With nothing in reach the edge goes to the grid, and edges still can't cross.
        Assert.Equal(new Rect(90, 100, 210, 100), EditGeometry.SnapResize(Box, 0, -13, 0, 10, others, Far, reach: 6).Rect);
        var squashed = EditGeometry.SnapResize(Box, 3, -500, 0, 1, [], Far, reach: 6).Rect;
        Assert.Equal((100.0, EditGeometry.MinimumSize), (squashed.Left, squashed.Width));
    }

    [Fact]
    public void Bounds_are_the_box_around_them_all()
    {
        Assert.Equal(new Rect(0, 0, 60, 30), EditGeometry.Bounds([new(0, 0, 10, 10), new(50, 20, 10, 10)]));
        Assert.True(EditGeometry.Bounds([]).IsEmpty);
    }
}
