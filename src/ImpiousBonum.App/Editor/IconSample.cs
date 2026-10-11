using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using ImpiousBonum.App.Widgets;

namespace ImpiousBonum.App.Editor;

/// <summary>One of the built-in icons, drawn in the surrounding text colour, for the icon picker. Draws nothing for "none".</summary>
public sealed class IconSample : FrameworkElement
{
    public static readonly DependencyProperty IconNameProperty = DependencyProperty.Register(
        nameof(IconName), typeof(string), typeof(IconSample), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    static IconSample()
    {
        // Follow the text colour, which changes on the highlighted item, like the name beside it.
        TextElement.ForegroundProperty.OverrideMetadata(typeof(IconSample),
            new FrameworkPropertyMetadata(SystemColors.ControlTextBrush, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    }

    public IconSample()
    {
        Width = 18;
        Height = 18;
        VerticalAlignment = VerticalAlignment.Center;
    }

    public string? IconName
    {
        get => (string?)GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Icons.Get(IconName) is not { } geometry)
            return;
        var pen = new Pen((Brush)GetValue(TextElement.ForegroundProperty), 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.PushTransform(new ScaleTransform(ActualWidth / 24, ActualHeight / 24));
        dc.DrawGeometry(null, pen, geometry);
        dc.Pop();
    }
}
