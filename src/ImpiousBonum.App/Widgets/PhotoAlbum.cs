using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImpiousBonum.App.Layout;
using FileTime = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace ImpiousBonum.App.Widgets;

/// <summary>A picture in a photo frame's folder, and when its file last changed (UTC).</summary>
public sealed record PhotoFile(string Path, DateTime Modified);

/// <summary>A photo decoded for showing, the right way up, with when it was taken if it says.</summary>
public sealed record Photo(BitmapSource Picture, DateTime? Taken);

/// <summary>
/// The photos in a photo frame's folder, and which comes next: shuffled (every one before any comes round again), by
/// name or by date. Photos that can't be shown are passed over until their file changes. This only keeps the order;
/// the widget lists the folder and decodes the photos off the UI thread, with <see cref="List"/> and <see cref="Decode"/>.
/// </summary>
public sealed class PhotoAlbum
{
    public const string Shuffle = "shuffle";
    public const string ByName = "name";
    public const string ByDate = "date";

    public static readonly IReadOnlyList<string> Orders = [Shuffle, ByName, ByDate];

    /// <summary>
    /// The most photos a folder is listed with. Every photo found costs a little memory, so a folder with whole drives of
    /// pictures in it shows the first this many found.
    /// </summary>
    public const int MostPhotos = 10_000;

