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

    public double Minimum { get; init; }

    /// <summary>Upper bound; <c>null</c> scales to the largest visible value.</summary>
    public double? Maximum { get; set; }

    /// <summary>Line colour. Changes show on the next <see cref="SetValues"/>.</summary>
    public Brush Stroke { get; set; } = Brushes.Orange;

    public Brush? Fill { get; set; }

    public double Thickness { get; init; } = 2;

    public void SetValues(double[] values)
    {
        _values = values;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (_values.Length < 2 || width <= 0 || height <= 0)
            return;

        var max = Maximum ?? Math.Max(_values.Where(v => !double.IsNaN(v)).DefaultIfEmpty(1).Max(), Minimum + 1e-9);
        var range = Math.Max(max - Minimum, 1e-9);
        var step = width / Math.Max(Points - 1, 1);
        var offset = width - (_values.Length - 1) * step;
        // Keep the stroke fully inside the element so peaks and floors aren't clipped.
        var inset = Thickness / 2;

        Point ToPoint(int i) => new(
            offset + i * step,
            inset + (height - 2 * inset) * (1 - Math.Clamp((_values[i] - Minimum) / range, 0, 1)));

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
}
