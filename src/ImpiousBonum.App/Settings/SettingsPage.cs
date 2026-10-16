namespace ImpiousBonum.App.Settings;

/// <summary>The main window's pages, in the sidebar's order.</summary>
public enum SettingsPage
{
    Layouts,
    Gallery,
    Screens,
    TabletAndUsb,
    PagesAndSchedule,
    GameLayouts,
    Sensors,
    Energy,
    Weather,
    Calendars,
    NewsFeeds,
    ClaudeUsage,
    CustomData,
    StatusChecks,
    Alerts,
    Hotkeys,
    Logging,
    General,
    About,
}

/// <summary>A page of the main window.</summary>
internal interface IPage
{
    /// <summary>Shown, or shown again: read the settings afresh, since they may have changed elsewhere.</summary>
    void Load();

    /// <summary>Each second while shown, for statuses that change by themselves.</summary>
    void Refresh()
    {
    }

    /// <summary>About to be left. False keeps the page (the user wants to carry on with it).</summary>
    bool CanLeave() => true;

    /// <summary>Left, for another page or because the window closed.</summary>
    void Left()
    {
    }
}

/// <summary>A sidebar entry: the page, its icon (Segoe Fluent Icons) and its name.</summary>
internal sealed record PageInfo(SettingsPage Page, string Glyph, string Title, bool StartsGroup = false)
{
    public static readonly IReadOnlyList<PageInfo> All =
    [
        new(SettingsPage.Layouts, "", "Layouts"),
        new(SettingsPage.Gallery, "", "Gallery"),
        new(SettingsPage.Screens, "", "Screens"),
        new(SettingsPage.TabletAndUsb, "", "Tablet and USB screen"),
        new(SettingsPage.PagesAndSchedule, "", "Pages and schedule"),
        new(SettingsPage.GameLayouts, "", "Game layouts"),
        new(SettingsPage.Sensors, "", "Sensors", StartsGroup: true),
        new(SettingsPage.Energy, "", "Energy"),
        new(SettingsPage.Weather, "", "Weather"),
        new(SettingsPage.Calendars, "", "Calendars"),
        new(SettingsPage.NewsFeeds, "", "News feeds"),
        new(SettingsPage.ClaudeUsage, "", "Claude usage"),
        new(SettingsPage.CustomData, "", "Custom data"),
        new(SettingsPage.StatusChecks, "", "Status checks"),
        new(SettingsPage.Alerts, "", "Alerts", StartsGroup: true),
        new(SettingsPage.Hotkeys, "", "Hotkeys"),
        new(SettingsPage.Logging, "", "Logging"),
        new(SettingsPage.General, "", "General", StartsGroup: true),
        new(SettingsPage.About, "", "About and updates"),
    ];

    public static PageInfo Of(SettingsPage page) => All.First(p => p.Page == page);
}
