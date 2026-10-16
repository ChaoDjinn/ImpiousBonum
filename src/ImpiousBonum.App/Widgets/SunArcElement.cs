using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// The sun's day as an arc over the horizon, from sunrise on the left to sunset on the right, filled as far as the sun
/// has come, with the sun on it while it's up. Sunrise and sunset go under the ends, and the day's length (or a note)
/// inside the arc. Everything is laid out from the element's size, so the arc is as big as its box allows.
/// </summary>
public sealed class SunArcElement : FrameworkElement
{
    public double Thickness { get; init; } = 6;

    /// <summary>The sun, and the part of the arc it's been along.</summary>
    public Brush SunBrush { get; init; } = Brushes.Orange;

    /// <summary>The rest of the arc, and the horizon.</summary>
    public Brush Track { get; init; } = Brushes.DimGray;

    public Typeface Typeface { get; init; } = new("Segoe UI");

    /// <summary>The size of the text inside the arc; sunrise and sunset, and the line under the text, are a little smaller.</summary>
    public double TextSize { get; init; } = 20;

    public Brush TextBrush { get; init; } = Brushes.White;

    public Brush DetailBrush { get; init; } = Brushes.Gray;

    /// <summary>
    /// Draws all the text smaller together when the text inside the arc doesn't fit there, or sunrise and sunset don't fit
    /// side by side or would leave the arc little of the height.
    /// </summary>
    public bool ShrinkText { get; init; } = true;

    /// <summary>The size the text inside the arc was last drawn at: <see cref="TextSize"/>, or smaller to fit; for tests.</summary>
    internal double ShownTextSize { get; private set; }

    /// <summary>How far the sun is through its day, from 0 at sunrise to 1 at sunset, or null while it's down.</summary>
    public double? Sun { get; private set; }

    public string Rise { get; private set; } = string.Empty;

    public string Set { get; private set; } = string.Empty;

    /// <summary>The line inside the arc, e.g. the day's length.</summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>The smaller line under <see cref="Text"/>.</summary>
    public string Caption { get; private set; } = string.Empty;

    /// <summary>Updates what it shows, redrawing only when something changed.</summary>
    public void Update(double? sun, string rise, string set, string text, string caption)
    {
        // A thousandth of a day is under a minute, and less than a pixel along the arc at any size it's likely to be.
        sun = sun is double s && !double.IsNaN(s) ? Math.Round(Math.Clamp(s, 0, 1), 3) : null;
        if (sun == Sun && rise == Rise && set == Set && text == Text && caption == Caption)
            return;
        (Sun, Rise, Set, Text, Caption) = (sun, rise, set, text, caption);
        InvalidateVisual();
    }

    /// <summary>Wide and tall enough for the text and a small arc, but no bigger than the room it has.</summary>
    protected override Size MeasureOverride(Size availableSize) =>
        new(Math.Min(availableSize.Width, TextSize * 8), Math.Min(availableSize.Height, TextSize * 5));

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        var t = Math.Min(Thickness, Math.Min(width, height) / 8);
        if (t <= 0)
            return;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // All the text gets smaller together, which makes room for a bigger arc, so it can take a few goes to settle.
        var at = Lay(1, width, height, t, dpi);
        for (var i = 0; ShrinkText && at.Fit < 0.99 && i < 8; i++)
            at = Lay(Math.Max(at.Scale * at.Fit, 6 / TextSize), width, height, t, dpi);
        ShownTextSize = TextSize * at.Scale;
        if (at.Rx <= t || at.Ry <= t)
            return;

        var cx = width / 2;
        dc.DrawLine(Pen(Track, Math.Max(1, t / 3)), new Point(0, at.Horizon), new Point(width, at.Horizon));
        dc.DrawGeometry(null, Pen(Track, t), Arc(cx, at.Horizon, at.Rx, at.Ry, 1));
        if (Sun is double sun)
        {
            if (sun > 0)
                dc.DrawGeometry(null, Pen(SunBrush, t), Arc(cx, at.Horizon, at.Rx, at.Ry, sun));
            dc.DrawEllipse(SunBrush, null, PointOn(cx, at.Horizon, at.Rx, at.Ry, sun), SunRadius(t), SunRadius(t));
        }

        // Sunrise and sunset under the ends of the arc, kept inside the element.
        var under = at.Horizon + at.Gap / 2;
        if (at.Rise is { } rise)
            dc.DrawText(rise, new Point(Math.Clamp(cx - at.Rx - rise.Width / 2, 0, Math.Max(0, width - rise.Width)), under));
        if (at.Set is { } set)
            dc.DrawText(set, new Point(Math.Clamp(cx + at.Rx - set.Width / 2, 0, Math.Max(0, width - set.Width)), under));

