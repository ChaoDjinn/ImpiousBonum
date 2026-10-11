using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// What's playing in any app that shows in Windows' media controls: album art, title and artist, progress, and
/// previous / play-pause / next buttons. Hides while nothing is playing, unless told not to.
/// </summary>
public sealed class MediaWidget : Widget
{
    public static WidgetDescriptor Descriptor { get; } = new(
        "media", "Now playing",
        "What's playing in Spotify, a browser, Media Player or any app in Windows' media controls: album art, title, "
        + "artist, progress and buttons to skip or pause.",
        640, 160,
        [
            Setting.Template("title", "Title", $"{{{MediaProvider.Title}}}"),
            Setting.Template("subtitle", "Second line", $"{{{MediaProvider.Artist}}}", help: "E.g. the artist, or {media.artist} · {media.album}. Empty hides it."),
            Setting.Number("fontSize", "Title size", 34, 6, 200),
            Setting.Number("subtitleSize", "Second line size", null, 6, 200, help: "Empty uses 70% of the title size."),
            Setting.Toggle("showArt", "Album art", true, group: Setting.Appearance),
            Setting.Toggle("showProgress", "Progress bar", true, group: Setting.Appearance, help: "With the position and length under it."),
            Setting.Toggle("showControls", "Buttons", true, group: Setting.Appearance, help: "Previous, play/pause and next. They work like the keyboard's media keys."),
            Setting.Number("controlSize", "Button size", 52, 16, 200, group: Setting.Appearance),
            Setting.Toggle("hideIdle", "Hide when nothing is playing", true, group: Setting.Appearance,
                help: "It shows faded while you edit the layout."),
        ],
        (settings, theme) => new MediaWidget(settings, theme));

    private const string Idle = "Nothing playing";

    private readonly ValueTemplate _title;
    private readonly ValueTemplate? _subtitle;
    private readonly bool _hideIdle;
    private readonly TextBlock _titleText;
    private readonly TextBlock? _subtitleText;
    private readonly Border? _art;
    private readonly FrameworkElement? _artPlaceholder;
    private readonly Grid? _progress;
    private readonly TextBlock? _position;
    private readonly TextBlock? _duration;
    private readonly ContentControl? _playPause;
    private readonly FrameworkElement? _playIcon;
    private readonly FrameworkElement? _pauseIcon;
    private object? _artwork;

    public MediaWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _title = ValueTemplate.Parse(settings.String("title"));
        _subtitle = settings.OptionalString("subtitle") is { Length: > 0 } subtitle ? ValueTemplate.Parse(subtitle) : null;
        _hideIdle = settings.Bool("hideIdle");
        var fontSize = settings.Number("fontSize");
        var subtitleSize = settings.OptionalNumber("subtitleSize") ?? fontSize * 0.7;

        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (settings.Bool("showArt"))
        {
            // A square as tall as the widget; the picture fills it, cropped to the middle.
            _art = new Border { CornerRadius = new CornerRadius(8), Background = theme.Track, Margin = new Thickness(0, 0, fontSize * 0.6, 0) };
            _art.SetBinding(WidthProperty, new Binding(nameof(ActualHeight)) { RelativeSource = RelativeSource.Self });
            _artPlaceholder = Icons.Create("music", theme.Secondary, fontSize * 1.4, 1.5);
            _artPlaceholder.HorizontalAlignment = HorizontalAlignment.Center;
            _artPlaceholder.VerticalAlignment = VerticalAlignment.Center;
            _art.Child = _artPlaceholder;
            Children.Add(_art);
        }

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        SetColumn(info, 1);
        Children.Add(info);

        _titleText = CreateText(fontSize);
        _titleText.TextTrimming = TextTrimming.CharacterEllipsis;
        info.Children.Add(_titleText);
        if (_subtitle is not null)
        {
            _subtitleText = CreateText(subtitleSize, theme.Secondary);
            _subtitleText.TextTrimming = TextTrimming.CharacterEllipsis;
            info.Children.Add(_subtitleText);
        }

