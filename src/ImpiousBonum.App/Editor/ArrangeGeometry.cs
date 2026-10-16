using System.Windows;

namespace ImpiousBonum.App.Editor;

/// <summary>Ways to line up, space out or size several widgets at once, from the editor's Align menu.</summary>
public enum Arrangement
{
    AlignLeft,
    AlignCentres,
    AlignRight,
    AlignTop,
    AlignMiddles,
    AlignBottom,
    DistributeHorizontally,
    DistributeVertically,
    SameWidth,
    SameHeight,
}

/// <summary>The maths behind the editor's Align menu, kept free of UI so it can be tested.</summary>
public static class ArrangeGeometry
{
    /// <summary>How many widgets an arrangement needs before it does anything: two to line up or size, three to space out.</summary>
    public static int Needs(Arrangement arrangement) =>
        arrangement is Arrangement.DistributeHorizontally or Arrangement.DistributeVertically ? 3 : 2;

    /// <summary>
    /// Where each rectangle goes, in the order given. Lining up is to the box around them all, and the same width or
    /// height is the first one's. Centres and gaps round to whole units, like everything else the editor places. Fewer
    /// rectangles than the arrangement <see cref="Needs"/> come back as they were.
    /// </summary>
    public static Rect[] Apply(Arrangement arrangement, IReadOnlyList<Rect> rects)
    {
        if (rects.Count < Needs(arrangement))
            return [.. rects];

        var bounds = EditGeometry.Bounds(rects);
        var middleX = bounds.Left + bounds.Width / 2;
        var middleY = bounds.Top + bounds.Height / 2;
        var first = rects[0];
        return arrangement switch
        {
            Arrangement.AlignLeft => [.. rects.Select(r => At(r, bounds.Left, r.Y))],
            Arrangement.AlignCentres => [.. rects.Select(r => At(r, Math.Round(middleX - r.Width / 2), r.Y))],
            Arrangement.AlignRight => [.. rects.Select(r => At(r, bounds.Right - r.Width, r.Y))],
            Arrangement.AlignTop => [.. rects.Select(r => At(r, r.X, bounds.Top))],
            Arrangement.AlignMiddles => [.. rects.Select(r => At(r, r.X, Math.Round(middleY - r.Height / 2)))],
            Arrangement.AlignBottom => [.. rects.Select(r => At(r, r.X, bounds.Bottom - r.Height))],
            Arrangement.DistributeHorizontally => Distribute(rects, bounds, across: true),
            Arrangement.DistributeVertically => Distribute(rects, bounds, across: false),
            Arrangement.SameWidth => [.. rects.Select(r => new Rect(r.X, r.Y, first.Width, r.Height))],
            Arrangement.SameHeight => [.. rects.Select(r => new Rect(r.X, r.Y, r.Width, first.Height))],
            _ => [.. rects],
        };
    }

    private static Rect At(Rect r, double x, double y) => new(x, y, r.Width, r.Height);

    /// <summary>
    /// Equal gaps between the rectangles, across (left to right) or down, within the space they cover now. They keep
    /// their order, by their centres, so only the gaps change; widgets of different sizes still get the same gaps.
    /// </summary>
    private static Rect[] Distribute(IReadOnlyList<Rect> rects, Rect bounds, bool across)
    {
        double Start(Rect r) => across ? r.Left : r.Top;
        double Length(Rect r) => across ? r.Width : r.Height;

        // OrderBy keeps the given order for centres that are level, so the result doesn't depend on chance.
        var order = Enumerable.Range(0, rects.Count).OrderBy(i => Start(rects[i]) + Length(rects[i]) / 2).ToList();
        var space = across ? bounds.Width : bounds.Height;
        var gap = (space - rects.Sum(Length)) / (rects.Count - 1);

        var result = rects.ToArray();
        var at = Start(bounds);
        foreach (var i in order)
        {
            var r = rects[i];
            result[i] = across ? At(r, Math.Round(at), r.Y) : At(r, r.X, Math.Round(at));
            at += Length(r) + gap;
        }
        return result;
    }
}
