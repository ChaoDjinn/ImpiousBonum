using System.Windows;

namespace ImpiousBonum.App.Editor;

/// <summary>
/// The maths behind selecting, moving and resizing widgets, kept free of UI so it can be tested.
/// Handles are numbered clockwise from the top-left: 0 top-left, 1 top, 2 top-right, 3 right,
/// 4 bottom-right, 5 bottom, 6 bottom-left, 7 left.
/// </summary>
public static class EditGeometry
{
    public const double MinimumSize = 8;

    public static Point[] HandlePoints(Rect r) =>
    [
        new(r.Left, r.Top), new(r.Left + r.Width / 2, r.Top), new(r.Right, r.Top), new(r.Right, r.Top + r.Height / 2),
        new(r.Right, r.Bottom), new(r.Left + r.Width / 2, r.Bottom), new(r.Left, r.Bottom), new(r.Left, r.Top + r.Height / 2),
    ];

    /// <summary>
    /// Which handles to show for a widget. Edge-middle handles are dropped when the widget is too small for them to sit
    /// clear of the corners (common with big touch handles on short widgets); the corners can do the same job.
    /// </summary>
    public static bool[] VisibleHandles(Rect rect, double handleSize)
    {
        var visible = Enumerable.Repeat(true, 8).ToArray();
        if (rect.Width < handleSize * 2.5)
            visible[1] = visible[5] = false;
        if (rect.Height < handleSize * 2.5)
            visible[3] = visible[7] = false;
        return visible;
    }

