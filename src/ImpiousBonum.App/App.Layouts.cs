using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using ImpiousBonum.App.Editor;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Settings;
using ImpiousBonum.App.Shell;
using ImpiousBonum.App.Widgets;

namespace ImpiousBonum.App;

// Which layout shows: the editor, saved layouts, game layouts, the schedule and pages.
public partial class App
{
    /// <summary>Tray → Edit layout…, the hotkey and the dashboard's own edit button: the editor as it is, or on the main dashboard.</summary>
    private void OpenEditor()
    {
        var window = ShowSettings();
        if (_editor is null)
            window.EditScreen(null);
    }

    /// <summary>
    /// The editor on the layout one dashboard shows: the main one (null) or another, by its monitor's id. An editor
    /// already open on another dashboard moves to this one, asking about unsaved changes first.
    /// </summary>
    private void EditDashboard(string? screen)
    {
        var window = ShowSettings(openEditor: false);
        if (_editor is not null)
            _editor.SwitchScreen(ScreenIndex(screen));
        else
            window.EditScreen(screen);
    }

    /// <summary>Where a dashboard is in the editor's list of them: the main one first, then the others in order.</summary>
    private int ScreenIndex(string? screen) =>
        screen is null ? 0 : _settings.ExtraDashboards.FindIndex(e => e.MonitorId == screen) + 1;

    /// <summary>The dashboards the editor can edit, by their screens' names, in <see cref="ScreenIndex"/> order.</summary>
    private List<string> EditorScreens()
    {
        var monitors = DisplayMonitor.GetAll();
        var main = DisplayMonitor.Resolve(_settings, monitors)?.DisplayName
            ?? $"{DisplayMonitor.ShortName(_settings.MonitorDevice ?? "Dashboard")} (not connected)";
        return [main, .. _settings.ExtraDashboards.Select(e => e.MonitorName(monitors))];
    }

    /// <summary>The extra dashboard the editor is on, or null for the main one.</summary>
    private ExtraDashboard? EditedExtra() =>
        _editingScreen is null ? null : _settings.ExtraDashboards.FirstOrDefault(e => e.MonitorId == _editingScreen);

    /// <summary>The saved layout the dashboard being edited shows.</summary>
    private string EditedLayout() => EditedExtra()?.Layout ?? _layouts!.Active;

    /// <summary>The window of the dashboard being edited, which shows the editor's handles.</summary>
    private DashboardWindow? EditedWindow()
    {
        if (_editingScreen is null)
            return _window;
        var index = ScreenIndex(_editingScreen) - 1;
        return index >= 0 && index < _extras.Count ? _extras[index] : null;
    }

    /// <summary>The size in pixels of the dashboard being edited, for the editor's "Match dashboard screen".</summary>
    private (int Width, int Height)? EditedScreenSize()
    {
        var monitors = DisplayMonitor.GetAll();
        if (EditedExtra() is { } extra)
            return extra.Resolve(monitors) is { } screen ? (screen.Bounds.Width, screen.Bounds.Height) : null;
        return !_settings.Fill && _settings.Area is { } area ? (area.Width, area.Height)
            : DisplayMonitor.Resolve(_settings, monitors) is { } monitor ? (monitor.Bounds.Width, monitor.Bounds.Height)
            : null;
    }

    /// <summary>The editor moved to another saved layout: the dashboard it's on shows that one from now on.</summary>
    private void ShowOnEditedDashboard(string name)
    {
        if (EditedExtra() is { } extra)
        {
            _settings.ExtraDashboards = _settings.ExtraDashboards.Select(e => e.MonitorId == extra.MonitorId ? e with { Layout = name } : e).ToList();
            _settings.Save();
            AppLog.Info($"Dashboard on {extra.MonitorDevice}: showing layout \"{name}\"");
        }
        else
        {
            try
            {
                _layouts?.SetActive(name);
            }
            catch (FileNotFoundException ex)
            {
                // Deleted on disk while the editor was picking it.
                AppLog.Warning(ex.Message);
            }
        }
        // Other dashboards that were showing unsaved changes to the last layout go back to its saved file.
        ShowSavedLayouts();
    }