    /// <summary>Names as Explorer sorts them: IMG2 before IMG10.</summary>
    private static readonly StringComparer Names = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);

    private readonly string _order;
    private readonly Random _random;
    private IReadOnlyList<PhotoFile> _files = [];

    /// <summary>The photos still to come this time round, in order. Failed ones are passed over when they come up.</summary>
    private List<PhotoFile> _queue = [];

    private readonly HashSet<PhotoFile> _failed = [];

    /// <param name="order"><see cref="Shuffle"/>, <see cref="ByName"/> or <see cref="ByDate"/>.</param>
    /// <param name="random">For shuffling; a fixed one in tests.</param>
    public PhotoAlbum(string order, Random? random = null)
    {
        _order = Orders.Contains(order) ? order : Shuffle;
        _random = random ?? Random.Shared;
    }

    /// <summary>The photo on show, or null before the first (or once none can be shown).</summary>
    public PhotoFile? Current { get; private set; }

    /// <summary>Whether none of the photos can be shown: there are none, or none could be read.</summary>
    public bool IsEmpty => _files.All(_failed.Contains);

    /// <summary>
    /// The photo after <see cref="Current"/>, to decode ahead. When every photo has had its turn they come round again:
    /// a new shuffle, which never starts with the photo just shown, or the first by name or date. With only one photo,
    /// it's that one again.
    /// </summary>
    public PhotoFile? Next
    {
        get
        {
            if (_queue.FirstOrDefault(Usable) is { } next)
                return next;
            _queue = [.. Ordered(_files.Where(Usable))];
            if (_order == Shuffle && _queue.Count > 1 && _queue[0] == Current)
            {
                var swap = _random.Next(1, _queue.Count);
                (_queue[0], _queue[swap]) = (_queue[swap], _queue[0]);
            }
            return _queue.FirstOrDefault();
        }
    }

    /// <summary>Moves on to <see cref="Next"/>, which becomes <see cref="Current"/>, and returns it.</summary>
    public PhotoFile? Advance()
    {
        var next = Next;
        if (next is not null)
            _queue.RemoveRange(0, _queue.IndexOf(next) + 1);
        Current = next;
        return next;
    }

    /// <summary>Carries on from <paramref name="photo"/>, as another frame showing this folder last did (before it's listed).</summary>
    public void StartFrom(PhotoFile photo)
    {
        Current = photo;
        _queue.Remove(photo);
    }

    /// <summary>
    /// Takes a new listing of the folder. The photo on show stays on show; the order carries on from it, with photos
    /// that have gone left out and new ones fitted in: in their place by name or date, or anywhere still to come in a
    /// shuffle.
    /// </summary>
    public void Update(IReadOnlyList<PhotoFile> files)
    {
        var listed = files.ToHashSet();
        // A changed file is a new one here, so a photo that failed while it was still being copied in gets another go.
        _failed.IntersectWith(listed);
        if (_order == Shuffle)
        {
            var known = _files.ToHashSet();
            _queue = [.. _queue.Where(listed.Contains)];
            foreach (var file in files.Where(f => !known.Contains(f) && f != Current))
                _queue.Insert(_random.Next(_queue.Count + 1), file);
        }
        else
        {
            var current = Current;
            _queue = [.. Ordered(files).Where(f => current is null || Compare(f, current) > 0)];
        }
        _files = files;
    }

    /// <summary>Passes over <paramref name="photo"/> from now on, as it couldn't be shown, until its file changes.</summary>
    public void Fail(PhotoFile photo) => _failed.Add(photo);

    /// <summary>Gives every photo that couldn't be shown another go: the folder was away (on a drive taken out, say) and is back.</summary>
    public void Retry() => _failed.Clear();

    private bool Usable(PhotoFile photo) => !_failed.Contains(photo);

    private IEnumerable<PhotoFile> Ordered(IEnumerable<PhotoFile> files) =>
        _order == Shuffle ? files.OrderBy(_ => _random.Next()) : files.Order(Comparer<PhotoFile>.Create(Compare));

    private int Compare(PhotoFile a, PhotoFile b)
    {
        var byDate = _order == ByDate ? a.Modified.CompareTo(b.Modified) : 0;
        return byDate != 0 ? byDate : Names.Compare(a.Path, b.Path);
    }

    /// <summary>
    /// The pictures in <paramref name="folder"/>, and with <paramref name="subfolders"/> the folders in it, or null if it
    /// isn't there or can't be read. Hidden and system files (such as thumbnail caches) are left out, and folders that
    /// can't be opened are passed over.
    /// </summary>
    public static IReadOnlyList<PhotoFile>? List(string folder, bool subfolders)
    {
        try
        {
            var directory = new DirectoryInfo(folder);
            if (!directory.Exists)
                return null;
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = subfolders,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            };
            return
            [
                .. directory.EnumerateFiles("*", options)
                    .Where(file => BackgroundImage.IsSupported(file.Name))
                    .Take(MostPhotos)
                    .Select(file => new PhotoFile(file.FullName, file.LastWriteTimeUtc)),
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>
    /// Decodes a photo no bigger than a <paramref name="width"/> × <paramref name="height"/> box needs for
    /// <paramref name="fit"/> (as <see cref="BackgroundImage"/> does), turned the way the camera says is up. Null if it
    /// can't be read. The picture is frozen, so this can run off the UI thread.
    /// </summary>
    public static Photo? Decode(string path, string fit, double width, double height)
    {
        try
        {
            // Without caching, WIC pulls the pixels through the scaler and crop as they're needed, so the full-size photo
            // is never held in memory, only the result.
            using var stream = File.OpenRead(path);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0];
            var (orientation, taken) = ReadMetadata(frame);

            // A photo taken sideways is stored on its side, so it's sized and cropped that way, then turned.
            var (boxWidth, boxHeight) = orientation >= 5 ? (height, width) : (width, height);
            BitmapSource source = frame;
            var (scaleX, scaleY) = BackgroundImage.DecodeScale(fit, frame.PixelWidth, frame.PixelHeight, boxWidth, boxHeight);
            if (scaleX < 1 || scaleY < 1)
                source = new TransformedBitmap(source, new ScaleTransform(scaleX, scaleY));
            if (BackgroundImage.VisibleCrop(fit, source.PixelWidth, source.PixelHeight, boxWidth, boxHeight) is { } crop)
                source = new CroppedBitmap(source, crop);
            if (Turn(orientation) is { } turn)
                source = new TransformedBitmap(source, turn);

            var picture = new CachedBitmap(source, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            picture.Freeze();
            return new Photo(picture, taken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException
            or InvalidOperationException or FileFormatException or ExternalException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>The EXIF orientation (1 is upright) and when the photo was taken, as far as the file says.</summary>
    private static (int Orientation, DateTime? Taken) ReadMetadata(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is not BitmapMetadata metadata)
                return (1, null);
            var orientation = metadata.GetQuery("System.Photo.Orientation") is ushort o and >= 1 and <= 8 ? o : 1;
            DateTime? taken = metadata.GetQuery("System.Photo.DateTaken") is FileTime time
                ? DateTime.FromFileTime(((long)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime)
                : null;
            return (orientation, taken);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException or ExternalException)
        {
            // Some formats (BMP) have no metadata to ask about.
            return (1, null);
        }
    }

    /// <summary>What turns a photo stored with EXIF orientation <paramref name="orientation"/> the right way up; null if it already is.</summary>
    private static Transform? Turn(int orientation) => orientation switch
    {
        2 => new ScaleTransform(-1, 1),
        3 => new RotateTransform(180),
        4 => new ScaleTransform(1, -1),
        5 => Both(new RotateTransform(90), new ScaleTransform(-1, 1)),
        6 => new RotateTransform(90),
        7 => Both(new RotateTransform(270), new ScaleTransform(-1, 1)),
        8 => new RotateTransform(270),
        _ => null,
    };

    private static TransformGroup Both(Transform first, Transform then)
    {
        var group = new TransformGroup();
        group.Children.Add(first);
        group.Children.Add(then);
        return group;
    }
}
