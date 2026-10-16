using System.Windows;
using ImpiousBonum.App.Editor;

namespace ImpiousBonum.App.Tests;

public sealed class ArrangeGeometryTests
{
    /// <summary>Three widgets of different sizes. Around them all: left 10, right 260, top 10, bottom 190.</summary>
    private static readonly Rect[] Three = [new(10, 10, 100, 50), new(200, 40, 60, 20), new(120, 100, 30, 90)];

    private static string Positions(IEnumerable<Rect> rects) => string.Join(" ", rects.Select(r => $"{r.X},{r.Y}"));

    [Theory]
    [InlineData(Arrangement.AlignLeft, "10,10 10,40 10,100")]
    [InlineData(Arrangement.AlignCentres, "85,10 105,40 120,100")]    // centres on 135, midway between 10 and 260
    [InlineData(Arrangement.AlignRight, "160,10 200,40 230,100")]
    [InlineData(Arrangement.AlignTop, "10,10 200,10 120,10")]
    [InlineData(Arrangement.AlignMiddles, "10,75 200,90 120,55")]     // middles on 100, midway between 10 and 190
    [InlineData(Arrangement.AlignBottom, "10,140 200,170 120,100")]
    public void Lining_up_goes_by_the_box_around_them_all_and_keeps_their_sizes(Arrangement arrangement, string expected)
    {
        var placed = ArrangeGeometry.Apply(arrangement, Three);
        Assert.Equal(expected, Positions(placed));
        Assert.Equal(Three.Select(r => r.Size), placed.Select(r => r.Size));
    }

    [Fact]
    public void Same_width_and_height_match_the_first_and_stay_put()
    {
        var wide = ArrangeGeometry.Apply(Arrangement.SameWidth, Three);
        Assert.Equal([100.0, 100, 100], wide.Select(r => r.Width));
        Assert.Equal(Three.Select(r => r.Height), wide.Select(r => r.Height));
        Assert.Equal(Positions(Three), Positions(wide));

        var tall = ArrangeGeometry.Apply(Arrangement.SameHeight, Three);
        Assert.Equal([50.0, 50, 50], tall.Select(r => r.Height));
        Assert.Equal(Three.Select(r => r.Width), tall.Select(r => r.Width));
    }

    [Fact]
    public void Distributing_widgets_of_different_sizes_gives_equal_gaps_and_keeps_the_ends()
    {
        // Given out of order: 60 wide at 300, 100 wide at 0, 20 wide at 130. They cover 0 to 360 with 180 of widget,
        // so 90 between each.
        Rect[] across = [new(300, 0, 60, 10), new(0, 5, 100, 10), new(130, 9, 20, 10)];
        var placed = ArrangeGeometry.Apply(Arrangement.DistributeHorizontally, across);
        Assert.Equal("300,0 0,5 190,9", Positions(placed));

        // The same down the canvas.
        Rect[] down = [new(0, 300, 10, 60), new(5, 0, 10, 100), new(9, 130, 10, 20)];
        Assert.Equal("0,300 5,0 9,190", Positions(ArrangeGeometry.Apply(Arrangement.DistributeVertically, down)));
    }

    [Fact]
    public void Distributing_rounds_each_place_to_a_whole_unit()
    {
        // 61 to share out over three gaps: 20⅓ each, so the middle two land at 30⅓ and 60⅔.
        Rect[] rects = [new(0, 0, 10, 10), new(5, 0, 10, 10), new(50, 0, 10, 10), new(91, 0, 10, 10)];
        Assert.Equal("0,0 30,0 61,0 91,0", Positions(ArrangeGeometry.Apply(Arrangement.DistributeHorizontally, rects)));
    }

    [Fact]
    public void Level_centres_keep_the_order_they_are_given_in()
    {
        // The middle two share a centre at 50, so which goes first is down to the order they come in.
        Rect a = new(0, 0, 10, 10), b = new(40, 0, 20, 10), c = new(45, 0, 10, 10), d = new(90, 0, 10, 10);
        Assert.Equal("0,0 27,0 63,0 90,0", Positions(ArrangeGeometry.Apply(Arrangement.DistributeHorizontally, [a, b, c, d])));
        Assert.Equal("0,0 27,0 53,0 90,0", Positions(ArrangeGeometry.Apply(Arrangement.DistributeHorizontally, [a, c, b, d])));
    }

    [Theory]
    [InlineData(Arrangement.AlignLeft)]
    [InlineData(Arrangement.AlignCentres)]
    [InlineData(Arrangement.AlignRight)]
    [InlineData(Arrangement.AlignTop)]
    [InlineData(Arrangement.AlignMiddles)]
    [InlineData(Arrangement.AlignBottom)]
    [InlineData(Arrangement.DistributeHorizontally)]
    [InlineData(Arrangement.DistributeVertically)]
    [InlineData(Arrangement.SameWidth)]
    [InlineData(Arrangement.SameHeight)]
    public void Too_few_widgets_are_left_as_they_were(Arrangement arrangement)
    {
        Rect one = new(13, 17, 31, 7);
        Assert.Equal([one], ArrangeGeometry.Apply(arrangement, [one]));
        Assert.Empty(ArrangeGeometry.Apply(arrangement, []));

        // Two can be lined up or sized, but spacing them out needs three.
        Rect[] two = [one, new(200, 100, 50, 50)];
        Assert.Equal(ArrangeGeometry.Needs(arrangement) > 2, ArrangeGeometry.Apply(arrangement, two).SequenceEqual(two));
    }
}