    /// <summary>The editor's buttons for what a widget needs set up outside the layout: the same as the tray's.</summary>
    internal IReadOnlyList<WidgetSetup> WidgetSetups(WidgetDescriptor descriptor)
    {
        if (descriptor.Type == WeatherWidget.Descriptor.Type)
        {
            return [new(_settings.WeatherLocation is null ? "Set location…" : "Change location…",
                () => _settings.WeatherLocation is not { } place ? "No location yet, so there's no forecast."
                    : _settings.WeatherPlaces.Count switch
                    {
                        0 => $"Forecasts for {place.Name}.",
                        1 => $"Forecasts for {place.Name}, and 1 more place on the Weather page.",
                        var more => $"Forecasts for {place.Name}, and {more} more places on the Weather page.",
                    },
                SetWeatherLocation)];
        }
        if (descriptor.Type == AgendaWidget.Descriptor.Type)
            return [new("Calendars…", () => Count(_settings.Calendars.Count, "calendar", "No calendars yet, so the agenda is empty."), EditCalendars)];
        if (descriptor.Type == NewsWidget.Descriptor.Type)
            return [new("News feeds…", () => Count(_settings.NewsFeeds.Count, "news feed", "No news feeds yet, so there are no headlines."), EditNews)];
        if (descriptor.Type == StatusWidget.Descriptor.Type)
        {
            return [new("Add a check…", () => _settings.StatusChecks.Count switch
            {
                0 => "No status checks yet, so there's nothing to show.",
                1 => "1 status check. Change it on the Status checks page.",
                var count => $"{count} status checks. Change them on the Status checks page.",
            }, AddStatusCheck)];
        }
        if (descriptor.Type == BarsWidget.ClaudeDescriptor.Type)
        {
            return [new(IsClaudeCodeConnected() ? "Reconnect Claude Code" : "Connect Claude Code…",
                () => IsClaudeCodeConnected() ? "Claude Code is connected." : "Claude Code isn't connected yet, so there's no usage.",
                owner => ConnectClaudeCode(owner))];
        }
        return [];

        static string Count(int count, string what, string none) => count switch
        {
            0 => none,
            1 => $"1 {what}.",
            _ => $"{count} {what}s.",
        };
    }

    /// <summary>
    /// The names a widget setting can pick from that the app keeps: the news feeds, in the News feeds page's order, the
    /// weather's places, the location first and then the Weather page's more places (none without a location), or the
    /// status checks, in the Status checks page's order.
    /// </summary>
    internal IReadOnlyList<string> PickOptions(string source) => source switch
    {
        NewsWidget.FeedNames => [.. _settings.NewsFeeds.Select(f => f.Name)],
        WeatherWidget.PlaceNames => WeatherWidget.ShortNames(WeatherPlaceNames()),
        StatusWidget.CheckNames => [.. _settings.StatusChecks.Select(c => c.Name.Trim())],
        _ => [],
    };

    /// <summary>
    /// For a name picked from <see cref="PickOptions"/> that isn't spelt as one of them now, the one it stands for, as the
    /// weather widget finds it: a place picked as Paris (Texas) is plain Paris once it's the only Paris, and the other way round.
    /// </summary>
    internal string? PickedOption(string source, string name) =>
        source == WeatherWidget.PlaceNames ? WeatherWidget.PlaceOption(name, WeatherPlaceNames()) : null;

    /// <summary>The weather's places by their full names, the location first; none without a location.</summary>
    private List<string> WeatherPlaceNames() =>
        _settings.WeatherLocation is { } main ? [main.Name, .. _settings.WeatherPlaces.Select(p => p.Name)] : [];

    /// <summary>
    /// Tray → Layout, the Screens page, pages, hotkeys and buttons: the main dashboard's layout. With the editor open, the editor moves to
    /// the main dashboard if it's on another and switches (asking about unsaved changes first).
    /// </summary>
    private void SelectLayout(string name)
    {
        if (_editor is not null)
        {
            if (_settingsWindow is not null)
                BringToFront(_settingsWindow);
            if (_editor.SwitchScreen(0))
                _editor.SwitchTo(name);
            return;
        }

        try
        {
            _layouts?.SetActive(name);
        }
        catch (FileNotFoundException ex)
        {
            _tray?.ShowError("Couldn't switch layout", ex.Message);
        }
    }

    /// <summary>
    /// Follows the app the user is looking at (the same one the FPS reading follows) and switches to a linked game's
    /// layout once it has been in front for a few seconds, and back once it has gone.
    /// </summary>
    private void UpdateGameLayout()
    {
        if (_frameRateTarget is null || _layouts is null)
            return;

        var (_, app) = _frameRateTarget.Get();
        if (app is not null && !NotGames.Contains(app))
            NoteRecentApp(app);
        _trackedGame = _settings.SessionRecaps && GameLayoutSwitcher.Match(_settings.GameLayouts, app) is { } game
            ? game.Layout ?? GameLayoutRule.Normalize(game.Process)
            : null;
        _linkedGame = GameLayoutSwitcher.Match(_settings.GameLayouts, app) is null ? null : app;

        if (_settings.GameLayoutsEnabled && _gameSwitcher.Update(_settings.GameLayouts, app, DateTime.UtcNow))
            ApplyGameLayout();
        UpdateSchedule();
    }