        if (settings.Bool("showProgress"))
        {
            _progress = new Grid { Height = Math.Max(3, fontSize * 0.15), Background = theme.Track, Margin = new Thickness(0, fontSize * 0.35, 0, 0) };
            _progress.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) });
            _progress.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _progress.Children.Add(new Border { Background = theme.Accent });
            info.Children.Add(_progress);

            var times = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            _position = CreateText(subtitleSize * 0.75, theme.Secondary);
            _duration = CreateText(subtitleSize * 0.75, theme.Secondary, HorizontalAlignment.Right);
            times.Children.Add(_position);
            times.Children.Add(_duration);
            info.Children.Add(times);
        }

        if (settings.Bool("showControls"))
        {
            var size = settings.Number("controlSize");
            var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(fontSize * 0.5, 0, 0, 0) };
            controls.Children.Add(Control("previous", WidgetAction.Previous, size * 0.7));
            _playIcon = Icons.Create("play", theme.Foreground, size, 1.5, filled: true);
            _pauseIcon = Icons.Create("pause", theme.Foreground, size, 1.5, filled: true);
            _playPause = new ContentControl { Content = _playIcon, Focusable = false };
            controls.Children.Add(Tap(_playPause, WidgetAction.PlayPause, size));
            controls.Children.Add(Control("next", WidgetAction.Next, size * 0.7));
            SetColumn(controls, 2);
            Children.Add(controls);
        }
    }

    private FrameworkElement Control(string icon, string action, double size) => Tap(Icons.Create(icon, Theme.Foreground, size, 1.5, filled: true), action, size);

    private TapTarget Tap(FrameworkElement content, string action, double size)
    {
        // A finger-sized target around a smaller glyph.
        var target = new TapTarget
        {
            Child = content,
            Background = Brushes.Transparent,
            Padding = new Thickness(size * 0.2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        target.Tapped += (_, _) => RequestAction(new WidgetAction(action));
        return target;
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        var playing = Has(store, MediaProvider.Title) || Has(store, MediaProvider.Artist);
        SetUnavailable(_hideIdle && !playing);

        _titleText.Text = playing ? _title.Render(store) : Idle;
        if (_subtitleText is not null)
            _subtitleText.Text = playing ? _subtitle!.Render(store) : string.Empty;

        if (_progress is not null)
        {
            var fraction = store.TryGet(MediaProvider.Progress, out var progress) && progress.Value is double p ? Math.Clamp(p / 100, 0, 1) : 0;
            _progress.ColumnDefinitions[0].Width = new GridLength(fraction, GridUnitType.Star);
            _progress.ColumnDefinitions[1].Width = new GridLength(1 - fraction, GridUnitType.Star);
            _position!.Text = Text(store, MediaProvider.Position);
            _duration!.Text = Text(store, MediaProvider.Duration);
        }

        if (_playPause is not null)
            _playPause.Content = Text(store, MediaProvider.Status) == "Playing" ? _pauseIcon : _playIcon;

        if (_art is not null)
            ShowArtwork(playing ? store.GetAttachment(MediaProvider.Artwork) : null);
    }

    private void ShowArtwork(object? artwork)
    {
        if (ReferenceEquals(artwork, _artwork))
            return;
        _artwork = artwork;
        var image = artwork is byte[] bytes ? Decode(bytes, ActualHeight) : null;
        _art!.Background = image is null ? Theme.Track : new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        _art.Child = image is null ? _artPlaceholder : null;
    }

    /// <summary>Decodes album art at about the size it's shown, so a large cover doesn't cost memory. Null if it isn't an image.</summary>
    internal static BitmapSource? Decode(byte[] bytes, double size)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(bytes);
            if (size > 0)
                image.DecodePixelHeight = (int)Math.Clamp(size * 2, 64, 1024);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static bool Has(MetricStore store, string id) => store.TryGet(id, out var sample) && !string.IsNullOrEmpty(sample.Text);

    private static string Text(MetricStore store, string id) => store.TryGet(id, out var sample) ? sample.Text ?? string.Empty : string.Empty;
}
