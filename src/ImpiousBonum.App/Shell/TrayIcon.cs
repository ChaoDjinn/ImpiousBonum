using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Shell;

/// <summary>The notification-area icon and its menu: displays, layouts, sensors, FPS source, the layout editor, updates and exit.</summary>
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
    private readonly ToolStripMenuItem _header = new("Impious Bonum") { Enabled = false };
    private readonly ToolStripMenuItem _checkUpdates = new("Check for updates") { Visible = false };
    private readonly ToolStripMenuItem _restartToUpdate = new() { Visible = false };
    private string? _activeLayoutPath;

    public TrayIcon()
    {
        _image = DrawIcon();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_header);
        menu.Items.Add(_restartToUpdate);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_displays);
        menu.Items.Add(_layouts);
        menu.Items.Add(_sensors);
        menu.Items.Add(_claude);
        menu.Items.Add(_weather);
        menu.Items.Add(new ToolStripMenuItem("Alerts…", null, (_, _) => AlertsRequested?.Invoke(this, EventArgs.Empty))
        {
            ToolTipText = "Be told when a reading is out of bounds for a while",
        });
        menu.Items.Add(new ToolStripMenuItem("Logging…", null, (_, _) => LoggingRequested?.Invoke(this, EventArgs.Empty))
        {
            ToolTipText = "Log readings to CSV files, with each one's average, lowest and highest",
        });
        menu.Items.Add(new ToolStripMenuItem("News feeds…", null, (_, _) => NewsRequested?.Invoke(this, EventArgs.Empty))
        {
            ToolTipText = "News feeds whose headlines the news widget shows",
        });
        menu.Items.Add(new ToolStripMenuItem("Calendar…", null, (_, _) => CalendarRequested?.Invoke(this, EventArgs.Empty))
        {
            ToolTipText = "Calendars whose events the agenda widget shows",
        });
        menu.Items.Add(new ToolStripMenuItem("Hotkeys…", null, (_, _) => HotkeysRequested?.Invoke(this, EventArgs.Empty))
        {
            ToolTipText = "Keys that show or hide the dashboard, turn pages or switch layouts from anywhere",
        });
        menu.Items.Add(new ToolStripMenuItem("Custom data…", null, (_, _) => CustomDataRequested?.Invoke(this, EventArgs.Empty))
        {
            ToolTipText = "Your own metrics from web addresses and commands",
        });
        menu.Items.Add(_fpsSource);
        menu.Items.Add(new ToolStripMenuItem("Edit layout…", null, (_, _) => EditLayoutRequested?.Invoke(this, EventArgs.Empty)) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add("Open layout file", null, (_, _) =>
        {
            if (_activeLayoutPath is not null)
                Open(_activeLayoutPath);
        });
        menu.Items.Add("Open settings folder", null, (_, _) => Open(AppPaths.DataDirectory));
        menu.Items.Add("Reload layout", null, (_, _) => ReloadRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(_startup);
        menu.Items.Add(_checkUpdates);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _sensors.DropDownItems.Add(_sensorStatus);
        _sensors.DropDownItems.Add(_sensorInstall);
        _sensors.DropDownItems.Add(_sensorRemove);
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

        _restartToUpdate.Font = new Font(menu.Font, FontStyle.Bold);
        _restartToUpdate.Click += (_, _) => RestartToUpdateRequested?.Invoke(this, EventArgs.Empty);
        _checkUpdates.Click += (_, _) => CheckForUpdatesRequested?.Invoke(this, EventArgs.Empty);

        _startup.Checked = StartupRegistration.IsEnabled;
        _startup.CheckedChanged += (_, _) => StartupRegistration.SetEnabled(_startup.Checked);
        menu.Opening += (_, _) =>
        {
            _startup.Checked = StartupRegistration.IsEnabled;
            MenuOpening?.Invoke(this, EventArgs.Empty);
        };

        _icon = new NotifyIcon { Icon = _image, Text = "Impious Bonum", ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => EditLayoutRequested?.Invoke(this, EventArgs.Empty);
    }

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
        _header.Text = version is null ? "Impious Bonum (development build)" : $"Impious Bonum {version}";
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
        _sensorInstall.Font = outdated ? new Font(_sensorInstall.Owner?.Font ?? SystemFonts.MenuFont!, FontStyle.Bold) : null;
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
        _activeLayoutPath = activePath;
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

    /// <summary>
    /// Fills the Game layouts submenu. <paramref name="recentApp"/> is the last app seen in front (the tray itself is in
    /// front while its menu is open) and is offered for linking to any of the saved layouts in <paramref name="names"/>,
    /// or to any of the <paramref name="themes"/> to restyle whichever layout is showing.
    /// </summary>
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
                item.Font = new Font(_schedule.DropDown.Font, FontStyle.Bold);
            item.DropDownItems.Add("Remove", null, (_, _) => ScheduleRuleRemoved?.Invoke(this, rule));
            _schedule.DropDownItems.Add(item);
        }
    }

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
        _displays.DropDownItems.Add(_away);
        _displays.DropDownItems.Add(_tablet);
        _displays.DropDownItems.Add(_usbScreen);
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

    public void ShowError(string title, string message) =>
        _icon.ShowBalloonTip(5000, title, message, ToolTipIcon.Warning);

    /// <summary>An alert: a notification that stands out from the app's own messages.</summary>
    public void ShowAlert(string message) =>
        _icon.ShowBalloonTip(10000, "Impious Bonum alert", message, ToolTipIcon.Warning);

    public void ShowInfo(string title, string message) =>
        _icon.ShowBalloonTip(5000, title, message, ToolTipIcon.Info);

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

    /// <summary>A small orange tile with three bars, drawn at runtime so there is no binary asset to maintain.</summary>
    private static Icon DrawIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var tile = new GraphicsPath();
            tile.AddArc(1, 1, 10, 10, 180, 90);
            tile.AddArc(21, 1, 10, 10, 270, 90);
            tile.AddArc(21, 21, 10, 10, 0, 90);
            tile.AddArc(1, 21, 10, 10, 90, 90);
            tile.CloseFigure();
            using var orange = new SolidBrush(Color.FromArgb(0xFF, 0xAA, 0x00));
            g.FillPath(orange, tile);
            g.FillRectangle(Brushes.Black, 7, 17, 4, 8);
            g.FillRectangle(Brushes.Black, 14, 9, 4, 16);
            g.FillRectangle(Brushes.Black, 21, 13, 4, 12);
        }

        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _image.Dispose();
    }
}

/// <summary>How the dashboard window behaves, as shown and changed in tray → Display.</summary>
public sealed record WindowOptions(bool Windowed, bool Locked, bool ClickThrough, bool AlwaysOnTop, bool HideOverFullscreen);
