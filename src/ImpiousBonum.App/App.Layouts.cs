using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using ImpiousBonum.App.Editor;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.App.Widgets;

namespace ImpiousBonum.App;

// Which layout shows: the editor, saved layouts, the gallery, game layouts, the schedule and pages.
public partial class App
{
    private void OpenEditor()
    {
        if (_editor is not null)
        {
            BringToFront(_editor);
            return;
        }

        var session = new LayoutSession(_layouts!.LoadOrDefault(), _layouts.Themes);
        _editor = new EditorWindow(session, _layouts, () =>
            !_settings.Fill && _settings.Area is { } area ? (area.Width, area.Height)
            : DisplayMonitor.Resolve(_settings, DisplayMonitor.GetAll()) is { } monitor ? (monitor.Bounds.Width, monitor.Bounds.Height)
            : null);

        session.Changed += (_, change) => MirrorToDashboard(session, change);
        _editor.Setups = WidgetSetups;
        _editor.WidgetAdded += (_, descriptor) =>
        {
            if (descriptor.Type == BarsWidget.ClaudeDescriptor.Type && _editor is { } editor)
                OfferClaudeCodeConnection(editor);
            else if (descriptor.Type == WeatherWidget.Descriptor.Type && _settings.WeatherLocation is null && _editor is { } owner)
                SetWeatherLocation(owner);
        };
        _editor.Closed += (_, _) =>
        {
            _editor = null;
            _window?.EndEdit();
            // Catch up with any game that came or went while the editor had the layout.
            ApplyGameLayout();
            // Back to what's on disk: the saved layout, or the old one if changes were discarded.
            ApplyLayout();
        };
        _editor.Show();
        BringToFront(_editor);
        _window?.BeginEdit(session);
        Refresh();
    }

    /// <summary>The editor's buttons for what a widget needs set up outside the layout: the same as the tray's.</summary>
    internal IReadOnlyList<WidgetSetup> WidgetSetups(WidgetDescriptor descriptor)
    {
        if (descriptor.Type == WeatherWidget.Descriptor.Type)
        {
            return [new(_settings.WeatherLocation is null ? "Set location…" : "Change location…",
                () => _settings.WeatherLocation is { } place ? $"Forecasts for {place.Name}." : "No location yet, so there's no forecast.",
                SetWeatherLocation)];
        }
        if (descriptor.Type == AgendaWidget.Descriptor.Type)
            return [new("Calendars…", () => Count(_settings.Calendars.Count, "calendar", "No calendars yet, so the agenda is empty."), EditCalendars)];
        if (descriptor.Type == NewsWidget.Descriptor.Type)
            return [new("News feeds…", () => Count(_settings.NewsFeeds.Count, "news feed", "No news feeds yet, so there are no headlines."), EditNews)];
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

    /// <summary>Tray → Layout. With the editor open, the editor switches (asking about unsaved changes first).</summary>
    private void SelectLayout(string name)
    {
        if (_editor is not null)
        {
            BringToFront(_editor);
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

    /// <summary>Tray → Layout → Gallery…; shows what was imported. Not while the editor is open, which has its own copy of the layouts.</summary>
    private void OpenGallery()
    {
        if (_editor is not null)
        {
            BringToFront(_editor);
            _tray?.ShowInfo("Layout gallery", "Close the layout editor first, then open the gallery from the tray.");
            return;
        }
        if (_layouts is null)
            return;
        var gallery = new GalleryWindow(_settings.GalleryAddress ?? Gallery.DefaultAddress, _layouts, _sampler?.Store);
        if (gallery.ShowDialog() != true || gallery.Imported is not { } name)
            return;
        _layouts.SetActive(name);
        AppLog.Info($"Imported layout \"{name}\" from the gallery");
    }

    /// <summary>Tray → Layout → Import…: adds the layout and shows it. With the editor open, the editor does it.</summary>
    private void ImportLayout()
    {
        if (_editor is not null)
        {
            BringToFront(_editor);
            _editor.ImportLayout();
            return;
        }
        if (_layouts is null || LayoutTransfer.Import(null, _layouts) is not { } name)
            return;

        _layouts.SetActive(name);
        AppLog.Info($"Imported layout \"{name}\"");
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
            _recentApp = app;
        _trackedGame = _settings.SessionRecaps && GameLayoutSwitcher.Match(_settings.GameLayouts, app) is { } game
            ? game.Layout ?? GameLayoutRule.Normalize(game.Process)
            : null;

        if (_settings.GameLayoutsEnabled && _gameSwitcher.Update(_settings.GameLayouts, app, DateTime.UtcNow))
            ApplyGameLayout();
        UpdateSchedule();
    }

    /// <summary>Checks the schedule (each second, with the game check) and switches when what it wants changes.</summary>
    private void UpdateSchedule()
    {
        var look = _settings.ScheduleEnabled ? ScheduledLook.From(_settings.Schedule, TimeOnly.FromDateTime(DateTime.Now), WindowsColourMode.IsDark()) : null;
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

    /// <summary>Tray → Layout → Schedule → Add…</summary>
    private void AddScheduleRule()
    {
        if (_layouts is null)
            return;
        if (ScheduleRuleWindow.Ask(_layouts.List(), _layouts.Themes.List(), "Night Watch") is not { } rule)
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
            $"{who} the {kind} \"{name}\", which doesn't exist any more. Tray → Layout → Game layouts or Schedule.");
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
        _tray?.ShowInfo("Game layout linked", rule.Layout is null
            ? $"The theme \"{rule.Theme}\" will restyle the dashboard a few seconds after \"{rule.Process}\" comes to the front."
            : $"\"{rule.Describe()}\" will show a few seconds after \"{rule.Process}\" comes to the front.");
    }

    private void UnlinkGame(GameLayoutRule rule)
    {
        _settings.GameLayouts.Remove(rule);
        _settings.Save();
        AppLog.Info($"Removed the link from \"{rule.Process}\" to {rule.Describe()}");
    }

    /// <summary>Shows the editor's working copy on the real dashboard as you edit, so you see it at true size.</summary>
    private void MirrorToDashboard(LayoutSession session, LayoutChange change)
    {
        if (_window is null)
            return;

        if (change.Kind == ChangeKind.Geometry && change.WidgetIndex is { } index)
        {
            var r = LayoutSession.GeometryOf(session.Document.Widgets[index]);
            _window.Dashboard.SetGeometry(index, r.X, r.Y, r.Width, r.Height);
            _tablet?.SetGeometry(index, r.X, r.Y, r.Width, r.Height);
            foreach (var extra in ExtrasShowing(_layouts?.Active))
                extra.Dashboard.SetGeometry(index, r.X, r.Y, r.Width, r.Height);
            return;
        }

        if (_mirrorQueued)
            return;
        _mirrorQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _mirrorQueued = false;
            _window.Background = Theme.From(session.ResolvedTheme).Background;
            _window.Dashboard.Build(session.Document, session.ResolvedTheme);
            _tablet?.Build(session.Document, session.ResolvedTheme);
            foreach (var extra in ExtrasShowing(_layouts?.Active))
            {
                extra.Background = _window.Background;
                extra.Dashboard.Build(session.Document, session.ResolvedTheme);
            }
            Refresh();
        }, DispatcherPriority.Background);
    }

    private void ApplyLayout()
    {
        // While the editor is open it owns the layout (including our own saves, which trigger the file watcher).
        if (_layouts is null || _window is null || _editor is not null)
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
