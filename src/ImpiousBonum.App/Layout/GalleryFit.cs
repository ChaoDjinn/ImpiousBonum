namespace ImpiousBonum.App.Layout;

/// <summary>
/// Which gallery layouts suit a screen. A layout is scaled to fill its dashboard but keeps its shape, so one of a
/// different shape leaves empty bands either side; its size only says how big things are drawn. Screens differ in a way
/// pixels don't show, too: a 50" TV across the room and a 5" panel on the desk both suit a few big widgets, where a
/// monitor at arm's length can show lots, so layouts are also told apart by how much they show.
/// </summary>
public static class GalleryFit
{
    /// <summary>How far a layout's shape can be from the screen's (wider or narrower) and still suit it.</summary>
    public const double ShapeTolerance = 0.12;

    /// <summary>Layouts with up to this many widgets are "a few big things"; more is "lots at once".</summary>
    public const int FewWidgets = 6;

    /// <summary>Widgets that only decorate: they don't count towards how much a layout shows.</summary>
    public static readonly IReadOnlySet<string> Decorations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "shape", "image" };

    /// <summary>True if the layout's shape is close enough to a <paramref name="width"/>×<paramref name="height"/> screen's.</summary>
    public static bool Suits(GalleryEntry entry, int width, int height) =>
        entry.Width > 0 && entry.Height > 0 && width > 0 && height > 0
        && Math.Abs(Math.Log((double)entry.Width / entry.Height * height / width)) <= Math.Log(1 + ShapeTolerance);

    /// <summary>How far the layout's size is from the screen's, for putting the closest first: 0 is the same, 1 twice or half.</summary>
    public static double SizeDistance(GalleryEntry entry, int width, int height) =>
        entry.Height > 0 && height > 0 ? Math.Abs(Math.Log2((double)entry.Height / height)) : double.MaxValue;

    /// <summary>The layouts for a screen (all, when null), filtered by how much they show and closest in size first.</summary>
    public static IReadOnlyList<GalleryEntry> For(IEnumerable<GalleryEntry> entries, (int Width, int Height)? screen, GalleryDetail detail)
    {
        var shown = entries.Where(e => Shows(e, detail));
        return screen is { } s
            ? shown.Where(e => Suits(e, s.Width, s.Height)).OrderBy(e => SizeDistance(e, s.Width, s.Height)).ToList()
            : shown.ToList();
    }

    /// <summary>Layouts whose widget count isn't given show whatever the filter.</summary>
    public static bool Shows(GalleryEntry entry, GalleryDetail detail) => detail switch
    {
        GalleryDetail.Few => entry.Widgets is 0 or <= FewWidgets,
        GalleryDetail.Lots => entry.Widgets is 0 or > FewWidgets,
        _ => true,
    };
}

/// <summary>How much a layout shows at once.</summary>
public enum GalleryDetail
{
    Any,
    /// <summary>A few big things, for a small screen or one across the room.</summary>
    Few,
    /// <summary>Lots at once, for a big screen close by.</summary>
    Lots,
}
