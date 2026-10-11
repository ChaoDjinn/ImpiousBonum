using System.Windows;

namespace ImpiousBonum.App.Shell;

/// <summary>
/// Layouts as pages: swipe across the dashboard to move between them, or let them rotate on a timer.
/// The pages are the layouts you picked (tray → Layout → Pages), in the Layout menu's order, or all of them.
/// </summary>
public static class Pages
{
    /// <summary>How long after you touch the dashboard rotation waits before carrying on.</summary>
    public static readonly TimeSpan RotationPauseAfterTouch = TimeSpan.FromMinutes(1);

    /// <summary>The pages, in <paramref name="layouts"/>' order: those in <paramref name="chosen"/> that still exist, or every layout if none do.</summary>
    public static IReadOnlyList<string> Of(IReadOnlyList<string> layouts, IReadOnlyList<string> chosen)
    {
        var picked = layouts.Where(l => chosen.Contains(l, StringComparer.OrdinalIgnoreCase)).ToList();
        return picked.Count > 0 ? picked : layouts;
    }

    /// <summary>
    /// The page <paramref name="step"/> away from <paramref name="current"/>, wrapping round; the first page when the
    /// current layout isn't one of them. Null with fewer than two pages.
    /// </summary>
    public static string? Next(IReadOnlyList<string> pages, string current, int step)
    {
        if (pages.Count < 2)
            return null;
        var index = pages.ToList().FindIndex(p => string.Equals(p, current, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return pages[0];
        return pages[((index + step) % pages.Count + pages.Count) % pages.Count];
    }

    /// <summary>
    /// Whether a press that moved by <paramref name="delta"/> on a dashboard <paramref name="width"/> wide is a swipe:
    /// +1 for the next page (swiping left), -1 for the previous (swiping right), or null. It has to travel at least an
    /// eighth of the width, mostly sideways.
    /// </summary>
    public static int? Swipe(Vector delta, double width)
    {
        var distance = Math.Max(60, width / 8);
        if (Math.Abs(delta.X) < distance || Math.Abs(delta.X) < 2 * Math.Abs(delta.Y))
            return null;
        return delta.X < 0 ? 1 : -1;
    }
}
