using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.Core.Demo;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// A slideshow of the pictures in a folder, like a digital photo frame: one photo at a time, fading into the next every
/// so many seconds or when tapped. Only the photo on show and the next one are decoded, at about the size they're shown
/// and off the UI thread, so a folder of big photos stays cheap; the folder is listed again every few minutes for photos
/// added and removed. While the dashboard is hidden, nothing moves on and nothing is read.
/// </summary>
public sealed class PhotoFrameWidget : Widget
{
    public static readonly IReadOnlyList<string> Fits = ["fill", "fit"];

    public static WidgetDescriptor Descriptor { get; } = new(
        "photos", "Photo frame",
        "A slideshow of the pictures in a folder, like a digital photo frame; tap it for the next one. The folder is on this PC, so exported and shared layouts leave it out.",
        480, 320,
        [
            new SettingDescriptor(LayoutPackage.PhotoFolderKey, "Folder", SettingKind.Folder, null, Setting.Content,
                "The folder of pictures to show: PNG, JPG, GIF, BMP and TIFF files. It stays on this PC: exported and shared layouts leave it out."),
            Setting.Toggle("subfolders", "Include subfolders", true, group: Setting.Content, help: "The pictures in the folders inside it too."),
            Setting.Number("seconds", "Seconds per photo", 30, 3, 86400, group: Setting.Content, help: "How long each photo shows. A tap moves on straight away."),
            Setting.Choice("order", "Order", PhotoAlbum.Shuffle, PhotoAlbum.Orders, group: Setting.Content,
                help: "shuffle shows every photo, in a random order, before any comes round again; name goes from A to Z; date goes from oldest to newest, by when the files last changed."),
            Setting.Choice("caption", "Caption", "none", ["none", "name", "date"], group: Setting.Content,
                help: "Along the bottom of the photo: its file name, or the date it was taken (or else the file's date)."),
            Setting.PlainText("dateFormat", "Date format", "d MMMM yyyy",
                help: "For a date caption: a .NET date format, e.g. d MMMM yyyy (14 August 2026) or d MMM yyyy, HH:mm (14 Aug 2026, 20:31)."),
            Setting.Number("fontSize", "Caption size", 18, 6, 200),
            Setting.ShrinkToFit(),
            Setting.Choice("fit", "Fit", "fill", Fits, group: Setting.Appearance, help: "fill covers the box, cropping the edges; fit shows the whole photo."),
            Setting.Number("fade", "Fade", 1, 0, 5, group: Setting.Appearance, help: "How many seconds one photo takes to fade into the next; 0 changes straight away."),
            Setting.Number("cornerRadius", "Corner radius", 0, 0, 1000, group: Setting.Appearance),
            Setting.Number("opacity", "Opacity", 1, 0, 1, group: Setting.Appearance),
            Setting.Color("captionColor", "Caption colour", "foreground", help: "Over a shade of the theme's background, so it can be read over any photo."),
        ],
        (settings, theme) => new PhotoFrameWidget(settings, theme));

    /// <summary>How often the folder is listed again, for photos added and removed.</summary>
    internal static readonly TimeSpan ListEvery = TimeSpan.FromMinutes(5);

    /// <summary>Decode in steps of this many pixels, so resizing in the editor doesn't decode at every size.</summary>
    private const int DecodeStep = 128;

    /// <summary>How many folders <see cref="Remembered"/> keeps.</summary>
    private const int RememberedFolders = 4;

    /// <summary>
    /// What the last frame showing each folder found and showed, so one built again (after an edit, or when its page or
    /// layout comes back) carries on with the same photo rather than starting again: already decoded, for the folder
    /// shown last.
    /// </summary>
    private static readonly Dictionary<string, Memory> Remembered = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The picture a frame with no folder shows with the demo readings, drawn once.</summary>
    private static readonly Lazy<BitmapSource> DemoPicture = new(() => DemoArtwork.Landscape());

    /// <summary>Clear at the top to mostly solid at the bottom: the shade behind a caption.</summary>
    private static readonly Brush Shade = CreateShade();

