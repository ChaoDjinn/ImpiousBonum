namespace ImpiousBonum.App.Shell;

/// <summary>Which edges of the dashboard window a resize drag moves.</summary>
[Flags]
public enum WindowEdges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
}

/// <summary>Placement maths for the windowed dashboard, in physical pixels. Pure so it can be tested.</summary>
public static class WindowGeometry
{
    /// <summary>Smallest width a resize can reach; the height follows from the aspect ratio.</summary>
    public const int MinimumWidth = 160;

    /// <summary>The edges within <paramref name="border"/> of a point inside a <paramref name="width"/>×<paramref name="height"/> window.</summary>
    public static WindowEdges EdgesAt(double x, double y, double width, double height, double border)
    {
        var edges = WindowEdges.None;
        if (x < border)
            edges |= WindowEdges.Left;
        else if (x >= width - border)
            edges |= WindowEdges.Right;
        if (y < border)
            edges |= WindowEdges.Top;
        else if (y >= height - border)
            edges |= WindowEdges.Bottom;
        return edges;
    }

    /// <summary>
    /// Resizes <paramref name="start"/> by a mouse movement of (<paramref name="dx"/>, <paramref name="dy"/>) on the given edges,
    /// keeping <paramref name="aspect"/> (width / height) and anchoring the opposite corner or edge.
    /// Dragging a side edge sets the width; the top or bottom edge sets the height; a corner follows whichever moved more.
    /// </summary>
    public static PixelRect Resize(PixelRect start, WindowEdges edges, int dx, int dy, double aspect)
    {
        var horizontal = edges & (WindowEdges.Left | WindowEdges.Right);
        var vertical = edges & (WindowEdges.Top | WindowEdges.Bottom);
        var widthChange = horizontal == WindowEdges.Left ? -dx : horizontal == WindowEdges.Right ? dx : 0;
        var heightChange = vertical == WindowEdges.Top ? -dy : vertical == WindowEdges.Bottom ? dy : 0;

        var byWidth = horizontal != 0 && (vertical == 0 || Math.Abs(widthChange) >= Math.Abs(heightChange * aspect));
        var width = byWidth ? start.Width + widthChange : (start.Height + heightChange) * aspect;
        width = Math.Max(MinimumWidth, width);
        var w = (int)Math.Round(width);
        var h = Math.Max(1, (int)Math.Round(width / aspect));

        // Anchor the opposite side. A side edge keeps the window centred vertically, and the top or bottom edge horizontally.
        var x = horizontal == WindowEdges.Left ? start.X + start.Width - w
            : horizontal == WindowEdges.Right ? start.X
            : start.X + (start.Width - w) / 2;
        var y = vertical == WindowEdges.Top ? start.Y + start.Height - h
            : vertical == WindowEdges.Bottom ? start.Y
            : start.Y + (start.Height - h) / 2;
        return new PixelRect(x, y, w, h);
    }

    /// <summary>A first windowed placement: the layout's shape, at most its design size and 60% of the monitor's width, centred.</summary>
    public static PixelRect DefaultArea(PixelRect monitor, double layoutWidth, double layoutHeight)
    {
        var aspect = layoutWidth / layoutHeight;
        var width = Math.Min(Math.Min(layoutWidth, monitor.Width * 0.6), monitor.Height * 0.6 * aspect);
        var w = Math.Max(MinimumWidth, (int)Math.Round(width));
        var h = Math.Max(1, (int)Math.Round(w / aspect));
        return new PixelRect((monitor.Width - w) / 2, (monitor.Height - h) / 2, w, h);
    }

    /// <summary>
    /// Keeps an area (relative to its monitor's top-left) on a <paramref name="monitorWidth"/>×<paramref name="monitorHeight"/>
    /// monitor: shrinks it to fit, then moves it back inside. Stops a resolution change or a drag half off the edge losing it.
    /// </summary>
    public static PixelRect Clamp(PixelRect area, int monitorWidth, int monitorHeight)
    {
        var w = Math.Clamp(area.Width, 1, monitorWidth);
        var h = Math.Clamp(area.Height, 1, monitorHeight);
        if (w < area.Width || h < area.Height)
        {
            // Shrink both ways by the same factor so the layout keeps its shape.
            var scale = Math.Min((double)monitorWidth / area.Width, (double)monitorHeight / area.Height);
            w = Math.Max(1, (int)(area.Width * scale));
            h = Math.Max(1, (int)(area.Height * scale));
        }
        return new PixelRect(Math.Clamp(area.X, 0, monitorWidth - w), Math.Clamp(area.Y, 0, monitorHeight - h), w, h);
    }

    /// <summary>Index of the monitor a window mostly sits on, or -1 when it overlaps none.</summary>
    public static int MostOverlapped(PixelRect window, IReadOnlyList<PixelRect> monitors)
    {
        var best = -1;
        long bestArea = 0;
        for (var i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            long w = Math.Min(window.X + window.Width, m.X + m.Width) - Math.Max(window.X, m.X);
            long h = Math.Min(window.Y + window.Height, m.Y + m.Height) - Math.Max(window.Y, m.Y);
            if (w > 0 && h > 0 && w * h > bestArea)
            {
                best = i;
                bestArea = w * h;
            }
        }
        return best;
    }
}