        // The text inside the arc, standing a little above the horizon.
        var bottom = at.Horizon - t;
        if (at.Caption is { } caption)
        {
            bottom -= caption.Height;
            dc.DrawText(caption, new Point(cx - caption.Width / 2, bottom));
        }
        if (at.Text is { } text)
            dc.DrawText(text, new Point(cx - text.Width / 2, bottom - text.Height * 0.92));
    }

    /// <summary>
    /// Where everything goes with the text at <paramref name="scale"/> of its size: the arc stands on the horizon, as
    /// wide as it can be and no taller than a semicircle, with room for the sun at its ends and top, and sunrise and
    /// sunset underneath. <see cref="Placing.Fit"/> says how much smaller the text has to be to fit.
    /// </summary>
    private Placing Lay(double scale, double width, double height, double t, double pixelsPerDip)
    {
        var endSize = TextSize * 0.8 * scale;
        var rise = Label(Rise, endSize, DetailBrush, pixelsPerDip);
        var set = Label(Set, endSize, DetailBrush, pixelsPerDip);
        var gap = Math.Max(t, endSize * 0.3);
        // The same room underneath whether there are times or not, so the arc doesn't jump when they come and go.
        var below = Label("0", endSize, DetailBrush, pixelsPerDip)!.Height + gap / 2;
        var horizon = height - below;
        var sunRadius = SunRadius(t);
        var rx = width / 2 - sunRadius;
        var ry = Math.Min(rx, horizon - sunRadius);
        // A box taller than the arc needs has it in the middle, rather than standing on the bottom.
        horizon -= Math.Max(0, horizon - sunRadius - ry) / 2;
        var text = Label(Text, TextSize * scale, TextBrush, pixelsPerDip);
        var caption = Label(Caption, TextSize * 0.75 * scale, DetailBrush, pixelsPerDip);

        // Sunrise and sunset side by side, a fraction of the height under the arc, and the text inside it.
        var fit = Math.Min(ShrinkToFit.ScaleFor((rise?.Width ?? 0) + (set?.Width ?? 0) + gap, width), ShrinkToFit.ScaleFor(below, height * 0.3));
        var blockWidth = Math.Max(text?.Width ?? 0, caption?.Width ?? 0);
        var blockHeight = (text?.Height * 0.92 ?? 0) + (caption?.Height ?? 0);
        fit = Math.Min(fit, rx > t && ry > t ? InsideScale(blockWidth, blockHeight, rx - t, ry - t, t) : 0.5);
        return new Placing(scale, fit, rise, set, text, caption, horizon, gap, rx, ry);
    }

    /// <summary>The sun's size on an arc <paramref name="thickness"/> thick.</summary>
    private static double SunRadius(double thickness) => Math.Max(thickness * 1.6, 4);

    /// <summary>
    /// How much smaller a box <paramref name="width"/> by <paramref name="height"/>, centred and standing
    /// <paramref name="lift"/> above the horizon, has to be to keep its top corners inside a half ellipse of
    /// <paramref name="rx"/> by <paramref name="ry"/>: 1 when it fits already.
    /// </summary>
    internal static double InsideScale(double width, double height, double rx, double ry, double lift)
    {
        static bool Fits(double w, double h, double rx, double ry, double lift) =>
            Math.Pow(w / 2 / rx, 2) + Math.Pow((h + lift) / ry, 2) <= 1;

        if (width <= 0 || height <= 0 || rx <= 0 || ry <= 0 || Fits(width, height, rx, ry, lift))
            return 1;
        double low = 0, high = 1;
        for (var i = 0; i < 20; i++)
        {
            var middle = (low + high) / 2;
            if (Fits(width * middle, height * middle, rx, ry, lift))
                low = middle;
            else
                high = middle;
        }
        return low;
    }

    /// <summary>The arc from sunrise as far as <paramref name="fraction"/> of the way to sunset.</summary>
    private static StreamGeometry Arc(double cx, double horizon, double rx, double ry, double fraction)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(PointOn(cx, horizon, rx, ry, 0), isFilled: false, isClosed: false);
            context.ArcTo(PointOn(cx, horizon, rx, ry, fraction), new Size(rx, ry), 0, false, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>Where the sun is on the arc <paramref name="fraction"/> of the way from sunrise to sunset.</summary>
    internal static Point PointOn(double cx, double horizon, double rx, double ry, double fraction)
    {
        var angle = Math.PI * (1 + fraction);
        return new Point(cx + rx * Math.Cos(angle), horizon + ry * Math.Sin(angle));
    }

    private FormattedText? Label(string text, double size, Brush brush, double pixelsPerDip) =>
        text.Length == 0 ? null : new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface, size, brush, pixelsPerDip);

    private static Pen Pen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    /// <summary>Where <see cref="Lay"/> puts everything, with the text at <paramref name="Scale"/> of its size.</summary>
    /// <param name="Fit">How much smaller the text has to be to fit: 1 when it does.</param>
    /// <param name="Gap">Between the horizon and sunrise and sunset.</param>
    private sealed record Placing(
        double Scale, double Fit, FormattedText? Rise, FormattedText? Set, FormattedText? Text, FormattedText? Caption, double Horizon, double Gap, double Rx, double Ry);
}
