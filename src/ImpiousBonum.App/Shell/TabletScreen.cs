using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ImpiousBonum.App.Layout;

namespace ImpiousBonum.App.Shell;

/// <summary>
/// A tablet's screen (the Screens page): not a monitor, just a name and a size to plan a layout at, and the saved layout
/// its tablet link (<c>&amp;screen=</c>) shows in the tablet view. Picked in the editor like a monitor's dashboard.
/// </summary>
/// <param name="Name">What it's called on the Screens page, in the editor and in its link.</param>
/// <param name="Width">Its width in pixels, the way up it's held.</param>
/// <param name="Height">Its height in pixels.</param>
/// <param name="Layout">The saved layout it shows.</param>
public sealed record TabletScreen(string Name, int Width, int Height, string Layout)
{
    public const int MaxNameLength = 40;

    /// <summary>The smallest and largest sides it can have, the same as a layout's canvas.</summary>
    public const int MinSide = 50;

    public const int MaxSide = 10_000;

    /// <summary>Starts every tablet screen's <see cref="Id"/>. Monitors' ids are device paths, which never start this way.</summary>
    public const string IdPrefix = "tablet:";

    /// <summary>
    /// Common tablets' screens, held landscape, in physical pixels. Android tablets show web pages at a fraction of
    /// these, but the tablet view's pictures are drawn at the layout's size and look sharpest at the screen's own.
    /// </summary>
    public static readonly IReadOnlyList<(string Label, int Width, int Height)> Presets =
    [
        ("Samsung Galaxy Tab S7, S8 or S9", 2560, 1600),
        ("Samsung Galaxy Tab S8+ or S9+", 2800, 1752),
        ("Samsung Galaxy Tab S9 FE", 2304, 1440),
        ("Samsung Galaxy Tab A8 or A9+", 1920, 1200),
        ("Google Pixel Tablet", 2560, 1600),
        ("Lenovo Tab P11 or M10 Plus", 2000, 1200),
        ("Xiaomi Pad 6", 2880, 1800),
        ("OnePlus Pad", 2800, 2000),
        ("Amazon Fire HD 10", 1920, 1200),
        ("Amazon Fire HD 8", 1280, 800),
        ("Apple iPad or iPad Air (10.9\" and 11\")", 2360, 1640),
        ("Apple iPad mini", 2266, 1488),
    ];

    /// <summary>How the editor and the Screens page tell it from monitors' dashboards and other tablet screens.</summary>
    [JsonIgnore]
    public string Id => IdPrefix + Name;

    [JsonIgnore]
    public bool IsPortrait => Height > Width;

    /// <summary>"Galaxy Tab · 2560×1600 (tablet)" for the editor's list of screens.</summary>
    [JsonIgnore]
    public string DisplayName => $"{Name} · {Width}×{Height} (tablet)";

    /// <summary>The tablet screen called <paramref name="name"/>, ignoring case, or null.</summary>
    public static TabletScreen? Find(IEnumerable<TabletScreen> screens, string name) =>
        screens.FirstOrDefault(s => string.Equals(s.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>What's wrong with a new tablet screen's name and size, or null if nothing is.</summary>
    /// <param name="others">The tablet screens there are already, which it can't share a name with.</param>
    public static string? Problem(string name, int? width, int? height, IEnumerable<TabletScreen> others)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            return "Give it a name, such as the tablet's.";
        if (trimmed.Length > MaxNameLength)
            return $"Keep the name to {MaxNameLength} characters.";
        if (trimmed.Any(char.IsControl))
            return "The name can't contain control characters.";
        if (Find(others, trimmed) is not null)
            return $"There's already a tablet screen called \"{trimmed}\".";
        if (width is not { } w || height is not { } h)
            return "Write the width and height in pixels, like 2560 and 1600.";
        if (w < MinSide || h < MinSide || w > MaxSide || h > MaxSide)
            return $"The width and height can each be from {MinSide} to {MaxSide} pixels.";
        return null;
    }

    /// <summary>"Tablet", or "Tablet 2" and so on when there's one of that name already: a new tablet screen's name to start with.</summary>
    public static string NewName(IEnumerable<TabletScreen> screens)
    {
        var list = screens.ToList();
        var candidate = "Tablet";
        for (var i = 2; Find(list, candidate) is not null; i++)
            candidate = $"Tablet {i}";
        return candidate;
    }

    /// <summary>
    /// The name of a new tablet screen's layout: the screen's own, or "Tablet" when that won't do as a file name,
    /// numbered if a layout already has it.
    /// </summary>
    public static string NewLayoutName(string name, IReadOnlyList<string> layouts)
    {
        var trimmed = name.Trim();
        var start = LayoutLibrary.ValidateName(trimmed) is null ? trimmed : "Tablet";
        var candidate = start;
        for (var i = 2; layouts.Contains(candidate, StringComparer.OrdinalIgnoreCase); i++)
            candidate = $"{start} {i}";
        return candidate;
    }

    /// <summary>
    /// A new tablet screen's layout: empty, the screen's size so it's planned at that and drawn sharp on the tablet, and in
    /// <paramref name="theme"/> (the main dashboard's) so the two look alike.
    /// </summary>
    public LayoutDocument NewLayout(JsonObject theme) => new()
    {
        Width = Width,
        Height = Height,
        Theme = (JsonObject)theme.DeepClone(),
    };
}
