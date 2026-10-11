using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Shell;

/// <summary>The notification-area icon, its notifications and its menu: the layout and its screens, data sources, alerts and the like, updates, help and exit.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _image;
    private readonly ToolStripMenuItem _displays = new("Display");
    private readonly ToolStripMenuItem _tablet = new("Tablet view");
    private readonly ToolStripMenuItem _away = new("When you're away") { ToolTipText = "Dim the dashboard when there's been no keyboard, mouse or touch input for a while" };
    private readonly ToolStripMenuItem _tabletOn = new("Show on tablets") { ToolTipText = "Serve the dashboard to browsers on your local network" };
    private readonly ToolStripMenuItem _tabletButtons = new("Let tablets press buttons")
    {
        ToolTipText = "Taps on the tablet press the layout's buttons and media controls. Anyone with the link could press them.",
    };
    private readonly ToolStripMenuItem _tabletShowLink = new("Show link…");
    private readonly ToolStripMenuItem _tabletCopyLink = new("Copy link");
    private readonly ToolStripMenuItem _tabletNewLink = new("New link") { ToolTipText = "Stops old links working" };
    private readonly ToolStripMenuItem _layouts = new("Layout");
    private readonly ToolStripMenuItem _gameLayouts = new("Game layouts");
    private readonly ToolStripMenuItem _pages = new("Pages") { ToolTipText = "Swipe across the dashboard to move between layouts, or rotate them" };
    private readonly ToolStripMenuItem _schedule = new("Schedule") { ToolTipText = "Show a layout or theme at night, or with Windows in dark or light mode" };
    private readonly ToolStripMenuItem _fpsSource = new("FPS from");
    private readonly ToolStripMenuItem _startup = new("Start with Windows") { CheckOnClick = true };
    private readonly ToolStripMenuItem _sensors = new("Sensors");
    private readonly ToolStripMenuItem _sensorStatus = new() { Enabled = false };
    private readonly ToolStripMenuItem _sensorInstall = new();
    private readonly ToolStripMenuItem _sensorRemove = new("Remove sensor service…");
    private readonly ToolStripMenuItem _claude = new("Claude usage");
    private readonly ToolStripMenuItem _claudeStatus = new() { Enabled = false };
    private readonly ToolStripMenuItem _claudeHint = new("Needs Claude Code in a terminal: the Desktop app doesn't share usage") { Enabled = false };
    private readonly ToolStripMenuItem _claudeConnect = new() { ToolTipText = "Set Claude Code's status line to send your plan usage to the dashboard" };
    private readonly ToolStripMenuItem _weather = new("Weather");
    private readonly ToolStripMenuItem _weatherStatus = new() { Enabled = false };
    private readonly ToolStripMenuItem _weatherSet = new("Set location…") { ToolTipText = "Where the weather widget's forecast is for" };
    private readonly ToolStripMenuItem _weatherUnits = new("Units");
    private readonly ToolStripMenuItem _weatherOff = new("Turn off weather") { ToolTipText = "Forget the location and stop fetching forecasts" };
    private readonly ToolStripMenuItem _header = new("Impious Bonum") { Enabled = false, Tag = TrayMenuStyle.HeaderTag.Instance };
    private readonly ToolStripMenuItem _checkUpdates = new("Check for updates") { Visible = false };
    private readonly ToolStripMenuItem _restartToUpdate = new() { Visible = false };
    private readonly ToolStripMenuItem _help = new("Help");
    private readonly Font _bold;
    private string? _version;

    /// <summary>What clicking the notification showing now does, if anything.</summary>
    private Action? _onNotificationClicked;

    public TrayIcon()
    {
        _image = LoadIcon();

        // Grouped by what they're for: the layout and where it shows, where its readings come from, what watches the
        // readings, then the app itself. The bold item is what double-clicking the icon does.
        var menu = new ContextMenuStrip();
        _bold = new Font(menu.Font, FontStyle.Bold);
        _header.Font = _bold;
        _header.Image = AppIconBitmap(menu.DeviceDpi);
        menu.Items.Add(_header);
        menu.Items.Add(_restartToUpdate.WithIcon(TrayMenuStyle.Icons.Restart));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Edit layout…", null, (_, _) => EditLayoutRequested?.Invoke(this, EventArgs.Empty)) { Font = _bold }
            .WithIcon(TrayMenuStyle.Icons.Edit));
        menu.Items.Add(_layouts.WithIcon(TrayMenuStyle.Icons.Layout));
        menu.Items.Add(_displays.WithIcon(TrayMenuStyle.Icons.Display));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_sensors.WithIcon(TrayMenuStyle.Icons.Sensors));
        menu.Items.Add(_weather.WithIcon(TrayMenuStyle.Icons.Weather));
        menu.Items.Add(Item("Calendar…", "Calendars whose events the agenda widget shows", () => CalendarRequested?.Invoke(this, EventArgs.Empty))
            .WithIcon(TrayMenuStyle.Icons.Calendar));
        menu.Items.Add(Item("News feeds…", "News feeds whose headlines the news widget shows", () => NewsRequested?.Invoke(this, EventArgs.Empty))
            .WithIcon(TrayMenuStyle.Icons.News));
        menu.Items.Add(_claude.WithIcon(TrayMenuStyle.Icons.Chat));
        menu.Items.Add(Item("Custom data…", "Your own metrics from web addresses and commands", () => CustomDataRequested?.Invoke(this, EventArgs.Empty))
            .WithIcon(TrayMenuStyle.Icons.Globe));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Alerts…", "Be told when a reading is out of bounds for a while", () => AlertsRequested?.Invoke(this, EventArgs.Empty))
            .WithIcon(TrayMenuStyle.Icons.Bell));
        menu.Items.Add(Item("Hotkeys…", "Keys that show or hide the dashboard, turn pages or switch layouts from anywhere", () => HotkeysRequested?.Invoke(this, EventArgs.Empty))
            .WithIcon(TrayMenuStyle.Icons.Keyboard));
        menu.Items.Add(Item("Logging…", "Log readings to CSV files, with each one's average, lowest and highest", () => LoggingRequested?.Invoke(this, EventArgs.Empty))
            .WithIcon(TrayMenuStyle.Icons.History));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_startup);
        menu.Items.Add(_checkUpdates.WithIcon(TrayMenuStyle.Icons.Sync));
        menu.Items.Add(_help.WithIcon(TrayMenuStyle.Icons.Help));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)).WithIcon(TrayMenuStyle.Icons.Power));

        _help.DropDownItems.Add(Item("User guide", "How to use everything, on GitHub", () => OpenLink(Links.Guide(_version))));
        _help.DropDownItems.Add(Item("Widget reference", "Every widget's settings, on GitHub", () => OpenLink(Links.Widgets(_version))));
        _help.DropDownItems.Add(Item("What's new", "The changelog, on GitHub", () => OpenLink(Links.Changelog(_version))));
        _help.DropDownItems.Add(Item("Report a problem…", "Open an issue on GitHub. Attaching dashboard.log from the settings folder helps.", () => OpenLink(Links.NewIssue)));
        _help.DropDownItems.Add(new ToolStripSeparator());
        _help.DropDownItems.Add(Item("Open settings folder", "Settings, layouts, themes and dashboard.log", () => Open(AppPaths.DataDirectory)));
        _help.DropDownItems.Add(new ToolStripSeparator());
        _help.DropDownItems.Add(Item("About Impious Bonum", null, () => AboutRequested?.Invoke(this, EventArgs.Empty)));

        _sensors.DropDownItems.Add(_sensorStatus);
        _sensors.DropDownItems.Add(_sensorInstall);
        _sensors.DropDownItems.Add(_sensorRemove);
        _sensors.DropDownItems.Add(new ToolStripSeparator());
        _sensors.DropDownItems.Add(_fpsSource);
        // Installing over an existing service replaces it, so "Update" is the same operation.
        _sensorInstall.Click += (_, _) => SensorServiceChangeRequested?.Invoke(this, true);
        _sensorRemove.Click += (_, _) => SensorServiceChangeRequested?.Invoke(this, false);

        _claude.DropDownItems.Add(_claudeStatus);
        _claude.DropDownItems.Add(_claudeHint);
        _claude.DropDownItems.Add(_claudeConnect);
        _claudeConnect.Click += (_, _) => ConnectClaudeCodeRequested?.Invoke(this, EventArgs.Empty);

        _weather.DropDownItems.AddRange([_weatherStatus, _weatherSet, _weatherUnits, _weatherOff]);
        _weatherSet.Click += (_, _) => WeatherLocationRequested?.Invoke(this, EventArgs.Empty);
        _weatherOff.Click += (_, _) => WeatherTurnedOff?.Invoke(this, EventArgs.Empty);
        foreach (var (units, label) in new[] { (AppSettings.WeatherUnitsAuto, "Automatic (your region)"), (AppSettings.WeatherUnitsMetric, "°C and km/h"), (AppSettings.WeatherUnitsImperial, "°F and mph") })
        {
            var item = new ToolStripMenuItem(label) { Tag = units };
            item.Click += (_, _) => WeatherUnitsSelected?.Invoke(this, units);
            _weatherUnits.DropDownItems.Add(item);
        }

        _tablet.DropDownItems.AddRange([_tabletOn, _tabletButtons, new ToolStripSeparator(), _tabletShowLink, _tabletCopyLink, _tabletNewLink]);
        _tabletOn.Click += (_, _) => TabletViewToggled?.Invoke(this, !_tabletOn.Checked);
        _tabletButtons.Click += (_, _) => TabletButtonsToggled?.Invoke(this, !_tabletButtons.Checked);
        _tabletShowLink.Click += (_, _) => TabletLinkRequested?.Invoke(this, EventArgs.Empty);
        _tabletCopyLink.Click += (_, _) => TabletLinkCopyRequested?.Invoke(this, EventArgs.Empty);
        _tabletNewLink.Click += (_, _) => TabletNewLinkRequested?.Invoke(this, EventArgs.Empty);

        _restartToUpdate.Font = _bold;
        _restartToUpdate.Click += (_, _) => RestartToUpdateRequested?.Invoke(this, EventArgs.Empty);
        _checkUpdates.Click += (_, _) => CheckForUpdatesRequested?.Invoke(this, EventArgs.Empty);

        _startup.Checked = StartupRegistration.IsEnabled;
        _startup.CheckedChanged += (_, _) => StartupRegistration.SetEnabled(_startup.Checked);
        menu.Opening += (_, _) =>
        {
            _startup.Checked = StartupRegistration.IsEnabled;
            MenuOpening?.Invoke(this, EventArgs.Empty);
        };

        // Dark or light with the taskbar, rounded, with icons: Windows 11's look rather than Windows Forms' default.
        TrayMenuStyle.Attach(menu);

        _icon = new NotifyIcon { Icon = _image, Text = "Impious Bonum", ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => EditLayoutRequested?.Invoke(this, EventArgs.Empty);
        _icon.BalloonTipClicked += (_, _) => _onNotificationClicked?.Invoke();
        _icon.BalloonTipClosed += (_, _) => _onNotificationClicked = null;
    }

    private static ToolStripMenuItem Item(string text, string? tip, Action click) =>
        new(text, null, (_, _) => click()) { ToolTipText = tip };

    /// <summary>Help → About Impious Bonum.</summary>
    public event EventHandler? AboutRequested;

    public event EventHandler? MenuOpening;

    public event EventHandler? ReloadRequested;

    /// <summary>Open the layout editor (menu, or double-click the icon).</summary>
    public event EventHandler? EditLayoutRequested;

    public event EventHandler? ExitRequested;

    public event EventHandler<DisplayMonitor>? MonitorSelected;

    public event EventHandler? CheckForUpdatesRequested;

    public event EventHandler? RestartToUpdateRequested;

    /// <summary>Shows the version in the menu header and, for installed copies, the update items.</summary>
    public void SetVersion(string? version, bool updatesAvailable)
    {
        _version = version;
        // The name, with the version on the right in the secondary colour.
        _header.ShortcutKeyDisplayString = version ?? "development build";
        _checkUpdates.Visible = updatesAvailable;
    }

    /// <summary>A downloaded update is waiting; offer to restart into it.</summary>
    public void SetUpdateReady(string? version)
    {
        _restartToUpdate.Visible = version is not null;
        _restartToUpdate.Text = $"Restart to update to {version}";
    }

    /// <summary>A monitor id, or null for "whichever app is in the foreground".</summary>
    public event EventHandler<string?>? FpsSourceSelected;

    public void SetFpsSources(IReadOnlyList<DisplayMonitor> monitors, string? currentMonitorId)
    {
        _fpsSource.DropDownItems.Clear();
        var foreground = new ToolStripMenuItem("Foreground app") { Checked = currentMonitorId is null };
        foreground.Click += (_, _) => FpsSourceSelected?.Invoke(this, null);
        _fpsSource.DropDownItems.Add(foreground);
        _fpsSource.DropDownItems.Add(new ToolStripSeparator());

        foreach (var monitor in monitors)
        {
            var item = new ToolStripMenuItem($"Top app on {monitor.DisplayName}") { Checked = monitor.Id == currentMonitorId };
            item.Click += (_, _) => FpsSourceSelected?.Invoke(this, monitor.Id);
            _fpsSource.DropDownItems.Add(item);
        }
    }

    /// <summary>True to install the sensor service, false to remove it.</summary>
    public event EventHandler<bool>? SensorServiceChangeRequested;

    public void SetSensorState(string status, bool serviceInstalled, bool outdated)
    {
        _sensorStatus.Text = outdated ? "Sensor service is from an older version" : status;
        _sensorInstall.Text = serviceInstalled ? "Update sensor service…" : "Install sensor service…";
        _sensorInstall.Font = outdated ? _bold : null;
        _sensorRemove.Visible = serviceInstalled;
    }

    /// <summary>Tray → Claude usage → Connect Claude Code.</summary>
    public event EventHandler? ConnectClaudeCodeRequested;

    /// <param name="status">What the dashboard last heard from Claude Code.</param>
    /// <param name="connected">Claude Code's status line already points at Impious Bonum.</param>
    public void SetClaudeState(string status, bool connected)
    {
        _claudeStatus.Text = status;
        _claudeHint.Visible = status != ClaudeUsageProvider.Connected;
        _claudeConnect.Text = connected ? "Reconnect Claude Code" : "Connect Claude Code…";
    }

    public event EventHandler? WeatherLocationRequested;

    public event EventHandler? CustomDataRequested;

    public event EventHandler? AlertsRequested;

    public event EventHandler? HotkeysRequested;

    public event EventHandler? CalendarRequested;

    public event EventHandler? NewsRequested;

    public event EventHandler? LoggingRequested;

    public event EventHandler? WeatherTurnedOff;

    /// <summary>One of <see cref="AppSettings.WeatherUnitsAuto"/>, <see cref="AppSettings.WeatherUnitsMetric"/> or <see cref="AppSettings.WeatherUnitsImperial"/>.</summary>
    public event EventHandler<string>? WeatherUnitsSelected;

    public void SetWeatherState(string status, bool hasLocation, string units)
    {
        _weatherStatus.Text = status;
        _weatherSet.Text = hasLocation ? "Change location…" : "Set location…";
        _weatherOff.Visible = hasLocation;
        foreach (ToolStripMenuItem item in _weatherUnits.DropDownItems)
            item.Checked = string.Equals((string?)item.Tag, units, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A saved layout's name.</summary>
    public event EventHandler<string>? LayoutSelected;

    public void SetLayouts(IReadOnlyList<string> names, string active, string activePath)
    {
        _layouts.DropDownItems.Clear();
        foreach (var name in names)
        {
            var item = new ToolStripMenuItem(Escape(name)) { Checked = name == active };
            item.Click += (_, _) => LayoutSelected?.Invoke(this, name);
            _layouts.DropDownItems.Add(item);
        }
        _layouts.DropDownItems.Add(new ToolStripSeparator());
        _layouts.DropDownItems.Add("Import…", null, (_, _) => ImportLayoutRequested?.Invoke(this, EventArgs.Empty));
        _layouts.DropDownItems.Add(new ToolStripMenuItem("Gallery…", null, (_, _) => GalleryRequested?.Invoke(this, EventArgs.Empty))
        {
            ToolTipText = "Browse layouts other people have shared, and import one",
        });
        _layouts.DropDownItems.Add(_pages);
        _layouts.DropDownItems.Add(_gameLayouts);
        _layouts.DropDownItems.Add(_schedule);
        _layouts.DropDownItems.Add(new ToolStripSeparator());
        _layouts.DropDownItems.Add(Item("Open layout file", "The shown layout's JSON file, to edit by hand", () => Open(activePath)));
        _layouts.DropDownItems.Add(Item("Reload", "Read the shown layout's file again", () => ReloadRequested?.Invoke(this, EventArgs.Empty)));
    }

    /// <summary>Tray → Layout → Import…</summary>
    public event EventHandler? ImportLayoutRequested;

    public event EventHandler? GalleryRequested;

    /// <summary>Tray → Layout → Game layouts → Switch automatically.</summary>
    public event EventHandler<bool>? GameLayoutsToggled;

    /// <summary>Game layouts → Recap each session, with the new state.</summary>
    public event EventHandler<bool>? SessionRecapsToggled;

    /// <summary>Link a process name to a layout or a theme.</summary>
    public event EventHandler<GameLayoutRule>? GameLinkRequested;

    public event EventHandler<GameLayoutRule>? GameLinkRemoved;

    /// <summary>Tray → Layout → Schedule → Follow the schedule, with the new state.</summary>
    public event EventHandler<bool>? ScheduleToggled;

    public event EventHandler? ScheduleAddRequested;

    public event EventHandler<ScheduleRule>? ScheduleRuleRemoved;

    /// <summary>Tray → Layout → Pages: (swipe, rotate seconds, the layouts that are pages or empty for all).</summary>
    public event EventHandler<(bool Swipe, int RotateSeconds, IReadOnlyList<string> Pages)>? PagesChanged;

    public void SetPages(bool swipe, int rotateSeconds, IReadOnlyList<string> chosen, IReadOnlyList<string> layouts)
    {
        _pages.DropDownItems.Clear();
        var swipeItem = new ToolStripMenuItem("Swipe between pages") { Checked = swipe };
        swipeItem.Click += (_, _) => PagesChanged?.Invoke(this, (!swipe, rotateSeconds, chosen));
        _pages.DropDownItems.Add(swipeItem);

        var rotate = new ToolStripMenuItem("Rotate");
        foreach (var seconds in new[] { 0, 15, 30, 60, 300 })
        {
            var label = seconds switch { 0 => "Don't rotate", < 60 => $"Every {seconds} seconds", 60 => "Every minute", _ => $"Every {seconds / 60} minutes" };
            var item = new ToolStripMenuItem(label) { Checked = rotateSeconds == seconds };
            item.Click += (_, _) => PagesChanged?.Invoke(this, (swipe, seconds, chosen));
            rotate.DropDownItems.Add(item);
        }
        _pages.DropDownItems.Add(rotate);

        _pages.DropDownItems.Add(new ToolStripSeparator());
        _pages.DropDownItems.Add(new ToolStripMenuItem("The pages are:") { Enabled = false });
        var pages = Pages.Of(layouts, chosen);
        foreach (var layout in layouts)
        {
            var included = pages.Contains(layout, StringComparer.OrdinalIgnoreCase);
            var item = new ToolStripMenuItem(Escape(layout)) { Checked = included };
            item.Click += (_, _) =>
            {
                var next = included ? pages.Where(p => !string.Equals(p, layout, StringComparison.OrdinalIgnoreCase)).ToList() : [.. pages, layout];
                // Every layout picked is the same as none: all of them.
                PagesChanged?.Invoke(this, (swipe, rotateSeconds, next.Count == layouts.Count ? [] : next));
            };
            _pages.DropDownItems.Add(item);
        }
    }

    /// <summary>Tray → Display → When you're away: (minutes, brightness), minutes 0 for never.</summary>
    public event EventHandler<(int Minutes, int Brightness)>? AwayChanged;

    public void SetAway(int minutes, int brightness)
    {
        _away.DropDownItems.Clear();
        foreach (var choice in new[] { 0, 5, 15, 30, 60 })
        {
            var item = new ToolStripMenuItem(choice == 0 ? "Don't dim" : $"Dim after {choice} minutes") { Checked = minutes == choice };
            item.Click += (_, _) => AwayChanged?.Invoke(this, (choice, brightness));
            _away.DropDownItems.Add(item);
        }
        _away.DropDownItems.Add(new ToolStripSeparator());
        foreach (var level in new[] { 40, 20, 0 })
        {
            var item = new ToolStripMenuItem(level == 0 ? "Turn it black" : $"Dim to {level}%") { Checked = brightness == level, Enabled = minutes > 0 };
            item.Click += (_, _) => AwayChanged?.Invoke(this, (minutes, level));
            _away.DropDownItems.Add(item);
        }
    }

    public void SetSchedule(bool enabled, IReadOnlyList<ScheduleRule> rules, IReadOnlyList<ScheduleRule> active)
    {
        _schedule.DropDownItems.Clear();
        var toggle = new ToolStripMenuItem("Follow the schedule") { Checked = enabled };
        toggle.Click += (_, _) => ScheduleToggled?.Invoke(this, !enabled);
        _schedule.DropDownItems.Add(toggle);
        _schedule.DropDownItems.Add("Add…", null, (_, _) => ScheduleAddRequested?.Invoke(this, EventArgs.Empty));
        if (rules.Count == 0)
            return;

        _schedule.DropDownItems.Add(new ToolStripSeparator());
        foreach (var rule in rules)
        {
            // Bold: the rules that apply right now.
            var item = new ToolStripMenuItem(Escape(rule.Describe()));
            if (enabled && active.Contains(rule))
                item.Font = _bold;
            item.DropDownItems.Add("Remove", null, (_, _) => ScheduleRuleRemoved?.Invoke(this, rule));
            _schedule.DropDownItems.Add(item);
        }
    }

    /// <summary>
    /// Fills the Game layouts submenu. <paramref name="recentApp"/> is the last app seen in front (the tray itself is in
    /// front while its menu is open) and is offered for linking to any of the saved layouts in <paramref name="names"/>,
    /// or to any of the <paramref name="themes"/> to restyle whichever layout is showing.
    /// </summary>
    public void SetGameLayouts(bool enabled, bool recaps, IReadOnlyList<GameLayoutRule> rules, string? recentApp, IReadOnlyList<string> names, IReadOnlyList<string> themes)
    {
        _gameLayouts.DropDownItems.Clear();
        var toggle = new ToolStripMenuItem("Switch automatically") { Checked = enabled };
        toggle.Click += (_, _) => GameLayoutsToggled?.Invoke(this, !enabled);
        _gameLayouts.DropDownItems.Add(toggle);
        var recap = new ToolStripMenuItem("Recap each session")
        {
            Checked = recaps,
            ToolTipText = "After playing a linked game: a notification with its FPS, loads and temperatures, and a line in sessions.csv",
        };
        recap.Click += (_, _) => SessionRecapsToggled?.Invoke(this, !recaps);
        _gameLayouts.DropDownItems.Add(recap);

        if (recentApp is { } app)
        {
            var linked = GameLayoutSwitcher.Match(rules, app);
            var link = new ToolStripMenuItem($"Link \"{Escape(app)}\" to");
            foreach (var name in names)
            {
                var item = new ToolStripMenuItem(Escape(name)) { Checked = linked?.Theme is null && string.Equals(name, linked?.Layout, StringComparison.OrdinalIgnoreCase) };
                item.Click += (_, _) => GameLinkRequested?.Invoke(this, new GameLayoutRule(app, name));
                link.DropDownItems.Add(item);
            }

            var themed = new ToolStripMenuItem("Theme only") { ToolTipText = "Keep the layout that's showing and restyle it with a theme" };
            foreach (var theme in themes)
            {
                var item = new ToolStripMenuItem(Escape(theme)) { Checked = linked?.Layout is null && string.Equals(theme, linked?.Theme, StringComparison.OrdinalIgnoreCase) };
                item.Click += (_, _) => GameLinkRequested?.Invoke(this, new GameLayoutRule(app, null, theme));
                themed.DropDownItems.Add(item);
            }
            link.DropDownItems.Add(new ToolStripSeparator());
            link.DropDownItems.Add(themed);
            _gameLayouts.DropDownItems.Add(link);
        }

        if (rules.Count == 0)
            return;
        _gameLayouts.DropDownItems.Add(new ToolStripSeparator());
        foreach (var rule in rules)
        {
            var item = new ToolStripMenuItem($"{Escape(rule.Process)} → {Escape(rule.Describe())}");
            item.DropDownItems.Add("Remove link", null, (_, _) => GameLinkRemoved?.Invoke(this, rule));
            _gameLayouts.DropDownItems.Add(item);
        }
    }

    // "&" would otherwise underline the next letter as a mnemonic.
    private static string Escape(string text) => text.Replace("&", "&&");

    /// <summary>Tray → Display: one of the window options changed.</summary>
    public event EventHandler<WindowOptions>? WindowOptionsChanged;

    /// <summary>Tray → Display → Reset size and position.</summary>
    public event EventHandler? ResetWindowRequested;

    /// <summary>Tray → Display → Bring to front.</summary>
    public event EventHandler? BringToFrontRequested;

    public void SetMonitors(IReadOnlyList<DisplayMonitor> monitors, DisplayMonitor? current, WindowOptions options)
    {
        _displays.DropDownItems.Clear();
        foreach (var monitor in monitors)
        {
            var item = new ToolStripMenuItem(monitor.DisplayName) { Checked = monitor.Id == current?.Id };
            item.Click += (_, _) => MonitorSelected?.Invoke(this, monitor);
            _displays.DropDownItems.Add(item);
        }

        _displays.DropDownItems.Add(new ToolStripSeparator());
        AddOption("Fill screen", !options.Windowed, true, options with { Windowed = false });
        AddOption("Windowed", options.Windowed, true, options with { Windowed = true });
        AddOption("Lock position", options.Locked, options.Windowed, options with { Locked = !options.Locked },
            "Unlocked, drag the dashboard to move it and drag its edges to resize it");
        AddOption("Click-through when locked", options.ClickThrough, options.Windowed && options.Locked, options with { ClickThrough = !options.ClickThrough },
            "Clicks go to the window underneath. Unlock it here to edit or move it again.");
        var reset = new ToolStripMenuItem("Reset size and position") { Enabled = options.Windowed };
        reset.Click += (_, _) => ResetWindowRequested?.Invoke(this, EventArgs.Empty);
        _displays.DropDownItems.Add(reset);

        _displays.DropDownItems.Add(new ToolStripSeparator());
        AddOption("Always on top", options.AlwaysOnTop, true, options with { AlwaysOnTop = !options.AlwaysOnTop });
        AddOption("Hide over fullscreen apps", options.HideOverFullscreen, options.AlwaysOnTop, options with { HideOverFullscreen = !options.HideOverFullscreen },
            "Steps aside while a game or video fills the dashboard's screen");
        _displays.DropDownItems.Add("Bring to front", null, (_, _) => BringToFrontRequested?.Invoke(this, EventArgs.Empty));

        _displays.DropDownItems.Add(new ToolStripSeparator());
        _displays.DropDownItems.Add(_extraDashboards);
        _displays.DropDownItems.Add(_away);
        _displays.DropDownItems.Add(_tablet);
        _displays.DropDownItems.Add(_usbScreen);
    }

    private readonly ToolStripMenuItem _extraDashboards = new("More dashboards") { ToolTipText = "Fill other screens with layouts of their own" };

    /// <summary>Display → More dashboards changed, with the new list.</summary>
    public event EventHandler<List<ExtraDashboard>>? ExtraDashboardsChanged;

    /// <param name="main">The main dashboard's monitor, which can't have another.</param>
    /// <param name="active">The main dashboard's layout; a new dashboard starts with another.</param>
    public void SetExtraDashboards(IReadOnlyList<ExtraDashboard> extras, IReadOnlyList<DisplayMonitor> monitors, DisplayMonitor? main, IReadOnlyList<string> layouts, string active)
    {
        _extraDashboards.DropDownItems.Clear();
        var free = ExtraDashboard.FreeMonitors(monitors, main, extras);
        for (var i = 0; i < extras.Count; i++)
        {
            var index = i;
            var extra = extras[i];
            var item = new ToolStripMenuItem(Escape($"{extra.Layout} on {extra.MonitorName(monitors)}"));

            var layout = new ToolStripMenuItem("Layout");
            foreach (var name in layouts)
            {
                var choice = new ToolStripMenuItem(Escape(name)) { Checked = string.Equals(name, extra.Layout, StringComparison.OrdinalIgnoreCase) };
                choice.Click += (_, _) => Change(list => list[index] = extra with { Layout = name });
                layout.DropDownItems.Add(choice);
            }

            var move = new ToolStripMenuItem("Move to") { Enabled = free.Count > 0 };
            foreach (var monitor in free)
                move.DropDownItems.Add(Escape(monitor.DisplayName), null, (_, _) => Change(list => list[index] = ExtraDashboard.On(monitor, extra.Layout)));

            var remove = new ToolStripMenuItem("Remove");
            remove.Click += (_, _) => Change(list => list.RemoveAt(index));
            item.DropDownItems.AddRange([layout, move, remove]);
            _extraDashboards.DropDownItems.Add(item);
        }

        if (extras.Count > 0)
            _extraDashboards.DropDownItems.Add(new ToolStripSeparator());
        var add = new ToolStripMenuItem("Add on") { Enabled = free.Count > 0, ToolTipText = free.Count > 0 ? null : "Every screen has a dashboard" };
        foreach (var monitor in free)
            add.DropDownItems.Add(Escape(monitor.DisplayName), null, (_, _) => Change(list => list.Add(ExtraDashboard.On(monitor, ExtraDashboard.FirstLayout(layouts, active)))));
        _extraDashboards.DropDownItems.Add(add);

        void Change(Action<List<ExtraDashboard>> change)
        {
            var list = extras.ToList();
            change(list);
            ExtraDashboardsChanged?.Invoke(this, list);
        }
    }

    private readonly ToolStripMenuItem _usbScreen = new("USB screen") { ToolTipText = "Show a layout on a small USB screen (Turing Smart Screen 3.5\" and its clones)" };

    /// <summary>Display → USB screen changed, with the new settings.</summary>
    public event EventHandler<UsbScreenSettings>? UsbScreenChanged;

    public void SetUsbScreen(UsbScreenSettings settings, IReadOnlyList<string> ports, IReadOnlyList<string> layouts, string status)
    {
        _usbScreen.DropDownItems.Clear();
        _usbScreen.DropDownItems.Add(new ToolStripMenuItem(Escape(status)) { Enabled = false });
        var on = new ToolStripMenuItem("Show on the USB screen") { Checked = settings.Enabled };
        on.Click += (_, _) => UsbScreenChanged?.Invoke(this, settings with { Enabled = !settings.Enabled });
        _usbScreen.DropDownItems.Add(on);
        _usbScreen.DropDownItems.Add(new ToolStripSeparator());

        var port = new ToolStripMenuItem("Port");
        foreach (var (value, label) in ports.Select(p => (p, p)).Prepend((UsbScreenSettings.Auto, "Find it automatically")))
        {
            var item = new ToolStripMenuItem(label) { Checked = string.Equals(settings.Port, value, StringComparison.OrdinalIgnoreCase) };
            item.Click += (_, _) => UsbScreenChanged?.Invoke(this, settings with { Port = value });
            port.DropDownItems.Add(item);
        }

        var layout = new ToolStripMenuItem("Layout");
        foreach (var (value, label) in layouts.Select(l => ((string?)l, Escape(l))).Prepend(((string?)null, "Same as the dashboard")))
        {
            var item = new ToolStripMenuItem(label) { Checked = string.Equals(settings.Layout, value, StringComparison.OrdinalIgnoreCase) };
            item.Click += (_, _) => UsbScreenChanged?.Invoke(this, settings with { Layout = value });
            layout.DropDownItems.Add(item);
        }

        var brightness = new ToolStripMenuItem("Brightness");
        foreach (var level in new[] { 10, 25, 40, 60, 80, 100 })
        {
            var item = new ToolStripMenuItem($"{level}%") { Checked = settings.Brightness == level };
            item.Click += (_, _) => UsbScreenChanged?.Invoke(this, settings with { Brightness = level });
            brightness.DropDownItems.Add(item);
        }

        var orientation = new ToolStripMenuItem("Way up");
        foreach (var (value, label) in UsbScreenSettings.Orientations)
        {
            var item = new ToolStripMenuItem(label) { Checked = settings.Orientation == value };
            item.Click += (_, _) => UsbScreenChanged?.Invoke(this, settings with { Orientation = value });
            orientation.DropDownItems.Add(item);
        }
        _usbScreen.DropDownItems.AddRange([port, layout, brightness, orientation]);
    }

    /// <summary>Tray → Display → Tablet view → Show on tablets: true to start serving, false to stop.</summary>
    public event EventHandler<bool>? TabletViewToggled;

    /// <summary>Tray → Display → Tablet view → Show link…</summary>
    public event EventHandler? TabletLinkRequested;

    public event EventHandler? TabletLinkCopyRequested;

    public event EventHandler? TabletNewLinkRequested;

    /// <summary>Display → Tablet view → Let tablets press buttons, with the new state.</summary>
    public event EventHandler<bool>? TabletButtonsToggled;

    public void SetTabletView(bool on, bool buttons)
    {
        _tabletOn.Checked = on;
        _tabletButtons.Checked = buttons;
        _tabletButtons.Enabled = on;
        _tabletShowLink.Enabled = _tabletCopyLink.Enabled = _tabletNewLink.Enabled = on;
    }

    private void AddOption(string text, bool isChecked, bool enabled, WindowOptions whenClicked, string? tip = null)
    {
        var item = new ToolStripMenuItem(text) { Checked = isChecked, Enabled = enabled, ToolTipText = tip };
        item.Click += (_, _) => WindowOptionsChanged?.Invoke(this, whenClicked);
        _displays.DropDownItems.Add(item);
    }

    /// <param name="onClick">What clicking the notification does, e.g. show the log it mentions.</param>
    public void ShowError(string title, string message, Action? onClick = null) =>
        Notify(5000, title, message, ToolTipIcon.Warning, onClick);

    /// <summary>An alert: a notification that stands out from the app's own messages.</summary>
    public void ShowAlert(string message) =>
        Notify(10000, "Impious Bonum alert", message, ToolTipIcon.Warning, null);

    /// <param name="onClick">What clicking the notification does, e.g. restart into a downloaded update.</param>
    public void ShowInfo(string title, string message, Action? onClick = null) =>
        Notify(5000, title, message, ToolTipIcon.Info, onClick);

    private void Notify(int timeout, string title, string message, ToolTipIcon icon, Action? onClick)
    {
        _onNotificationClicked = onClick;
        _icon.ShowBalloonTip(timeout, title, message, icon);
    }

    /// <summary>Opens the settings folder with dashboard.log selected, for notifications about errors.</summary>
    public static void ShowLog()
    {
        var arguments = File.Exists(AppPaths.LogFile) ? $"/select,\"{AppPaths.LogFile}\"" : $"\"{AppPaths.DataDirectory}\"";
        try
        {
            Process.Start("explorer.exe", arguments);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No Explorer to show it in; the notification has already said where the log is.
        }
    }

    private static void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No handler registered for .json; fall back to Notepad.
            Process.Start("notepad.exe", $"\"{path}\"");
        }
    }

    private static void OpenLink(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No browser registered; nothing else to offer.
        }
    }

    /// <summary>
    /// The app's own icon (assets/ImpiousBonum.ico, built in) at the size the notification area wants at the screen's
    /// scaling, so it stays sharp at 125% or 150% rather than being one 32-pixel drawing scaled down.
    /// </summary>
    private static Icon LoadIcon()
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream(IconResource)!;
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    internal const string IconResource = "ImpiousBonum.icon.ico";

    /// <summary>The app's icon at menu-icon size for this <paramref name="dpi"/>, for the menu's header.</summary>
    private static Bitmap AppIconBitmap(int dpi)
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream(IconResource)!;
        var size = (int)Math.Round(16 * dpi / 96f);
        using var icon = new Icon(stream, size, size);
        return icon.ToBitmap();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
        _bold.Dispose();
    }
}

/// <summary>How the dashboard window behaves, as shown and changed in tray → Display.</summary>
public sealed record WindowOptions(bool Windowed, bool Locked, bool ClickThrough, bool AlwaysOnTop, bool HideOverFullscreen);
