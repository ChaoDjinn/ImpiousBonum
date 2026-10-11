using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// A panel, circle or line, to group widgets or separate them. Added behind the other widgets, so a panel sits under
/// whatever is placed on it.
/// </summary>
public sealed class ShapeWidget : Widget
{
    public static WidgetDescriptor Descriptor { get; } = new(
        "shape", "Shape",
        "A rounded panel, a circle or a line, to group widgets or divide them. It's added behind the other widgets.",
        400, 200,
        [
            Setting.Choice("shape", "Shape", "panel", ["panel", "circle", "line"], group: Setting.Content,
                help: "A line runs along the middle of the box, across its longer side."),
            Setting.Color("color", "Colour", "track", help: "The fill of a panel or circle, or the colour of a line."),
            Setting.Number("cornerRadius", "Corner radius", 16, 0, 1000, group: Setting.Appearance, help: "For a panel."),
            Setting.Color("borderColor", "Outline colour", "accent"),
            Setting.Number("borderThickness", "Outline", 0, 0, 100, group: Setting.Appearance, help: "0 has no outline."),
            Setting.Number("thickness", "Line thickness", 2, 0.5, 100, group: Setting.Appearance, help: "For a line."),
            Setting.Number("opacity", "Opacity", 1, 0, 1, group: Setting.Appearance),
        ],
        (settings, theme) => new ShapeWidget(settings, theme)) { AddBehind = true };

    public ShapeWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        var color = theme.Resolve(settings.String("color"), theme.Track);
        var border = theme.Resolve(settings.String("borderColor"), theme.Accent);
        var borderThickness = settings.Number("borderThickness");

        FrameworkElement shape = settings.String("shape") switch
        {
            "circle" => new Ellipse { Fill = color, Stroke = borderThickness > 0 ? border : null, StrokeThickness = borderThickness },
            "line" => new DividerLine(color, settings.Number("thickness")),
            _ => new System.Windows.Controls.Border
            {
                Background = color,
                BorderBrush = border,
                BorderThickness = new Thickness(borderThickness),
                CornerRadius = new CornerRadius(settings.Number("cornerRadius")),
            },
        };
        // On the shape, not the widget: the widget's own opacity fades it while editing.
        shape.Opacity = settings.Number("opacity");
        Children.Add(shape);
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
    }

    /// <summary>A line along the middle of its box, horizontal or vertical, whichever way the box is longer.</summary>
    private sealed class DividerLine(Brush brush, double thickness) : FrameworkElement
    {
        private readonly Pen _pen = CreatePen(brush, thickness);

        protected override void OnRender(DrawingContext dc)
        {
            var (width, height) = (ActualWidth, ActualHeight);
            if (width >= height)
                dc.DrawLine(_pen, new Point(0, height / 2), new Point(width, height / 2));
            else
                dc.DrawLine(_pen, new Point(width / 2, 0), new Point(width / 2, height));
        }

        private static Pen CreatePen(Brush brush, double thickness)
        {
            var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            return pen;
        }
    }
}