    /// <summary>The visible handle within <paramref name="reach"/> of the point, preferring the nearest, or -1.</summary>
    public static int HandleAt(Rect rect, Point point, double reach, double handleSize = 0)
    {
        var best = -1;
        var bestDistance = double.MaxValue;
        var points = HandlePoints(rect);
        var visible = VisibleHandles(rect, handleSize);
        for (var i = 0; i < points.Length; i++)
        {
            if (!visible[i])
                continue;
            var dx = Math.Abs(points[i].X - point.X);
            var dy = Math.Abs(points[i].Y - point.Y);
            if (dx > reach || dy > reach)
                continue;
            var distance = dx * dx + dy * dy;
            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>Index of the topmost rectangle containing the point. Later entries draw on top, so search from the end.</summary>
    public static int TopmostAt(IReadOnlyList<Rect> rects, Point point)
    {
        for (var i = rects.Count - 1; i >= 0; i--)
        {
            if (rects[i].Contains(point))
                return i;
        }
        return -1;
    }

    public static double SnapValue(double value, double snap) => Math.Round(value / Math.Max(1, snap)) * Math.Max(1, snap);

    /// <summary>Moves a rectangle by a drag delta, snapping its top-left corner to the grid.</summary>
    public static Rect Move(Rect start, double dx, double dy, double snap) =>
        new(SnapValue(start.X + dx, snap), SnapValue(start.Y + dy, snap), start.Width, start.Height);

    /// <summary>
    /// Drags one handle: the edges it owns follow the pointer (snapped); the opposite edges stay put.
    /// The result never gets smaller than <see cref="MinimumSize"/>, so edges can't cross.
    /// </summary>
    public static Rect Resize(Rect start, int handle, double dx, double dy, double snap)
    {
        double left = start.Left, top = start.Top, right = start.Right, bottom = start.Bottom;
        if (handle is 0 or 6 or 7)
            left = Math.Min(SnapValue(start.Left + dx, snap), right - MinimumSize);
        if (handle is 2 or 3 or 4)
            right = Math.Max(SnapValue(start.Right + dx, snap), left + MinimumSize);
        if (handle is 0 or 1 or 2)
            top = Math.Min(SnapValue(start.Top + dy, snap), bottom - MinimumSize);
        if (handle is 4 or 5 or 6)
            bottom = Math.Max(SnapValue(start.Bottom + dy, snap), top + MinimumSize);
        return new Rect(left, top, right - left, bottom - top);
    }

    // ---- Snapping to other widgets ------------------------------------------------------------------

    /// <summary>
    /// Positions are whole units, so a centre (which can fall half way) counts as lined up within half a unit.
    /// </summary>
    private const double GuideTolerance = 0.5;

    /// <summary>
    /// <see cref="Move"/>, but the rectangle's edges and centre also snap to the edges and centres of the
    /// <paramref name="others"/> and of the canvas, when one is within <paramref name="reach"/>. The nearest wins; on a
    /// tie, edges come before the centre, left (or top) before right (or bottom), and the canvas before widgets. On an
    /// axis with nothing in reach, the corner snaps to the grid as before.
    /// </summary>
    /// <param name="others">The widgets that aren't moving.</param>
    /// <param name="reach">How near counts, in canvas units: a few screen pixels at the preview's zoom.</param>
    public static Snapped SnapMove(Rect start, double dx, double dy, double grid, IReadOnlyList<Rect> others, Size canvas, double reach)
    {
        var (across, down) = Lines(others, canvas);
        var moved = new Rect(start.X + dx, start.Y + dy, start.Width, start.Height);
        var x = Pull([moved.Left, moved.Right, CentreX(moved)], across, reach) is { } pullX ? Math.Round(moved.X + pullX) : SnapValue(moved.X, grid);
        var y = Pull([moved.Top, moved.Bottom, CentreY(moved)], down, reach) is { } pullY ? Math.Round(moved.Y + pullY) : SnapValue(moved.Y, grid);
        var rect = new Rect(x, y, start.Width, start.Height);
        return new(rect, Guides(rect, [rect.Left, rect.Right, CentreX(rect)], [rect.Top, rect.Bottom, CentreY(rect)], across, down));
    }

    /// <summary>
    /// <see cref="Resize"/>, but the edges the handle drags also snap to the edges and centres of the
    /// <paramref name="others"/> and of the canvas, as in <see cref="SnapMove"/>. The edges it doesn't drag stay put.
    /// </summary>
    public static Snapped SnapResize(Rect start, int handle, double dx, double dy, double grid, IReadOnlyList<Rect> others, Size canvas, double reach)
    {
        var (across, down) = Lines(others, canvas);
        double left = start.Left, top = start.Top, right = start.Right, bottom = start.Bottom;
        if (handle is 0 or 6 or 7)
            left = Math.Min(Edge(start.Left + dx, across, grid, reach), right - MinimumSize);
        if (handle is 2 or 3 or 4)
            right = Math.Max(Edge(start.Right + dx, across, grid, reach), left + MinimumSize);
        if (handle is 0 or 1 or 2)
            top = Math.Min(Edge(start.Top + dy, down, grid, reach), bottom - MinimumSize);
        if (handle is 4 or 5 or 6)
            bottom = Math.Max(Edge(start.Bottom + dy, down, grid, reach), top + MinimumSize);
        var rect = new Rect(left, top, right - left, bottom - top);

        // Guides only for the edges being dragged: the others haven't moved, lined up with something or not.
        double[] xs = handle switch { 0 or 6 or 7 => [left], 2 or 3 or 4 => [right], _ => [] };
        double[] ys = handle switch { 0 or 1 or 2 => [top], 4 or 5 or 6 => [bottom], _ => [] };
        return new(rect, Guides(rect, xs, ys, across, down));
    }

    /// <summary>The smallest box around all the rectangles; empty when there are none.</summary>
    public static Rect Bounds(IEnumerable<Rect> rects) => rects.Aggregate(Rect.Empty, Rect.Union);

    private static double CentreX(Rect r) => r.Left + r.Width / 2;

    private static double CentreY(Rect r) => r.Top + r.Height / 2;

    /// <summary>
    /// A line something can snap to: at <see cref="At"/> across one axis, belonging to something that spans
    /// <see cref="From"/> to <see cref="To"/> along the other (so a guide can reach it).
    /// </summary>
    private readonly record struct Line(double At, double From, double To);

    /// <summary>The lines to snap to: vertical ones (x positions) and horizontal ones (y positions). The canvas's come first.</summary>
    private static (List<Line> Across, List<Line> Down) Lines(IReadOnlyList<Rect> others, Size canvas)
    {
        List<Line> across = [new(0, 0, canvas.Height), new(canvas.Width / 2, 0, canvas.Height), new(canvas.Width, 0, canvas.Height)];
        List<Line> down = [new(0, 0, canvas.Width), new(canvas.Height / 2, 0, canvas.Width), new(canvas.Height, 0, canvas.Width)];
        foreach (var r in others)
        {
            across.Add(new(r.Left, r.Top, r.Bottom));
            across.Add(new(CentreX(r), r.Top, r.Bottom));
            across.Add(new(r.Right, r.Top, r.Bottom));
            down.Add(new(r.Top, r.Left, r.Right));
            down.Add(new(CentreY(r), r.Left, r.Right));
            down.Add(new(r.Bottom, r.Left, r.Right));
        }
        return (across, down);
    }

    /// <summary>
    /// How far to move to put one of the <paramref name="moving"/> positions on the nearest line within reach, or null
    /// when none is. Ties go to the first found, so the order of both lists decides them.
    /// </summary>
    private static double? Pull(ReadOnlySpan<double> moving, List<Line> lines, double reach)
    {
        double? best = null;
        foreach (var at in moving)
        {
            foreach (var line in lines)
            {
                var pull = line.At - at;
                if (Math.Abs(pull) <= reach && (best is null || Math.Abs(pull) < Math.Abs(best.Value)))
                    best = pull;
            }
        }
        return best;
    }

    /// <summary>A dragged edge: onto the nearest line in reach, or else onto the grid.</summary>
    private static double Edge(double at, List<Line> lines, double grid, double reach) =>
        Pull([at], lines, reach) is { } pull ? Math.Round(at + pull) : SnapValue(at, grid);

    /// <summary>A guide for each of the positions that lines up with something, long enough to reach all it lines up with.</summary>
    private static List<SnapGuide> Guides(Rect rect, ReadOnlySpan<double> xs, ReadOnlySpan<double> ys, List<Line> across, List<Line> down)
    {
        var guides = new List<SnapGuide>();
        foreach (var x in xs)
            AddGuide(guides, vertical: true, x, rect.Top, rect.Bottom, across);
        foreach (var y in ys)
            AddGuide(guides, vertical: false, y, rect.Left, rect.Right, down);
        return guides;
    }

    private static void AddGuide(List<SnapGuide> guides, bool vertical, double at, double from, double to, List<Line> lines)
    {
        var found = false;
        foreach (var line in lines)
        {
            if (Math.Abs(line.At - at) > GuideTolerance)
                continue;
            found = true;
            from = Math.Min(from, line.From);
            to = Math.Max(to, line.To);
        }
        if (found)
            guides.Add(new SnapGuide(vertical, at, from, to));
    }
}

/// <summary>
/// A line showing what a dragged widget lined up with: down the canvas at x = <paramref name="At"/> when
/// <paramref name="Vertical"/>, else across it at y = <paramref name="At"/>, from <paramref name="From"/> to
/// <paramref name="To"/> along it.
/// </summary>
public readonly record struct SnapGuide(bool Vertical, double At, double From, double To);

/// <summary>Where a dragged widget goes, and the guides for what it lines up with there.</summary>
public sealed record Snapped(Rect Rect, IReadOnlyList<SnapGuide> Guides);
