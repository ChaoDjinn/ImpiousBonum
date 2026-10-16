using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ImpiousBonum.App.Widgets;

public enum GaugeShape
{
    /// <summary>A 270° dial open at the bottom, with the label in the gap.</summary>
    Arc,

    /// <summary>A semicircle, with the value inside and the label under it.</summary>
    Half,

    /// <summary>A full circle starting at the top.</summary>
    Ring,
}

/// <summary>
/// Draws a dial: a track, the part of it filled to <see cref="Fraction"/>, the value text in the middle and a label.
/// Everything is laid out from the element's size, so the dial is as large as its box allows.
/// </summary>
public sealed class GaugeElement : FrameworkElement
{
    public GaugeShape Shape { get; init; } = GaugeShape.Arc;

    public double Thickness { get; init; } = 14;

    public Brush Track { get; init; } = Brushes.DimGray;

    public Typeface Typeface { get; init; } = new("Segoe UI");

    public Brush LabelBrush { get; init; } = Brushes.White;

    public string Label { get; init; } = string.Empty;

    public double LabelSize { get; init; } = 22;

    public double ValueSize { get; init; } = 48;

    /// <summary>
    /// Draws the value smaller when it's too wide or too tall for the inside of the dial, and the label when it's wider
    /// than the gauge (or, in a ring, than the inside).
    /// </summary>
    public bool ShrinkText { get; init; } = true;

    /// <summary>The size the value was last drawn at: <see cref="ValueSize"/>, or smaller to fit; for tests.</summary>
    internal double ShownValueSize { get; private set; }

    /// <summary>The size the label was last drawn at: <see cref="LabelSize"/>, or smaller to fit; for tests.</summary>
    internal double ShownLabelSize { get; private set; }

    // A warning symbol's size and place for the value at full size, measured once when first needed.
    private (double Size, double Middle, double LineHeight)? _symbol;

    /// <summary>0–1, how much of the dial is filled.</summary>
    public double Fraction { get; private set; }

    public string Value { get; private set; } = string.Empty;

    public Brush Fill { get; private set; } = Brushes.Orange;

    public Brush ValueBrush { get; private set; } = Brushes.White;

    /// <summary>The <see cref="WarningSymbol"/> after the value, in the dial's colour, or none.</summary>
    public ThresholdLevel Symbol { get; private set; }

    /// <summary>Updates what the dial shows, redrawing only when something changed.</summary>
    public void Update(double fraction, string value, Brush fill, Brush valueBrush, ThresholdLevel symbol = ThresholdLevel.None)
    {
        fraction = double.IsNaN(fraction) ? 0 : Math.Clamp(fraction, 0, 1);
        if (fraction == Fraction && value == Value && ReferenceEquals(fill, Fill) && ReferenceEquals(valueBrush, ValueBrush) && symbol == Symbol)
            return;
        Fraction = fraction;
        Value = value;
        Fill = fill;
        ValueBrush = valueBrush;
        Symbol = symbol;
        InvalidateVisual();
    }

    /// <summary>Where the dial starts (degrees clockwise from 3 o'clock) and how far it goes round.</summary>
    internal static (double Start, double Sweep) Angles(GaugeShape shape) => shape switch
    {
        GaugeShape.Half => (180, 180),
        GaugeShape.Ring => (270, 360),
        _ => (135, 270),
    };

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        var t = Math.Min(Thickness, Math.Min(width, height) / 4);
        if (width <= t * 2 || height <= t * 2)
            return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var label = Label.Length > 0 ? Text(Label, LabelSize, LabelBrush, dpi) : null;
        ShownLabelSize = LabelSize;
        // An arc's or a half dial's label is under the dial, with the gauge's width to itself.
        if (label is not null && Shape != GaugeShape.Ring)
            label = FitLabel(label, width, dpi);
        var labelHeight = label?.Height ?? 0;

        // The dial's radius (to the middle of the stroke) and centre, fitted to the box.
        double radius, cx = width / 2, cy;
        switch (Shape)
        {
            case GaugeShape.Ring:
                radius = (Math.Min(width, height) - t) / 2;
                cy = height / 2;
                break;
            case GaugeShape.Half:
                // The label sits under the dial's flat edge.
                var below = label is null ? 0 : labelHeight + t * 0.3;
                radius = Math.Min((width - t) / 2, height - t / 2 - below);
                cy = (height - (radius + t / 2 + below)) / 2 + t / 2 + radius;
                break;
            default:
                // A 270° arc reaches cos(45°) of its radius below the centre.
                const double drop = 0.7071;
                radius = Math.Min((width - t) / 2, (height - t) / (1 + drop));
                cy = (height - radius * (1 + drop) - t) / 2 + t / 2 + radius;
                break;
        }
        if (radius <= t / 2)
            return;

