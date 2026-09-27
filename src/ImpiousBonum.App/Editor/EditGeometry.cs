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
}