    private readonly string? _folder;
    private readonly bool _subfolders;
    private readonly string _key;
    private readonly TimeSpan _duration;
    private readonly string _fit;
    private readonly TimeSpan _fade;
    private readonly double _cornerRadius;
    private readonly string _caption;
    private readonly string _dateFormat;
    private readonly double _fontSize;
    private readonly Brush _captionColor;
    private readonly PhotoAlbum _album;

    /// <summary>The photo on show, over the one it's fading in from.</summary>
    private readonly Grid _stage;
    private readonly Border _placeholder;
    private readonly TextBlock _hint;

    /// <summary>The size photos are decoded for: the widget's, in steps. Empty until it has one.</summary>
    private (int Width, int Height) _size;

    /// <summary>The last refresh's time, which the slideshow keeps to.</summary>
    private DateTime _now;

    /// <summary>Whether the folder has been listed, when, and what was found (null if it couldn't be read).</summary>
    private bool _listed;
    private DateTime _listedAt;
    private IReadOnlyList<PhotoFile>? _files;

    /// <summary>The photo on show, and since when.</summary>
    private Decoded? _shown;
    private DateTime _since;

    /// <summary>The next photo, decoded ahead so it's ready when its time comes.</summary>
    private Decoded? _next;

    /// <summary>The photo the last frame showing this folder had on show, to start with (see <see cref="Remembered"/>).</summary>
    private Decoded? _resume;

    /// <summary>Listing the folder or decoding a photo, which happen one at a time.</summary>
    private bool _busy;

    /// <summary>Tapped: on to the next photo as soon as it's ready.</summary>
    private bool _skip;

    /// <summary>Taken off its dashboard (rebuilt, say), so anything still under way is let go.</summary>
    private bool _gone;

    public PhotoFrameWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        // Explorer's Copy as path puts the folder in quotes.
        _folder = settings.OptionalString(LayoutPackage.PhotoFolderKey)?.Trim().Trim('"') is { Length: > 0 } folder ? folder : null;
        _subfolders = settings.Bool("subfolders");
        _key = $"{_folder}|{_subfolders}";
        _duration = TimeSpan.FromSeconds(settings.Number("seconds"));
        _fit = settings.String("fit") == "fit" ? "fit" : "fill";
        _fade = TimeSpan.FromSeconds(settings.Number("fade"));
        _cornerRadius = settings.Number("cornerRadius");
        _caption = settings.String("caption");
        _dateFormat = settings.String("dateFormat");
        _fontSize = settings.Number("fontSize");
        _captionColor = theme.Resolve(settings.String("captionColor"), theme.Foreground);
        _album = new PhotoAlbum(settings.String("order"));

        _stage = new Grid { Opacity = settings.Number("opacity") };
        if (_folder is null)
            Children.Add(_stage);
        else
        {
            var tap = new TapTarget { Child = _stage, Background = Brushes.Transparent };
            tap.Tapped += (_, _) => NextPhoto();
            Children.Add(tap);
        }

        // Only while editing, so a frame with nothing to show can still be found and set up.
        _hint = CreateText(18, theme.Secondary, HorizontalAlignment.Center);
        _hint.VerticalAlignment = VerticalAlignment.Center;
        _hint.TextWrapping = TextWrapping.Wrap;
        _placeholder = new Border
        {
            BorderBrush = theme.Secondary,
            BorderThickness = new Thickness(1),
            Child = _hint,
            Visibility = Visibility.Collapsed,
        };
        Children.Add(_placeholder);

