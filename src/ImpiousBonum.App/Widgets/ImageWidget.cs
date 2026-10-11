using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// A picture from a file: a logo, artwork, a photo. Decoded at about the size it's shown, so a big file stays cheap.
/// With <c>recolor</c> on, a one-colour logo is drawn in a theme colour instead, so it follows the theme.
/// </summary>
public sealed class ImageWidget : Widget
{
    public static WidgetDescriptor Descriptor { get; } = new(
        "image", "Image",
        "A picture from a file: a logo, artwork or a photo. It's packed into exported layouts so it travels with them.",
        240, 240,
        [
            new SettingDescriptor(LayoutPackage.ImageFileKey, "Picture", SettingKind.ImageFile, null, Setting.Content, "A PNG, JPG, GIF, BMP or TIFF file."),
            Setting.Choice("fit", "Fit", "fit", BackgroundImage.Fits, group: Setting.Appearance,
                help: "fill covers the box (cropping), fit shows it all, stretch distorts to fit, center and tile keep its size."),
            Setting.Number("opacity", "Opacity", 1, 0, 1, group: Setting.Appearance),
            Setting.Number("cornerRadius", "Corner radius", 0, 0, 1000, group: Setting.Appearance),
            Setting.Toggle("recolor", "Recolour", false, group: Setting.Appearance,
                help: "Draws the picture's shape in the colour below, e.g. a white logo in the theme's accent."),
            Setting.Color("color", "Colour", "accent"),
        ],
        (settings, theme) => new ImageWidget(settings, theme));

    /// <summary>Decode in steps of this many pixels, so resizing in the editor doesn't decode the file at every size.</summary>
    private const int DecodeStep = 128;

    private readonly string? _file;
    private readonly string _fit;
    private readonly bool _recolor;
    private readonly Brush _color;
    private readonly Border _picture;
    private readonly Border _placeholder;
    private (int Width, int Height) _decoded;

    public ImageWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _file = settings.OptionalString(LayoutPackage.ImageFileKey) is { Length: > 0 } file ? file : null;
        _fit = BackgroundImage.NormalizeFit(settings.String("fit"));
        _recolor = settings.Bool("recolor");
        _color = theme.Resolve(settings.String("color"), theme.Accent);

        _picture = new Border { CornerRadius = new CornerRadius(settings.Number("cornerRadius")), Opacity = settings.Number("opacity") };
        Children.Add(_picture);

        // Only while editing, so an image widget without a picture can still be found and set up.
        var hint = CreateText(18, theme.Secondary, HorizontalAlignment.Center);
        hint.Text = _file is null ? "Choose a picture" : "Picture not found";
        hint.VerticalAlignment = VerticalAlignment.Center;
        _placeholder = new Border
        {
            BorderBrush = theme.Secondary,
            BorderThickness = new Thickness(1),
            Child = hint,
            Visibility = Visibility.Collapsed,
        };
        Children.Add(_placeholder);

        SizeChanged += (_, _) => LoadPicture();
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        _placeholder.Visibility = Editing && _picture.Background is null && _picture.OpacityMask is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadPicture()
    {
        if (_file is null || ActualWidth <= 0 || ActualHeight <= 0)
            return;
        var size = (Step(ActualWidth), Step(ActualHeight));
        if (size == _decoded)
            return;
        _decoded = size;

        if (BackgroundImage.Load(_file, _fit, size.Item1, size.Item2) is not { } bitmap)
        {
            _picture.Background = null;
            _picture.OpacityMask = null;
            return;
        }

        var brush = new ImageBrush(bitmap)
        {
            Stretch = _fit switch
            {
                "fill" => Stretch.UniformToFill,
                "fit" => Stretch.Uniform,
                "stretch" => Stretch.Fill,
                _ => Stretch.None,
            },
            TileMode = _fit == "tile" ? TileMode.Tile : TileMode.None,
        };
        if (_fit == "tile")
        {
            brush.ViewportUnits = BrushMappingMode.Absolute;
            brush.Viewport = new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight);
        }
        brush.Freeze();

        if (_recolor)
        {
            // The picture's alpha becomes the shape; the colour fills it.
            _picture.Background = _color;
            _picture.OpacityMask = brush;
        }
        else
        {
            _picture.Background = brush;
            _picture.OpacityMask = null;
        }
    }

    private static int Step(double pixels) => (int)Math.Ceiling(pixels / DecodeStep) * DecodeStep;
}
