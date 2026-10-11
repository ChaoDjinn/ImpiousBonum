using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// Shrinks its child evenly when it's wider than the space it has, so a long reading fits instead of being cut off.
/// Never enlarges it, and only the width counts: text that fits looks exactly as it would without this.
/// Give it the child's alignment, since it's only as big as the (shrunk) child.
/// </summary>
public sealed class ShrinkToWidth : Decorator
{
    /// <summary>How much the child is shrunk, from 1 (not at all) down; for tests.</summary>
    public double Scale { get; private set; } = 1;

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is not { } child)
            return default;
        child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var natural = child.DesiredSize;
        Scale = ScaleFor(natural.Width, constraint.Width);
        return new Size(natural.Width * Scale, natural.Height * Scale);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child is not { } child)
            return arrangeSize;
        // Laid out at its natural size, then drawn smaller from the top-left, which fills exactly the size measured.
        child.RenderTransform = Scale < 1 ? new ScaleTransform(Scale, Scale) : Transform.Identity;
        child.Arrange(new Rect(child.DesiredSize));
        return arrangeSize;
    }

    internal static double ScaleFor(double natural, double available) =>
        natural > 0 && available > 0 && natural > available ? available / natural : 1;
}
