using System.Windows;
using System.Windows.Media;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// The moon as it looks: a disc in <see cref="Dark"/> with the part that's lit in <see cref="Lit"/> over it, lit on the
/// right or the left and as full as <see cref="Illumination"/> says.
/// </summary>
public sealed class MoonElement : FrameworkElement
{
    /// <summary>How wide the moon is.</summary>
    public double Diameter { get; init; } = 90;

    public Brush Lit { get; init; } = Brushes.Silver;

    public Brush Dark { get; init; } = Brushes.DimGray;

    /// <summary>How much of it is lit, from 0 (a new moon) to 1 (a full moon).</summary>
    public double Illumination { get; private set; }

    /// <summary>Which side the light is on: the right while it waxes as seen from the northern hemisphere.</summary>
    public bool LitOnRight { get; private set; } = true;

    /// <summary>Updates what it shows, redrawing only when something changed.</summary>
    public void Update(double illumination, bool litOnRight)
    {
        // To a tenth of a percent: finer than shows at any size it's likely to be.
        illumination = double.IsNaN(illumination) ? 0 : Math.Round(Math.Clamp(illumination, 0, 1), 3);
        if (illumination == Illumination && litOnRight == LitOnRight)
            return;
        Illumination = illumination;
        LitOnRight = litOnRight;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(Diameter, Diameter);

    protected override void OnRender(DrawingContext dc)
    {
        var radius = Math.Min(ActualWidth, ActualHeight) / 2;
        if (radius <= 0)
            return;
        var centre = new Point(ActualWidth / 2, ActualHeight / 2);
        dc.DrawEllipse(Dark, null, centre, radius, radius);
        if (LitShape(Illumination, LitOnRight, centre, radius) is { } lit)
            dc.DrawGeometry(Lit, null, lit);
    }

    /// <summary>
    /// The lit part of a moon of <paramref name="radius"/> around <paramref name="centre"/>: half the disc on the lit
    /// side, less half an ellipse for a crescent or plus one for a gibbous moon. The line between light and dark is
    /// that half ellipse, as wide as the lit part is short of half (or past it). Null for a new moon.
    /// </summary>
    internal static Geometry? LitShape(double illumination, bool litOnRight, Point centre, double radius)
    {
        if (illumination < 0.005)
            return null;
        if (illumination > 0.995)
            return Frozen(new EllipseGeometry(centre, radius, radius));

        var top = new Point(centre.X, centre.Y - radius);
        var bottom = new Point(centre.X, centre.Y + radius);
        var width = radius * Math.Abs(1 - 2 * illumination);
        // Down the lit side, then back up along the line between light and dark: a crescent's bows out towards the lit
        // side, a gibbous moon's towards the other.
        var outside = litOnRight ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;
        var inside = (illumination < 0.5) == litOnRight ? SweepDirection.Counterclockwise : SweepDirection.Clockwise;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(top, isFilled: true, isClosed: true);
            context.ArcTo(bottom, new Size(radius, radius), 0, false, outside, isStroked: false, isSmoothJoin: false);
            if (width < 0.01)
                context.LineTo(top, isStroked: false, isSmoothJoin: false);
            else
                context.ArcTo(top, new Size(width, radius), 0, false, inside, isStroked: false, isSmoothJoin: false);
        }
        return Frozen(geometry);
    }

    private static Geometry Frozen(Geometry geometry)
    {
        geometry.Freeze();
        return geometry;
    }
}