    /// <summary>Checks the schedule and holiday themes (each second, with the game check) and switches when what they want changes.</summary>
    private void UpdateSchedule()
    {
        var now = ScheduleNow.Current(_settings.WeatherLocation);
        var holiday = _settings.HolidayThemes ? Holidays.On(DateOnly.FromDateTime(now.Time)) : null;
        var look = ScheduledLook.From(_settings.ScheduleEnabled ? _settings.Schedule : [], now, holiday?.Theme);
        if (look == _scheduled)
            return;
        _scheduled = look;
        AppLog.Info(look is null ? "Schedule: nothing scheduled now" : $"Schedule: {look.Describe()}");
        ApplyGameLayout();
    }

    /// <summary>
    /// Moves to the next page every few seconds when rotation is on: not while editing, while a game's layout is
    /// showing, or for a minute after someone touched the dashboard.
    /// </summary>
    private void RotatePages()
    {
        var now = DateTime.UtcNow;
        if (_settings.RotatePagesSeconds <= 0 || _layouts is null || _editor is not null || _layouts.IsTemporary
            || now - _lastTouch < Pages.RotationPauseAfterTouch || now - _lastPageChange < TimeSpan.FromSeconds(_settings.RotatePagesSeconds))
            return;
        TurnPage(1);
    }

    /// <summary>A swipe or rotation: shows the page <paramref name="step"/> away, as if picked from the tray, with page dots.</summary>
    private void TurnPage(int step)
    {
        _lastPageChange = DateTime.UtcNow;
        if (_layouts is null || _editor is not null)
            return;
        var pages = Pages.Of(_layouts.List(), _settings.Pages);
        if (Pages.Next(pages, _layouts.Active, step) is not { } next)
            return;
        SelectLayout(next);
        _window?.ShowPage(pages.ToList().FindIndex(p => string.Equals(p, next, StringComparison.OrdinalIgnoreCase)), pages.Count);
    }

    /// <summary>Pages and schedule → Add to the schedule.</summary>
    private void AddScheduleRule(System.Windows.Window owner)
    {
        if (_layouts is null)
            return;
        if (ScheduleRuleWindow.Ask(owner, _layouts.List(), _layouts.Themes.List(), "Night Watch", _settings.WeatherLocation is not null) is not { } rule)
            return;
        _settings.Schedule.Add(rule);
        _settings.Save();
        AppLog.Info($"Added to the schedule: {rule.Describe()}");
        UpdateSchedule();
    }

