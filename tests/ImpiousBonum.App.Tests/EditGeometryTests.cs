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
}
