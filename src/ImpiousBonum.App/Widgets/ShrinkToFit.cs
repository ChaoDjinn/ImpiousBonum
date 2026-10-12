using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ImpiousBonum.App.Widgets;

/// <summary>Which ways a <see cref="ShrinkToFit"/> makes its child fit.</summary>
public enum FitDirection
{
    /// <summary>Shrinks it when it's wider than its space: a line of text, or a label and its value.</summary>
    Width,

    /// <summary>
    /// Shrinks it when its text would be cut off at the top or bottom. It's laid out at the width it's given, so text
    /// that ends with … still does: for a stack of rows, each fitting its own width.
    /// </summary>
    Height,

    /// <summary>Either: a line of text, or a few stacked lines, with a widget to itself.</summary>
    Both,
}

/// <summary>
/// Shrinks its child evenly when it doesn't fit, so a long reading or a big font fits instead of being cut off. Never
/// enlarges it, so text that fits looks exactly as it would without this. The child keeps its own alignment in the
/// space, and stretches across it if it's set to.
/// </summary>
/// <remarks>
/// For the height only the ink counts, not the room a font leaves above and below its letters: big text's line is
/// often a little taller than its widget without anything visibly cut off, and that stays as it was.
/// </remarks>
public sealed class ShrinkToFit : Decorator
{
    private double _widthScale = 1;
    private ScaleTransform? _transform;

    public FitDirection Direction { get; init; } = FitDirection.Width;

    /// <summary>How much the child is shrunk, from 1 (not at all) down; for tests.</summary>
    public double Scale { get; private set; } = 1;

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is not { } child)
            return default;
        // At its natural size, so a line of text says how wide it really is. For the height alone, at the width it has,
        // so text that ends with … still does.
        var heightOnly = Direction == FitDirection.Height;
        child.Measure(new Size(heightOnly ? constraint.Width : double.PositiveInfinity, double.PositiveInfinity));
        var natural = child.DesiredSize;
        _widthScale = heightOnly ? 1 : ScaleFor(natural.Width, constraint.Width);
        return new Size(Math.Min(natural.Width * _widthScale, constraint.Width), Math.Min(natural.Height * _widthScale, constraint.Height));
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child is not { } child)
            return arrangeSize;
        var scale = _widthScale;
        Place(child, arrangeSize, scale);
        // Arranging drew it, so its ink is known; it's only worth a look when the child is taller than its space.
        if (Direction != FitDirection.Width && child.DesiredSize.Height * scale > arrangeSize.Height)
        {
            var ink = VisualTreeHelper.GetDescendantBounds(child);
            var fit = ink.IsEmpty ? 1 : ScaleForInk(Anchor(child), child.DesiredSize.Height, ink.Top, ink.Bottom, arrangeSize.Height);
            if (fit < scale)
            {
                scale = fit;
                Place(child, arrangeSize, scale);
            }
        }
        Scale = scale;
        return arrangeSize;
    }

    /// <summary>
    /// Lays the child out where its alignment puts it, as if the space were 1 / <paramref name="scale"/> times bigger,
    /// then draws it that much smaller from its top-left.
    /// </summary>
    private void Place(UIElement child, Size space, double scale)
    {
        var natural = child.DesiredSize;
        var horizontal = (child as FrameworkElement)?.HorizontalAlignment ?? HorizontalAlignment.Stretch;
        var anchor = Anchor(child);
        var width = horizontal == HorizontalAlignment.Stretch ? Math.Max(natural.Width, space.Width / scale) : natural.Width;
        var height = anchor < 0 ? Math.Max(natural.Height, space.Height / scale) : natural.Height;
        var x = horizontal switch
        {
            HorizontalAlignment.Center => (space.Width - width * scale) / 2,
            HorizontalAlignment.Right => space.Width - width * scale,
            _ => 0,
        };
        var y = Math.Max(anchor, 0) * (space.Height - height * scale);

        // One transform, changed only when the scale does.
        if (scale < 1)
        {
            _transform ??= new ScaleTransform();
            _transform.ScaleX = _transform.ScaleY = scale;
            child.RenderTransform = _transform;
        }
        else if (_transform is not null)
            child.RenderTransform = Transform.Identity;
        child.Arrange(new Rect(x, y, width, height));
    }

    /// <summary>Where the child sits in the height: 0 at the top, 0.5 in the middle, 1 at the bottom, or -1 stretched (filling it from the top).</summary>
    private static double Anchor(UIElement child) => (child as FrameworkElement)?.VerticalAlignment switch
    {
        VerticalAlignment.Top => 0,
        VerticalAlignment.Center => 0.5,
        VerticalAlignment.Bottom => 1,
        _ => -1,
    };

    internal static double ScaleFor(double natural, double available) =>
        natural > 0 && available > 0 && natural > available ? available / natural : 1;

    /// <summary>
    /// The largest scale, up to 1, that keeps ink from <paramref name="inkTop"/> to <paramref name="inkBottom"/> of a box
    /// <paramref name="height"/> tall inside <paramref name="available"/>, with the box at <paramref name="anchor"/>
    /// (0 the top, 0.5 the middle, 1 the bottom, below 0 stretched, which is the top).
    /// </summary>
    internal static double ScaleForInk(double anchor, double height, double inkTop, double inkBottom, double available)
    {
        if (available <= 0)
            return 1;
        anchor = Math.Max(anchor, 0);
        // The box's top lands at anchor × (available − height × scale), and its ink has to stay between 0 and available.
        var scale = 1.0;
        if (anchor > 0 && anchor * height - inkTop > 0)
            scale = Math.Min(scale, anchor * available / (anchor * height - inkTop));
        if (anchor < 1 && inkBottom - anchor * height > 0)
            scale = Math.Min(scale, (1 - anchor) * available / (inkBottom - anchor * height));
        return scale;
    }
}
