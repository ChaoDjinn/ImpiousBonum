using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Tests;

public sealed class WindowGeometryTests
{
    private static readonly PixelRect Window = new(100, 100, 800, 200); // 4:1

    [Theory]
    [InlineData(5, 50, WindowEdges.Left)]
    [InlineData(795, 50, WindowEdges.Right)]
    [InlineData(400, 3, WindowEdges.Top)]
    [InlineData(400, 195, WindowEdges.Bottom)]
    [InlineData(2, 198, WindowEdges.Left | WindowEdges.Bottom)]
    [InlineData(400, 100, WindowEdges.None)]
    public void Edges_are_found_within_the_border(double x, double y, WindowEdges expected) =>
        Assert.Equal(expected, WindowGeometry.EdgesAt(x, y, 800, 200, 10));

    [Fact]
    public void Right_edge_sets_the_width_and_keeps_the_shape()
    {
        var r = WindowGeometry.Resize(Window, WindowEdges.Right, 200, 999, 4);
        Assert.Equal(new PixelRect(100, 75, 1000, 250), r);
    }

    [Fact]
    public void Left_edge_anchors_the_right_side()
    {
        var r = WindowGeometry.Resize(Window, WindowEdges.Left, 200, 0, 4);
        Assert.Equal(new PixelRect(300, 125, 600, 150), r);
        Assert.Equal(Window.X + Window.Width, r.X + r.Width);
    }

    [Fact]
    public void Bottom_edge_sets_the_height()
    {
        var r = WindowGeometry.Resize(Window, WindowEdges.Bottom, 0, 50, 4);
        Assert.Equal(new PixelRect(0, 100, 1000, 250), r);
    }

    [Fact]
    public void Top_left_corner_anchors_the_bottom_right()
    {
        var r = WindowGeometry.Resize(Window, WindowEdges.Left | WindowEdges.Top, -400, -10, 4);
        Assert.Equal(new PixelRect(-300, 0, 1200, 300), r);
    }

    [Fact]
    public void Resize_stops_at_the_minimum_width()
    {
        var r = WindowGeometry.Resize(Window, WindowEdges.Right, -5000, 0, 4);
        Assert.Equal(WindowGeometry.MinimumWidth, r.Width);
        Assert.Equal(WindowGeometry.MinimumWidth / 4, r.Height);
    }

    [Fact]
    public void Default_area_is_the_layout_shape_centred()
    {
        var r = WindowGeometry.DefaultArea(new PixelRect(0, 0, 2560, 1440), 1920, 480);
        Assert.Equal(new PixelRect(512, 528, 1536, 384), r);
    }

    [Fact]
    public void Default_area_is_never_bigger_than_the_design_size()
    {
        var r = WindowGeometry.DefaultArea(new PixelRect(0, 0, 3840, 2160), 800, 480);
        Assert.Equal((800, 480), (r.Width, r.Height));
    }

    [Fact]
    public void Clamp_moves_an_area_back_on_screen()
    {
        Assert.Equal(new PixelRect(1120, 0, 800, 200), WindowGeometry.Clamp(new PixelRect(1500, -40, 800, 200), 1920, 1080));
        Assert.Equal(new PixelRect(0, 0, 800, 200), WindowGeometry.Clamp(new PixelRect(-50, 0, 800, 200), 1920, 1080));
    }

    [Fact]
    public void Clamp_shrinks_an_area_bigger_than_the_monitor_keeping_its_shape()
    {
        var r = WindowGeometry.Clamp(new PixelRect(0, 0, 3840, 960), 1920, 1080);
        Assert.Equal(new PixelRect(0, 0, 1920, 480), r);
    }

    [Fact]
    public void Most_overlapped_monitor_wins()
    {
        var monitors = new[] { new PixelRect(0, 0, 1920, 1080), new PixelRect(1920, 0, 1920, 480) };
        Assert.Equal(1, WindowGeometry.MostOverlapped(new PixelRect(1800, 0, 800, 200), monitors));
        Assert.Equal(0, WindowGeometry.MostOverlapped(new PixelRect(1000, 500, 800, 200), monitors));
        Assert.Equal(-1, WindowGeometry.MostOverlapped(new PixelRect(5000, 5000, 800, 200), monitors));
    }
}
