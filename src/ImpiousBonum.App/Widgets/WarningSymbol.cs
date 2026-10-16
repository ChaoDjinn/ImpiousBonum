using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ImpiousBonum.App.Widgets;

/// <summary>Which symbol a value's warning colour gets (see <see cref="ThresholdColors"/>).</summary>
public enum ThresholdLevel
{
    /// <summary>No rule matches, or the one that does draws in a colour other than the theme's warning or critical.</summary>
    None,

    /// <summary>The theme's warning colour: a triangle.</summary>
    Warning,

    /// <summary>The theme's critical colour: a diamond.</summary>
    Critical,
}

/// <summary>
/// The symbol beside a value in a warning colour, with the theme's <c>warningSymbols</c> on: a triangle for warning and a
/// diamond for critical. Their outlines differ, so they can be told apart (and from a normal value) without seeing
/// colour. They're drawn as shapes rather than characters, so every theme font shows them the same, level with its
/// capitals. It takes no room while there's nothing to mark.
/// </summary>
public sealed class WarningSymbol : FrameworkElement
{
    /// <summary>The space between a symbol and its value, in ems of the value's font.</summary>
    internal const double Gap = 0.25;

    private readonly double _lineHeight;
    private readonly double _middle;
    private readonly double _size;

    /// <summary>A symbol for a value, hidden until <see cref="Show"/> gives it something to mark.</summary>
    /// <param name="typeface">The value's font, to be sized and lined up with its capitals.</param>
    /// <param name="fontSize">The value's font size.</param>
    public WarningSymbol(Typeface typeface, double fontSize)
    {
        (_size, _middle, _lineHeight) = Measure(typeface, fontSize);
        VerticalAlignment = VerticalAlignment.Center;
        Visibility = Visibility.Collapsed;
    }

    /// <summary>Which symbol it shows, or none.</summary>
    public ThresholdLevel Level { get; private set; }

    /// <summary>The colour it's drawn in: the value's warning colour.</summary>
    public Brush Brush { get; private set; } = Brushes.Transparent;

    /// <summary>Shows the symbol for <paramref name="level"/> in <paramref name="brush"/>, or hides it for <see cref="ThresholdLevel.None"/>.</summary>
    public void Show(ThresholdLevel level, Brush brush)
    {
        if (level == Level && ReferenceEquals(brush, Brush))
            return;
        Level = level;
        Brush = brush;
        Visibility = level == ThresholdLevel.None ? Visibility.Collapsed : Visibility.Visible;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(WidthFor(_size), _lineHeight);

    protected override void OnRender(DrawingContext dc) =>
        Draw(dc, Level, Brush, new Point(ActualWidth / 2, (ActualHeight - _lineHeight) / 2 + _middle), _size);

    /// <summary>
    /// How tall a symbol is for text in this font, where its middle is from the top of a line of that text (the middle
    /// of a capital), and how tall the line is.
    /// </summary>
    internal static (double Size, double Middle, double LineHeight) Measure(Typeface typeface, double fontSize)
    {
        var text = new FormattedText("H", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black, 1);
        var capital = text.BuildGeometry(default).Bounds;
        if (capital.IsEmpty || capital.Height <= 0)
            capital = new Rect(0, text.Baseline - fontSize * 0.7, 0, fontSize * 0.7);
        // A little shorter than the capitals: a solid shape looks heavier than letters drawn in lines.
        return (capital.Height * 0.85, (capital.Top + capital.Bottom) / 2, text.Height);
    }

    /// <summary>How wide a symbol <paramref name="size"/> tall is: the same for both, so nothing moves when one becomes the other.</summary>
    internal static double WidthFor(double size) => size * 1.16;

    /// <summary>Draws the symbol for <paramref name="level"/>, <paramref name="size"/> tall and centred on <paramref name="centre"/>.</summary>
    internal static void Draw(DrawingContext dc, ThresholdLevel level, Brush brush, Point centre, double size)
    {
        var (x, y, half) = (centre.X, centre.Y, size / 2);
        Point[] points = level switch
        {
            ThresholdLevel.Warning => [new(x, y - half), new(x + size * 0.58, y + half), new(x - size * 0.58, y + half)],
            // A little taller than the triangle, which looks bigger for the same height.
            ThresholdLevel.Critical => [new(x, y - half * 1.12), new(x + size * 0.46, y), new(x, y + half * 1.12), new(x - size * 0.46, y)],
            _ => [],
        };
        if (points.Length == 0)
            return;
        var shape = new StreamGeometry();
        using (var context = shape.Open())
        {
            context.BeginFigure(points[0], isFilled: true, isClosed: true);
            context.PolyLineTo(points[1..], isStroked: false, isSmoothJoin: false);
        }
        shape.Freeze();
        dc.DrawGeometry(brush, null, shape);
    }
}
