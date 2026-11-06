using System.Windows;
using System.Windows.Media;

namespace ImpiousBonum.App.Widgets;

public enum GraphStyle
{
    Line,
    Area,
}

/// <summary>Draws a history of values as a line or a gradient-filled area. Gaps (NaN) break the line.</summary>
public sealed class GraphElement : FrameworkElement
{
    private double[] _values = [];

    public GraphStyle Kind { get; init; } = GraphStyle.Line;

    /// <summary>Number of points across the full width; fewer values draw right-aligned, like a scrolling chart.</summary>
    public int Points { get; init; } = 120;

    public double Minimum { get; set; }

    /// <summary>Upper bound; <c>null</c> scales to the largest visible value.</summary>
    public double? Maximum { get; set; }

    /// <summary>Line colour. Changes show on the next <see cref="SetValues"/>.</summary>
    public Brush Stroke { get; set; } = Brushes.Orange;

    public Brush? Fill { get; set; }

    public double Thickness { get; init; } = 2;

    /// <summary>The band between <see cref="SetValues"/>' lows and highs. Changes show on the next <see cref="SetValues"/>.</summary>
    public Brush? BandFill { get; set; }

    private double[]? _low;
    private double[]? _high;

    /// <summary>What it's drawing, for tests.</summary>
    internal (double[] Values, double[]? Low, double[]? High) Shown => (_values, _low, _high);

    /// <param name="low">With <paramref name="high"/>, the lowest reading behind each value, for a band behind the line.</param>
    /// <param name="high">The highest reading behind each value.</param>
    public void SetValues(double[] values, double[]? low = null, double[]? high = null)
    {
        _values = values;
        (_low, _high) = low is not null && high is not null && low.Length == values.Length && high.Length == values.Length ? (low, high) : (null, null);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (_values.Length < 2 || width <= 0 || height <= 0)
            return;

        var max = Maximum ?? Math.Max((_high ?? _values).Where(v => !double.IsNaN(v)).DefaultIfEmpty(1).Max(), Minimum + 1e-9);
        var range = Math.Max(max - Minimum, 1e-9);
        var step = width / Math.Max(Points - 1, 1);
        var offset = width - (_values.Length - 1) * step;
        // Keep the stroke fully inside the element so peaks and floors aren't clipped.
        var inset = Thickness / 2;

        Point At(int i, double value) => new(
            offset + i * step,
            inset + (height - 2 * inset) * (1 - Math.Clamp((value - Minimum) / range, 0, 1)));
        Point ToPoint(int i) => At(i, _values[i]);

        if (_low is { } low && _high is { } high && BandFill is not null)
            DrawBand(dc, low, high, At);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var i = 0;
            while (i < _values.Length)
            {
                while (i < _values.Length && double.IsNaN(_values[i]))
                    i++;
                if (i >= _values.Length)
                    break;

                var start = i;
                var first = ToPoint(start);
                if (Kind == GraphStyle.Area)
                {
                    // Left open: fills close implicitly, and a closed figure would stroke the bottom edge.
                    ctx.BeginFigure(new Point(first.X, height), isFilled: true, isClosed: false);
                    ctx.LineTo(first, isStroked: false, isSmoothJoin: false);
                }
                else
                {
                    ctx.BeginFigure(first, isFilled: false, isClosed: false);
                }

                i++;
                while (i < _values.Length && !double.IsNaN(_values[i]))
                {
                    ctx.LineTo(ToPoint(i), isStroked: true, isSmoothJoin: true);
                    i++;
                }

                if (Kind == GraphStyle.Area)
                    ctx.LineTo(new Point(ToPoint(i - 1).X, height), isStroked: false, isSmoothJoin: false);
            }
        }
        geometry.Freeze();

        var pen = new Pen(Stroke, Thickness) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        dc.DrawGeometry(Kind == GraphStyle.Area ? Fill : null, pen, geometry);
    }

    /// <summary>
    /// The band from each point's lowest reading to its highest, a shape for each run without gaps: along the highs left
    /// to right, then back along the lows.
    /// </summary>
    private void DrawBand(DrawingContext dc, double[] low, double[] high, Func<int, double, Point> at)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var i = 0;
            while (i < high.Length)
            {
                while (i < high.Length && (double.IsNaN(low[i]) || double.IsNaN(high[i])))
                    i++;
                if (i >= high.Length)
                    break;
                var start = i;
                while (i < high.Length && !double.IsNaN(low[i]) && !double.IsNaN(high[i]))
                    i++;
                var end = i - 1;

                ctx.BeginFigure(at(start, high[start]), isFilled: true, isClosed: true);
                for (var j = start + 1; j <= end; j++)
                    ctx.LineTo(at(j, high[j]), isStroked: false, isSmoothJoin: false);
                for (var j = end; j >= start; j--)
                    ctx.LineTo(at(j, low[j]), isStroked: false, isSmoothJoin: false);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(BandFill, null, geometry);
    }
}
