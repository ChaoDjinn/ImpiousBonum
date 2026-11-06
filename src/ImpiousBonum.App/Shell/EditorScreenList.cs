namespace ImpiousBonum.App.Shell;

/// <summary>
/// The dashboards the layout editor can be on, in the order its Screen box lists them: the main one, then the other
/// monitors' dashboards, then the tablet screens. Each but the main one is known by an id: its monitor's
/// (<see cref="ExtraDashboard.MonitorId"/>) or the tablet screen's (<see cref="TabletScreen.Id"/>).
/// </summary>
public static class EditorScreenList
{
    /// <summary>Where the screen <paramref name="screen"/> is in the list: 0, the main dashboard, for null or one that isn't there.</summary>
    public static int IndexOf(string? screen, IReadOnlyList<ExtraDashboard> extras, IReadOnlyList<TabletScreen> tablets)
    {
        if (screen is null)
            return 0;
        for (var i = 0; i < extras.Count; i++)
        {
            if (extras[i].MonitorId == screen)
                return i + 1;
        }
        for (var i = 0; i < tablets.Count; i++)
        {
            if (tablets[i].Id == screen)
                return extras.Count + 1 + i;
        }
        return 0;
    }

    /// <summary>The id of the screen at <paramref name="index"/> in the list, or null for the main dashboard (or a place past the end).</summary>
    public static string? At(int index, IReadOnlyList<ExtraDashboard> extras, IReadOnlyList<TabletScreen> tablets)
    {
        if (index > 0 && index <= extras.Count)
            return extras[index - 1].MonitorId;
        var tablet = index - extras.Count - 1;
        return tablet >= 0 && tablet < tablets.Count ? tablets[tablet].Id : null;
    }
}