    /// <summary>Apps offered for linking never include the shell, whose windows come to the front when you use the tray.</summary>
    private static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp", "LockApp",
        Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "ImpiousBonum",
    };

    /// <summary>
    /// Shows the layout and theme a game in front wants, with the schedule filling in what the game's link doesn't set,
    /// unless the editor has the layout or they're already showing.
    /// </summary>
    private void ApplyGameLayout()
    {
        if (_layouts is null || _editor is not null)
            return;

        var rule = _gameSwitcher.Current;
        var scheduled = _scheduled;
        var layout = rule?.Layout ?? scheduled?.Layout;
        var theme = rule?.Theme ?? scheduled?.Theme;
        var wanted = layout is null && theme is null ? null : $"{layout}|{theme}";
        if (string.Equals(wanted, _gameLayoutApplied, StringComparison.OrdinalIgnoreCase))
            return;
        _gameLayoutApplied = wanted;

        if (layout is not null && _layouts.Library.Find(layout) is null)
        {
            ReportMissing(rule?.Layout is not null ? $"\"{rule.Process}\" is linked to" : "The schedule names", "layout", layout);
            layout = null;
        }
        if (theme is not null && _layouts.Themes.Find(theme) is null)
        {
            ReportMissing(rule?.Theme is not null ? $"\"{rule.Process}\" is linked to" : "The schedule names", "theme", theme);
            theme = null;
        }

        AppLog.Info(rule is not null ? $"\"{rule.Process}\" is in front; switching to {rule.Describe()}{(scheduled is null ? "" : $" (schedule: {scheduled.Describe()})")}"
            : scheduled is not null ? $"Scheduled: {scheduled.Describe()}"
            : "No linked game in front and nothing scheduled; back to the chosen layout");
        try
        {
            _layouts.ShowTemporarily(layout);
            _layouts.ShowThemeTemporarily(theme);
        }
        catch (FileNotFoundException ex)
        {
            // Deleted between the check and the switch.
            AppLog.Warning(ex.Message);
        }
    }

    /// <param name="who">"\"game\" is linked to" or "The schedule names".</param>
    private void ReportMissing(string who, string kind, string name)
    {
        if (!_reportedMissingLinks.Add($"{kind}:{name}"))
            return;
        AppLog.Warning($"{who} the {kind} \"{name}\", which doesn't exist; ignored");
        _tray?.ShowError($"{char.ToUpperInvariant(kind[0])}{kind[1..]} not found",
            $"{who} the {kind} \"{name}\", which doesn't exist any more. Change it in Impious Bonum: Game layouts, or Pages and schedule.");
    }

    private void SetGameLayoutsEnabled(bool enabled)
    {
        _settings.GameLayoutsEnabled = enabled;
        _settings.Save();
        AppLog.Info($"Automatic game layouts {(enabled ? "on" : "off")}");
        if (!enabled)
        {
            _gameSwitcher.Reset();
            ApplyGameLayout();
        }
    }

    private void LinkGame(GameLayoutRule rule)
    {
        _settings.GameLayouts.RemoveAll(r => r.Matches(rule.Process));
        _settings.GameLayouts.Add(rule);
        _settings.Save();
        _reportedMissingLinks.Remove($"layout:{rule.Layout}");
        _reportedMissingLinks.Remove($"theme:{rule.Theme}");
        AppLog.Info($"Linked \"{rule.Process}\" to {rule.Describe()}");
    }

    private void UnlinkGame(GameLayoutRule rule)
    {
        _settings.GameLayouts.Remove(rule);
        _settings.Save();
        AppLog.Info($"Removed the link from \"{rule.Process}\" to {rule.Describe()}");
    }

    /// <summary>
    /// Shows the editor's working copy on the real dashboards as you edit, so you see it at true size: the one being
    /// edited, and any other showing the same layout.
    /// </summary>
    private void MirrorToDashboard(LayoutSession session, LayoutChange change)
    {
        if (_window is null || _layouts is null)
            return;

        if (change.Kind == ChangeKind.Geometry && change.WidgetIndex is { } index)
        {
            var r = LayoutSession.GeometryOf(session.Document.Widgets[index]);
            var name = EditedLayout();
            if (IsMainShowing(name))
            {
                _window.Dashboard.SetGeometry(index, r.X, r.Y, r.Width, r.Height);
                _tablet?.SetGeometry(index, r.X, r.Y, r.Width, r.Height);
            }
            foreach (var extra in ExtrasShowing(name))
                extra.Dashboard.SetGeometry(index, r.X, r.Y, r.Width, r.Height);
            return;
        }

        if (_mirrorQueued)
            return;
        _mirrorQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _mirrorQueued = false;
            if (_editor is null)
                return;
            var name = EditedLayout();
            var background = Theme.From(session.ResolvedTheme).Background;
            if (IsMainShowing(name))
            {
                _window.Background = background;
                _window.Dashboard.Build(session.Document, session.ResolvedTheme);
                _tablet?.Build(session.Document, session.ResolvedTheme);
            }
            foreach (var extra in ExtrasShowing(name))
            {
                extra.Background = background;
                extra.Dashboard.Build(session.Document, session.ResolvedTheme);
            }
            Refresh();
        }, DispatcherPriority.Background);
    }

    private bool IsMainShowing(string name) => string.Equals(_layouts?.Active, name, StringComparison.OrdinalIgnoreCase);

    private void ApplyLayout()
    {
        // While the editor is open it owns the layout (including our own saves, which trigger the file watcher).
        if (_editor is not null)
            return;
        ShowSavedLayouts();
    }

    /// <summary>Every dashboard, the tablet view and the USB screen show their layouts as saved.</summary>
    private void ShowSavedLayouts()
    {
        if (_layouts is null || _window is null)
            return;
        ApplyExtraDashboards();

        LayoutDocument layout;
        try
        {
            layout = _layouts.Load();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Keep showing the previous layout; an obviously broken file shouldn't blank the screen.
            _tray?.ShowError($"Couldn't read {Path.GetFileName(_layouts.ActivePath)}", ex.Message);
            if (_window.Dashboard.Children.Count > 0)
                return;
            layout = LayoutStore.LoadDefault();
        }

        var issues = LayoutValidator.Validate(layout, _layouts.Themes);
        if (issues.Count > 0)
            _tray?.ShowError($"{Path.GetFileName(_layouts.ActivePath)}: {issues.Count} problem{(issues.Count == 1 ? "" : "s")}", string.Join("\n", issues.Take(3)));

        var theme = _layouts.ResolveTheme(layout);
        _window.Background = Theme.From(theme).Background;
        _window.Dashboard.Build(layout, theme);
        _tablet?.Build(layout, theme);
        if (_settings.UsbScreen.Layout is null)
            _usb?.Build(layout, theme);
        else
            ApplyUsbScreen();
        Refresh();
    }
}
