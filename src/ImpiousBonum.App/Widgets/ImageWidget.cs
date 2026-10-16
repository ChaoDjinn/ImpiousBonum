using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Games;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// A picture from a file: a logo, artwork, a photo. Decoded at about the size it's shown, so a big file stays cheap.
/// With <c>recolor</c> on, a one-colour logo is drawn in a theme colour instead, so it follows the theme.
/// With <c>source</c> <c>game</c>, it's the game you're playing instead (<see cref="GameArtProvider"/>).
/// </summary>
public sealed class ImageWidget : Widget
{
    public const string FileSource = "file";
    public const string GameSource = "game";

    public static WidgetDescriptor Descriptor { get; } = new(
        "image", "Image",
        "A logo, artwork or a photo from a file, packed into exported layouts so it travels with them, or the cover art of "
        + "the game you're playing.",
        240, 240,
        [
            Setting.Choice("source", "Show", FileSource, [FileSource, GameSource], group: Setting.Content,
                help: "file shows the picture below. game shows the game you're playing: Steam's art for it, or its icon."),
            new SettingDescriptor(LayoutPackage.ImageFileKey, "Picture", SettingKind.ImageFile, null, Setting.Content, "A PNG, JPG, GIF, BMP or TIFF file."),
            Setting.Choice("art", "Game picture", GameArt.CoverKind, GameArt.Kinds, group: Setting.Content,
                help: "For a Steam game: cover is the tall box art, header the wide banner, hero the big picture from its library page. "
                + "Other games show their icon."),
            Setting.Toggle("hideNoGame", "Hide when there's no game", false, group: Setting.Content,
                help: "Otherwise it's empty while no game is in front. It shows faded while you edit the layout."),
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
    // With source game: which picture, whether to hide without a game, and the game's pictures now.
    private readonly bool _game;
    private readonly string _art;
    private readonly bool _hideNoGame;
    private GameArt? _gameArt;

    public ImageWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _file = settings.OptionalString(LayoutPackage.ImageFileKey) is { Length: > 0 } file ? file : null;
        _fit = BackgroundImage.NormalizeFit(settings.String("fit"));
        _recolor = settings.Bool("recolor");
        _color = theme.Resolve(settings.String("color"), theme.Accent);
        _game = string.Equals(settings.String("source"), GameSource, StringComparison.OrdinalIgnoreCase);
        _art = settings.String("art");
        _hideNoGame = settings.Bool("hideNoGame");

        _picture = new Border { CornerRadius = new CornerRadius(settings.Number("cornerRadius")), Opacity = settings.Number("opacity") };
        Children.Add(_picture);

        // Only while editing, so an image widget without a picture can still be found and set up.
        var hint = CreateText(18, theme.Secondary, HorizontalAlignment.Center);
        hint.Text = _game ? "No game in front" : _file is null ? "Choose a picture" : "Picture not found";
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
        if (_game)
        {
            var art = store.GetAttachment(GameArtProvider.Art) as GameArt;
            SetUnavailable(_hideNoGame && art is null);
            if (!ReferenceEquals(art, _gameArt))
            {
                // Another game, or none: decode its picture afresh.
                _gameArt = art;
                _decoded = default;
                LoadPicture();
            }
        }
        _placeholder.Visibility = Editing && _picture.Background is null && _picture.OpacityMask is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadPicture()
    {
        if ((_file is null && !_game) || ActualWidth <= 0 || ActualHeight <= 0)
            return;
        var size = (Step(ActualWidth), Step(ActualHeight));
        if (size == _decoded)
            return;
        _decoded = size;
        // At least the stepped size, but in the widget's own shape, so fill crops the picture to what shows and no more.
        var scale = Math.Max(size.Item1 / ActualWidth, size.Item2 / ActualHeight);
        var (width, height) = (ActualWidth * scale, ActualHeight * scale);

        // A game's icon is small and square, so it's shown whole whatever the fit.
        var picture = _gameArt?.Picture(_art);
        var fit = _gameArt is not null && _gameArt.IsIcon(picture) ? "fit" : _fit;
        var loaded = _game
            ? picture is null ? null : BackgroundImage.Load(picture, fit, width, height)
            : BackgroundImage.Load(_file!, fit, width, height);
        if (loaded is not { } bitmap)
        {
            _picture.Background = null;
            _picture.OpacityMask = null;
            return;
        }

        var brush = new ImageBrush(bitmap)
        {
            Stretch = fit switch
            {
                "fill" => Stretch.UniformToFill,
                "fit" => Stretch.Uniform,
                "stretch" => Stretch.Fill,
                _ => Stretch.None,
            },
            TileMode = fit == "tile" ? TileMode.Tile : TileMode.None,
        };
        if (fit == "tile")
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