        var (start, sweep) = Angles(Shape);
        var centre = new Point(cx, cy);
        var trackPen = CreatePen(Track, t, Shape != GaugeShape.Ring);
        DrawArc(dc, trackPen, centre, radius, start, sweep);
        if (Fraction > 0)
            DrawArc(dc, CreatePen(Fill, t, true), centre, radius, start, sweep * Fraction);

        // The value, shrunk if it doesn't fit inside the dial, with its warning symbol (if any) after it.
        var value = Text(Value, ValueSize, ValueBrush, dpi);
        ShownValueSize = ValueSize;
        var symbol = Symbol == ThresholdLevel.None ? default : (_symbol ??= WarningSymbol.Measure(Typeface, ValueSize));
        var symbolWidth = Symbol == ThresholdLevel.None ? 0 : WarningSymbol.WidthFor(symbol.Size) + ValueSize * WarningSymbol.Gap;
        var (roomWidth, roomHeight) = ValueRoom(Shape, radius - t / 2);
        if (ShrinkText)
        {
            var scale = Math.Min(ShrinkToFit.ScaleFor(value.Width + symbolWidth, roomWidth), ShrinkToFit.ScaleFor(value.Extent, roomHeight));
            if (scale < 1)
            {
                ShownValueSize = Math.Max(6, ValueSize * scale);
                value = Text(Value, ShownValueSize, ValueBrush, dpi);
            }
        }
        var shown = ShownValueSize / ValueSize;

        // The value and its symbol are centred together, the symbol level with the value's capitals.
        void DrawValue(double top)
        {
            var left = cx - (value.Width + symbolWidth * shown) / 2;
            dc.DrawText(value, new Point(left, top));
            if (Symbol == ThresholdLevel.None)
                return;
            var x = left + value.Width + ValueSize * WarningSymbol.Gap * shown + WarningSymbol.WidthFor(symbol.Size * shown) / 2;
            WarningSymbol.Draw(dc, Symbol, Fill, new Point(x, top + symbol.Middle * shown), symbol.Size * shown);
        }

        switch (Shape)
        {
            case GaugeShape.Ring:
            {
                // A ring's label is inside it, under the value.
                if (label is not null)
                    label = FitLabel(label, roomWidth, dpi);
                labelHeight = label?.Height ?? 0;
                var total = value.Height + labelHeight * 0.9;
                var top = cy - total / 2;
                DrawValue(top);
                if (label is not null)
                    dc.DrawText(label, new Point(cx - label.Width / 2, top + value.Height * 0.92));
                break;
            }
            case GaugeShape.Half:
                // Sit the value's baseline a little above the flat edge.
                DrawValue(cy - value.Baseline - t * 0.15);
                if (label is not null)
                    dc.DrawText(label, new Point(cx - label.Width / 2, cy + t * 0.3));
                break;
            default:
                DrawValue(cy - value.Height / 2);
                if (label is not null)
                {
                    // In the gap at the bottom, but never over the value.
                    var labelTop = Math.Max(cy + value.Height / 2 - value.Height * 0.08, cy + radius * 0.62 - labelHeight / 2);
                    dc.DrawText(label, new Point(cx - label.Width / 2, labelTop));
                }
                break;
        }
    }

    /// <summary>
    /// How wide and tall the value can be inside a dial whose inner radius is <paramref name="room"/>: a box that
    /// size, centred (or standing on a half dial's flat edge), stays clear of the track.
    /// </summary>
    internal static (double Width, double Height) ValueRoom(GaugeShape shape, double room) =>
        shape == GaugeShape.Half ? (2 * room * 0.8, room * 0.6) : (2 * room * 0.78, 2 * room * 0.62);

    /// <summary>The label again, smaller, when it's wider than <paramref name="room"/> and text shrinks to fit.</summary>
    private FormattedText FitLabel(FormattedText label, double room, double pixelsPerDip)
    {
        var scale = ShrinkToFit.ScaleFor(label.Width, room);
        if (!ShrinkText || scale >= 1)
            return label;
        ShownLabelSize = Math.Max(6, LabelSize * scale);
        return Text(Label, ShownLabelSize, LabelBrush, pixelsPerDip);
    }

    private FormattedText Text(string text, double size, Brush brush, double pixelsPerDip) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface, size, brush, pixelsPerDip);

    private static Pen CreatePen(Brush brush, double thickness, bool roundEnds)
    {
        var cap = roundEnds ? PenLineCap.Round : PenLineCap.Flat;
        var pen = new Pen(brush, thickness) { StartLineCap = cap, EndLineCap = cap };
        pen.Freeze();
        return pen;
    }

    private static void DrawArc(DrawingContext dc, Pen pen, Point centre, double radius, double start, double sweep)
    {
        if (sweep >= 359.99)
        {
            dc.DrawEllipse(null, pen, centre, radius, radius);
            return;
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(PointAt(centre, radius, start), isFilled: false, isClosed: false);
            ctx.ArcTo(PointAt(centre, radius, start + sweep), new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static Point PointAt(Point centre, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(centre.X + radius * Math.Cos(radians), centre.Y + radius * Math.Sin(radians));
    }
}