        if (_folder is not null)
            Recall();
        SizeChanged += (_, _) => Resized();
    }

    /// <summary>The photo on show, for tests.</summary>
    internal string? ShownPath => _shown?.File.Path;

    /// <summary>The photo decoded ahead, for tests.</summary>
    internal string? NextPath => _next?.File.Path;

    /// <summary>Whether the folder's being listed or a photo decoded, for tests.</summary>
    internal bool Busy => _busy;

    public override void Refresh(MetricStore store, DateTime now)
    {
        _now = now;
        if (_folder is not null)
            Pump();
        else if (!Editing && _shown is null && store.GetAttachment(DemoReadings.Photo) is DemoPhoto demo)
        {
            // A snapshot with the demo readings has a made-up photo rather than an empty box.
            var file = new PhotoFile(demo.Name, demo.Taken);
            Show(new Decoded(file, new Photo(DemoPicture.Value, demo.Taken), default, _fit), fade: false);
        }
        ShowHint();
    }

    /// <summary>
    /// Whether the slideshow goes on: while it's on screen, or in a copy drawn off screen (the tablet view, a USB screen),
    /// which is only refreshed while something's showing it. On a hidden dashboard, or a page that isn't showing, it waits.
    /// </summary>
    private bool OnShow => IsVisible || PresentationSource.FromVisual(this) is null;

    /// <summary>
    /// Does the next thing the slideshow needs, one at a time and off the UI thread: lists the folder when that's due,
    /// decodes the photo to show (the first, or again at a new size), decodes the next one ahead, and shows it when its
    /// time comes or the frame's tapped. Called on each refresh and whenever one of those finishes.
    /// </summary>
    private void Pump()
    {
        if (_folder is null || _busy || _gone || _size == default || _now == default || !OnShow)
            return;
        if (!_listed || _now - _listedAt >= ListEvery || _now < _listedAt)
        {
            List();
            return;
        }
        if (_album.Current is not { } current)
        {
            if (_album.Advance() is { } first)
            {
                _since = _now;
                Decode(first, show: true);
            }
            return;
        }
        if (_shown is not { } shown || shown.File != current || shown.Size != _size || shown.Fit != _fit)
        {
            Decode(current, show: true);
            return;
        }
        if (_album.Next is not { } next || next == current)
        {
            // Only one photo to show: nothing to move on to.
            _skip = false;
            return;
        }
        if (_next is not { } ready || ready.File != next || ready.Size != _size)
        {
            Decode(next, show: false);
            return;
        }
        // From now if the clock goes back, as the weather's places do.
        if (_now < _since)
            _since = _now;
        if (_skip || _now - _since >= _duration)
        {
            _skip = false;
            _album.Advance();
            _since = _now;
            _next = null;
            Show(ready, fade: true);
            Pump();
        }
    }

    /// <summary>A tap: on to the next photo now, or as soon as it's decoded.</summary>
    private void NextPhoto()
    {
        _skip = true;
        Pump();
    }

    /// <summary>Lists the folder off the UI thread, then carries on.</summary>
    private void List()
    {
        _busy = true;
        var (folder, subfolders, dispatcher) = (_folder!, _subfolders, Dispatcher);
        Task.Run(() =>
        {
            IReadOnlyList<PhotoFile>? files = null;
            try
            {
                files = PhotoAlbum.List(folder, subfolders);
            }
            finally
            {
                dispatcher.InvokeAsync(() => OnListed(files));
            }
        });
    }

    private void OnListed(IReadOnlyList<PhotoFile>? files)
    {
        _busy = false;
        if (_gone)
            return;
        // A folder that was away (on a USB drive taken out, say) and is back: its photos get another go.
        if (files is not null && _listed && _files is null)
            _album.Retry();
        (_listed, _listedAt, _files) = (true, _now, files);
        if (files is not null)
            _album.Update(files);
        Remember();
        ShowHint();
        Pump();
    }

    /// <summary>Decodes <paramref name="file"/> off the UI thread, to show now or as the next one, then carries on.</summary>
    private void Decode(PhotoFile file, bool show)
    {
        _busy = true;
        var (size, fit, dispatcher) = (_size, _fit, Dispatcher);
        Task.Run(() =>
        {
            Photo? photo = null;
            try
            {
                photo = PhotoAlbum.Decode(file.Path, fit, size.Width, size.Height);
            }
            finally
            {
                dispatcher.InvokeAsync(() => OnDecoded(file, photo is null ? null : new Decoded(file, photo, size, fit), show));
            }
        });
    }

    private void OnDecoded(PhotoFile file, Decoded? photo, bool show)
    {
        _busy = false;
        if (_gone)
            return;
        if (photo is null)
        {
            // Gone, still being copied in, or not a picture after all: passed over.
            _album.Fail(file);
            if (show && _album.Current == file)
            {
                _album.Advance();
                _since = _now;
            }
        }
        else if (show)
            Show(photo, fade: _shown is not null && _shown.File != file);
        else
            _next = photo;
        ShowHint();
        Pump();
    }

    private void Resized()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
            return;
        var size = (Step(ActualWidth), Step(ActualHeight));
        if (size == _size)
            return;
        _size = size;
        // Straight away, so a frame built again doesn't flash empty; if it was for another size, it's decoded again.
        if (_resume is { } resume)
        {
            _resume = null;
            Show(resume, fade: false);
        }
        Pump();
    }

    private static int Step(double pixels) => (int)Math.Ceiling(pixels / DecodeStep) * DecodeStep;

    /// <summary>Puts <paramref name="photo"/> on show; with <paramref name="fade"/>, it fades in over the last one.</summary>
    private void Show(Decoded photo, bool fade)
    {
        _shown = photo;
        // Only the photo it fades in over stays behind it; any older (after a quick tap or two) goes.
        while (_stage.Children.Count > 1)
            _stage.Children.RemoveAt(0);
        var behind = _stage.Children.Count > 0 ? _stage.Children[0] : null;
        var slide = Slide(photo.Photo, CaptionFor(photo.File, photo.Photo));
        _stage.Children.Add(slide);

        if (behind is not null)
        {
            // Fades only show on screen; a copy drawn once a second (the tablet view) changes straight away.
            if (fade && _fade > TimeSpan.Zero && IsVisible)
                FadeIn(slide, behind);
            else
                _stage.Children.Remove(behind);
        }
        if (_folder is not null)
            Remember();
        ShowHint();
    }

    private void FadeIn(UIElement slide, UIElement behind)
    {
        var fadeIn = new DoubleAnimation(0, 1, _fade);
        fadeIn.Completed += (_, _) => _stage.Children.Remove(behind);
        slide.BeginAnimation(OpacityProperty, fadeIn);
        // The last photo goes in the second half, so the dashboard barely shows through between the two.
        var half = TimeSpan.FromTicks(_fade.Ticks / 2);
        behind.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, half) { BeginTime = half });
    }

    /// <summary>A photo with its corners rounded and its caption; with fit, it has the photo's shape, so they're the photo's.</summary>
    private FrameworkElement Slide(Photo photo, string? caption)
    {
        var picture = new ImageBrush(photo.Picture) { Stretch = _fit == "fit" ? Stretch.Fill : Stretch.UniformToFill };
        picture.Freeze();
        var slide = new Grid();
        slide.Children.Add(new Border { Background = picture, CornerRadius = new CornerRadius(_cornerRadius) });
        if (!string.IsNullOrEmpty(caption))
            AddCaption(slide, caption);
        return _fit == "fit" ? new AspectBox { Aspect = (double)photo.Picture.PixelWidth / photo.Picture.PixelHeight, Child = slide } : slide;
    }

    /// <summary>The caption along the bottom, over a shade of the theme's background that fades out upwards, so it can be read over any photo.</summary>
    private void AddCaption(Grid slide, string caption)
    {
        slide.Children.Add(new Border
        {
            Background = Theme.Background,
            OpacityMask = Shade,
            CornerRadius = new CornerRadius(0, 0, _cornerRadius, _cornerRadius),
            Height = _fontSize * 2.8,
            VerticalAlignment = VerticalAlignment.Bottom,
        });
        var text = CreateText(_fontSize, _captionColor);
        text.Text = caption;
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        text.VerticalAlignment = VerticalAlignment.Bottom;
        text.Margin = new Thickness(_fontSize * 0.7, 0, _fontSize * 0.7, _fontSize * 0.55);
        slide.Children.Add(Fit(text, FitDirection.Width));
    }

    private static Brush CreateShade()
    {
        var shade = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0xC0, 0, 0, 0), 90);
        shade.Freeze();
        return shade;
    }

    /// <summary>What the caption says for a photo: its name without the extension, or the date it was taken; null for none.</summary>
    private string? CaptionFor(PhotoFile file, Photo photo) => _caption switch
    {
        "name" => Path.GetFileNameWithoutExtension(file.Path),
        "date" => FormatDate(photo.Taken ?? file.Modified.ToLocalTime()),
        _ => null,
    };

    private string FormatDate(DateTime date)
    {
        try
        {
            return date.ToString(_dateFormat, CultureInfo.CurrentCulture);
        }
        catch (FormatException)
        {
            // A half-typed format shouldn't take the dashboard down.
            return _dateFormat;
        }
    }

    /// <summary>While editing a frame with nothing to show, says why, so it can still be found and set up.</summary>
    private void ShowHint()
    {
        _hint.Text = _folder is null ? "Choose a folder"
            : !_listed ? string.Empty
            : _files is null ? "Folder not found"
            : _album.IsEmpty ? "No pictures in this folder"
            : string.Empty;
        _placeholder.Visibility = Editing && _stage.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Picks up where the last frame showing this folder left off, if it's remembered.</summary>
    private void Recall()
    {
        Memory? memory;
        lock (Remembered)
            Remembered.TryGetValue(_key, out memory);
        if (memory is null)
            return;
        if (memory.Shown is { } shown)
        {
            _album.StartFrom(shown);
            _since = memory.Since;
            _resume = memory.Picture;
        }
        (_listed, _listedAt, _files) = (memory.Listed, memory.ListedAt, memory.Files);
        if (_files is not null)
            _album.Update(_files);
    }

    /// <summary>
    /// Remembers what this frame found and shows, for the next one built for its folder. Only this folder keeps its
    /// photo decoded; the others keep which photo it was, so a big picture isn't held for a frame that's gone.
    /// </summary>
    private void Remember()
    {
        lock (Remembered)
        {
            if (Remembered.Count >= RememberedFolders && !Remembered.ContainsKey(_key))
                Remembered.Clear();
            foreach (var (key, memory) in Remembered.Where(m => m.Value.Picture is not null).ToList())
                Remembered[key] = memory with { Picture = null };
            Remembered[_key] = new Memory(_files, _listed, _listedAt, _shown?.File, _since, _shown);
        }
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        _gone = VisualParent is null && oldParent is not null;
    }

    /// <summary>A photo decoded for a box of <paramref name="Size"/> with <paramref name="Fit"/>.</summary>
    private sealed record Decoded(PhotoFile File, Photo Photo, (int Width, int Height) Size, string Fit);

    /// <summary>What <see cref="Remembered"/> keeps for a folder: its listing, the photo on show and since when, and that photo decoded.</summary>
    private sealed record Memory(IReadOnlyList<PhotoFile>? Files, bool Listed, DateTime ListedAt, PhotoFile? Shown, DateTime Since, Decoded? Picture);

    /// <summary>Lays its child out as big as it fits with the shape <see cref="Aspect"/> (width over height), centred.</summary>
    private sealed class AspectBox : Decorator
    {
        public double Aspect { get; init; } = 1;

        protected override Size MeasureOverride(Size constraint)
        {
            var size = Fitted(constraint);
            Child?.Measure(size);
            return size;
        }

        protected override Size ArrangeOverride(Size arrangeSize)
        {
            var size = Fitted(arrangeSize);
            Child?.Arrange(new Rect(new Point((arrangeSize.Width - size.Width) / 2, (arrangeSize.Height - size.Height) / 2), size));
            return arrangeSize;
        }

        private Size Fitted(Size space)
        {
            if (double.IsInfinity(space.Width) && double.IsInfinity(space.Height))
                return default;
            var width = Math.Min(space.Width, space.Height * Aspect);
            return new Size(width, width / Aspect);
        }
    }
}
