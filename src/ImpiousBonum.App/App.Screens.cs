using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Threading;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.Core.Remote;
using ImpiousBonum.Core.Sensors;

namespace ImpiousBonum.App;

// Where the dashboards show: the main window and its placement, more dashboards, dimming, the tablet view and a USB screen.
public partial class App
{
    /// <summary>
    /// Dims the dashboard for the schedule and when you're away, each second. A game drawing frames counts as someone
    /// there, since a gamepad doesn't count as input to Windows.
    /// </summary>
    private void UpdateDimming()
    {
        if (_window is null || _sampler is null)
            return;
        var drawing = _sampler.Store.TryGet(SensorAliases.FramesPerSecond, out var fps) && fps.Value is > 0;
        var brightness = Dimming.Brightness(
            _scheduled?.Brightness, Dimming.IdleTime(), _settings.AwayDimMinutes, _settings.AwayBrightness,
            drawing, DateTime.UtcNow < _peekUntil, _editor is not null);
        _window.SetBrightness(brightness);
        foreach (var extra in _extras)
            extra.SetBrightness(brightness);
    }

    /// <summary>The show/hide hotkey. Hidden stays hidden (until pressed again or the app restarts), whatever else would show it.</summary>
    private void ShowOrHide()
    {
        if (_window is null)
            return;
        _hiddenByUser = !_hiddenByUser;
        AppLog.Info(_hiddenByUser ? "Dashboard hidden by hotkey" : "Dashboard shown by hotkey");
        if (_hiddenByUser)
        {
            _window.Hide();
            foreach (var extra in _extras)
                extra.Hide();
        }
        else
            Place();
    }

    /// <summary>Connects the USB screen (or not) and gives it its layout: its own, or the dashboard's.</summary>
    private void ApplyUsbScreen()
    {
        if (_usb is null || _layouts is null)
            return;
        var settings = _settings.UsbScreen;
        _usb.Apply(settings);
        if (!settings.Enabled)
            return;
        if (settings.Layout is { } name && _layouts.Library.Find(name) is { } found)
        {
            try
            {
                var layout = _layouts.Library.Load(found);
                _usb.Build(layout, _layouts.ResolveTheme(layout));
                return;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                AppLog.Warning($"USB screen: couldn't read layout \"{found}\": {ex.Message}");
            }
        }
        try
        {
            var current = _layouts.Load();
            _usb.Build(current, _layouts.ResolveTheme(current));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // ApplyLayout has already said what's wrong with it.
            AppLog.Warning($"USB screen: couldn't read the dashboard's layout: {ex.Message}");
        }
    }

    /// <summary>Tablet and USB screen → Show on tablets. Turning it on shows the link to open.</summary>
    private void SetTabletView(bool on)
    {
        _settings.TabletView = on;
        _settings.Save();
        if (!on)
        {
            _tablet?.Stop();
            _tabletLink?.Close();
            AppLog.Info("Tablet view off");
            return;
        }
        if (StartTablet())
            ShowTabletLink();
    }

    private bool StartTablet()
    {
        if (_tablet is null)
            return false;
        if (string.IsNullOrEmpty(_settings.TabletToken))
        {
            _settings.TabletToken = TabletAccess.NewToken();
            _settings.Save();
        }

        try
        {
            _tablet.Start(_settings.TabletPort, _settings.TabletToken);
            AppLog.Info($"Tablet view on, port {_settings.TabletPort}");
            return true;
        }
        catch (Exception ex) when (ex is SocketException or ArgumentOutOfRangeException)
        {
            // In use by another app, or a hand-edited port that isn't one.
            AppLog.Warning($"Tablet view couldn't use port {_settings.TabletPort}: {ex.Message}");
            _tray?.ShowError("Couldn't start the tablet view",
                $"Port {_settings.TabletPort} is in use or isn't a valid port. Change tabletPort in settings.json (Impious Bonum → General → Settings folder), then try again.");
            return false;
        }
    }

    private void ShowTabletLink()
    {
        if (_tabletLink is not null)
        {
            BringToFront(_tabletLink);
            return;
        }
        if (_tablet?.IsRunning != true || _settings.TabletToken is not { } token)
            return;

        _tabletLink = new TabletLinkWindow(_settings.TabletPort, token, NewTabletLink);
        _tabletLink.Closed += (_, _) => _tabletLink = null;
        _tabletLink.Show();
        BringToFront(_tabletLink);
    }

    private void CopyTabletLink()
    {
        if (_settings.TabletToken is not { } token)
            return;
        if (TabletLinkWindow.FirstLink(_settings.TabletPort, token) is not { } link)
            _tray?.ShowError("No tablet link", "This PC doesn't seem to be on a local network.");
        else if (TabletLinkWindow.CopyToClipboard(link))
            _tray?.ShowInfo("Tablet link copied", link);
    }

    /// <summary>A new secret for the link: tablets using the old one stop updating. Returns the new secret.</summary>
    private string NewTabletLink()
    {
        var token = TabletAccess.NewToken();
        _settings.TabletToken = token;
        _settings.Save();
        _tablet?.SetToken(token);
        AppLog.Info("Tablet view: new link");
        return token;
    }

    private void Place()
    {
        if (_window is null)
            return;

        var monitors = DisplayMonitor.GetAll();
        PlaceExtras(monitors);
        var monitor = DisplayMonitor.Resolve(_settings, monitors);
        if (monitor is null)
        {
            // Our monitor is unplugged or off. Wait for it rather than covering the main screen.
            _window.Hide();
            return;
        }

        if (_settings.MonitorId is null)
        {
            var windowed = _settings.PlaceFirstTime(monitor);
            _settings.MonitorId ??= monitor.Id;
            _settings.MonitorDevice ??= monitor.Device;
            _settings.Save();
            if (windowed)
            {
                AppLog.Info("No spare screen, so the dashboard starts as a window on the main screen");
                ApplyWindowBehaviour();
                _tray?.ShowInfo("The dashboard is a window for now",
                    "There's no spare screen, so it's on your main screen. Drag it where you like and resize it from its edges, then lock it on Impious Bonum's Screens page.");
            }
        }

        var b = monitor.Bounds;
        var rect = b;
        if (!_settings.Fill)
        {
            // Windowed: first time, the layout's shape centred on the monitor; after that, kept on screen.
            var saved = _settings.Area ?? WindowGeometry.DefaultArea(b, LayoutWidth, LayoutHeight);
            var area = WindowGeometry.Clamp(saved, b.Width, b.Height);
            if (area != _settings.Area)
            {
                _settings.Area = area;
                _settings.Save();
            }
            rect = new PixelRect(b.X + area.X, b.Y + area.Y, area.Width, area.Height);
        }

        _window.PlaceOn(rect);
        if (!_window.IsVisible && !_hiddenForFullscreen && !_hiddenByUser)
            _window.Show();
    }

    private double LayoutWidth => _window?.Dashboard.Width is > 0 and var w ? w : 1920;

    private double LayoutHeight => _window?.Dashboard.Height is > 0 and var h ? h : 480;

    private WindowOptions CurrentWindowOptions() =>
        new(!_settings.Fill, _settings.Locked, _settings.ClickThrough, _settings.AlwaysOnTop, _settings.HideOverFullscreen);

    private void ApplyWindowBehaviour()
    {
        var windowed = !_settings.Fill;
        _window?.SetBehaviour(
            movable: windowed && !_settings.Locked,
            clickThrough: windowed && _settings.Locked && _settings.ClickThrough,
            topmost: _settings.AlwaysOnTop);
        foreach (var extra in _extras)
            extra.SetBehaviour(movable: false, clickThrough: false, topmost: _settings.AlwaysOnTop);
    }

    /// <summary>Screens: fill screen or windowed, lock, click-through, always on top.</summary>
    private void SetWindowOptions(WindowOptions options)
    {
        var becameWindowed = options.Windowed && _settings.Fill;
        _settings.Fill = !options.Windowed;
        // Unlock on the way into windowed mode so it can be put in place straight away.
        _settings.Locked = !becameWindowed && options.Locked;
        _settings.ClickThrough = options.ClickThrough;
        _settings.AlwaysOnTop = options.AlwaysOnTop;
        _settings.HideOverFullscreen = options.HideOverFullscreen;
        _settings.Save();
        AppLog.Info($"Window: {(options.Windowed ? "windowed" : "fill screen")}, {(_settings.Locked ? "locked" : "unlocked")}"
            + $"{(_settings.ClickThrough ? ", click-through" : "")}{(_settings.AlwaysOnTop ? ", on top" : "")}");

        ApplyWindowBehaviour();
        UpdateFullscreenHide();
        Place();
        if (becameWindowed)
            _tray?.ShowInfo("Dashboard windowed", "Drag it where you want and resize it from the edges, then lock its position here.");
    }

    /// <summary>Back to the default windowed size, centred on the dashboard's monitor.</summary>
    private void ResetWindow()
    {
        _settings.Area = null;
        _settings.Save();
        Place();
        _window?.BringForward();
    }

    /// <summary>The window was dragged or resized: remember which monitor it's on and where.</summary>
    private void OnWindowMoved(PixelRect rect)
    {
        var monitors = DisplayMonitor.GetAll();
        var index = WindowGeometry.MostOverlapped(rect, monitors.Select(m => m.Bounds).ToList());
        if (index >= 0)
        {
            var monitor = monitors[index];
            var b = monitor.Bounds;
            _settings.MonitorId = monitor.Id;
            _settings.MonitorDevice = monitor.Device;
            _settings.Area = new PixelRect(rect.X - b.X, rect.Y - b.Y, rect.Width, rect.Height);
            _settings.Save();
        }
        // Dropped off every screen: Place puts it back where it was.
        Place();
        _settingsWindow?.Reload();
    }

    /// <summary>
    /// With always on top and "hide over fullscreen apps", steps aside while a game or video fills the dashboard's
    /// monitor, rather than sitting over it (which can also cost the game frame pacing).
    /// </summary>
    private void UpdateFullscreenHide()
    {
        if (_window is null)
            return;
        UpdateExtrasFullscreenHide();

        var handle = new WindowInteropHelper(_window).Handle;
        var hide = handle != 0 && _editor is null && _settings.AlwaysOnTop && _settings.HideOverFullscreen
            && (_window.IsVisible || _hiddenForFullscreen) && FullscreenApp.IsInFrontOf(handle);
        if (hide == _hiddenForFullscreen)
            return;

        _hiddenForFullscreen = hide;
        if (hide)
            _window.Hide();
        else
            Place();
    }

    /// <summary>
    /// Makes a window for each of the extra dashboards (closing any no longer wanted) and gives each its layout. Called
    /// with <see cref="ApplyLayout"/>, so a layout saved or changed on disk shows on them too.
    /// </summary>
    private void ApplyExtraDashboards()
    {
        if (_layouts is null)
            return;
        var wanted = _settings.ExtraDashboards;
        while (_extras.Count > wanted.Count)
        {
            _extrasHiddenForFullscreen.Remove(_extras[^1]);
            _extras[^1].Close();
            _extras.RemoveAt(_extras.Count - 1);
        }
        while (_extras.Count < wanted.Count)
            _extras.Add(CreateExtraWindow());

        for (var i = 0; i < wanted.Count; i++)
        {
            LayoutDocument layout;
            var name = _layouts.Library.Find(wanted[i].Layout);
            try
            {
                layout = name is null ? LayoutStore.LoadDefault() : _layouts.Library.Load(name);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                AppLog.Warning($"Dashboard on {wanted[i].MonitorDevice}: couldn't read layout \"{name}\": {ex.Message}");
                layout = LayoutStore.LoadDefault();
            }
            if (name is null && _reportedMissingLinks.Add($"extra:{wanted[i].Layout}"))
                _tray?.ShowError("Impious Bonum", $"There's no layout called \"{wanted[i].Layout}\" any more. Pick another on Impious Bonum's Screens page.");

            var theme = _layouts.ResolveTheme(layout);
            _extras[i].Background = Theme.From(theme).Background;
            _extras[i].Dashboard.Build(layout, theme);
        }
    }

    private DashboardWindow CreateExtraWindow()
    {
        var window = new DashboardWindow();
        window.EditRequested += (_, _) => EditDashboard(ScreenOf(window));
        window.Touched += (_, _) => _lastTouch = DateTime.UtcNow;
        window.PeekRequested += (_, _) =>
        {
            _peekUntil = DateTime.UtcNow + Dimming.PeekFor;
            UpdateDimming();
        };
        window.ActionRequested += (_, action) => RunAction(action);
        window.SetBehaviour(movable: false, clickThrough: false, topmost: _settings.AlwaysOnTop);
        return window;
    }

    /// <summary>The monitor id of the extra dashboard in <paramref name="window"/>, which the editor knows it by.</summary>
    private string? ScreenOf(DashboardWindow window)
    {
        var index = _extras.IndexOf(window);
        return index >= 0 && index < _settings.ExtraDashboards.Count ? _settings.ExtraDashboards[index].MonitorId : null;
    }

    /// <summary>
    /// Screens → Add a dashboard, or the gallery: a dashboard on <paramref name="monitor"/> showing <paramref name="layout"/>,
    /// or with a new, empty layout of its own (see <see cref="ExtraDashboard.NewLayout"/>), open in the editor to fill.
    /// </summary>
    private void AddDashboard(DisplayMonitor monitor, string? layout = null)
    {
        if (_layouts is null || RefuseWhileEditing())
            return;
        var name = layout ?? ExtraDashboard.NewLayoutName(monitor, _layouts.List());
        if (layout is null)
            _layouts.Save(name, ExtraDashboard.NewLayout(monitor, _layouts.LoadOrDefault().Theme));
        _settings.ExtraDashboards = [.. _settings.ExtraDashboards, ExtraDashboard.On(monitor, name)];
        _settings.Save();
        AppLog.Info($"More dashboards: added \"{name}\" on {monitor.Device}");
        ApplyExtraDashboards();
        Place();
        if (layout is null)
            EditDashboard(monitor.Id);
    }

    /// <summary>
    /// Dashboards aren't added, moved or removed while the editor is open, since it may be editing one of them: says so
    /// and returns true then. (The window's other pages, where that's done, don't show while the editor does.)
    /// </summary>
    private bool RefuseWhileEditing()
    {
        if (_editor is null)
            return false;
        _tray?.ShowInfo("Leave the layout editor first", "Then add, move or remove the dashboard again.");
        return true;
    }

    /// <summary>The extra dashboards showing the layout <paramref name="name"/>, which the editor's changes show on live.</summary>
    private IEnumerable<DashboardWindow> ExtrasShowing(string? name) =>
        _extras.Where((_, i) => i < _settings.ExtraDashboards.Count && string.Equals(_settings.ExtraDashboards[i].Layout, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Fills each extra dashboard's monitor; one whose monitor isn't connected hides until it is.</summary>
    private void PlaceExtras(IReadOnlyList<DisplayMonitor> monitors)
    {
        for (var i = 0; i < _extras.Count && i < _settings.ExtraDashboards.Count; i++)
        {
            var window = _extras[i];
            if (_settings.ExtraDashboards[i].Resolve(monitors) is not { } monitor || _hiddenByUser || _extrasHiddenForFullscreen.Contains(window))
            {
                window.Hide();
                continue;
            }
            window.PlaceOn(monitor.Bounds);
            if (!window.IsVisible)
                window.Show();
        }
    }

    /// <summary>The extra dashboards step aside over fullscreen apps too, each for its own monitor.</summary>
    private void UpdateExtrasFullscreenHide()
    {
        var changed = false;
        foreach (var window in _extras)
        {
            var handle = new WindowInteropHelper(window).Handle;
            var hidden = _extrasHiddenForFullscreen.Contains(window);
            var hide = handle != 0 && _editor is null && _settings.AlwaysOnTop && _settings.HideOverFullscreen
                && (window.IsVisible || hidden) && FullscreenApp.IsInFrontOf(handle);
            if (hide == hidden)
                continue;
            if (hide)
            {
                _extrasHiddenForFullscreen.Add(window);
                window.Hide();
            }
            else
            {
                _extrasHiddenForFullscreen.Remove(window);
                changed = true;
            }
        }
        if (changed)
            PlaceExtras(DisplayMonitor.GetAll());
    }

    private void MoveTo(DisplayMonitor monitor)
    {
        // One dashboard a screen: the main one takes the place of another already there.
        var displaced = _settings.ExtraDashboards.FirstOrDefault(e => e.MonitorId == monitor.Id);
        if (displaced is not null && RefuseWhileEditing())
            return;
        _settings.MonitorId = monitor.Id;
        _settings.MonitorDevice = monitor.Device;
        if (displaced is not null)
        {
            _settings.ExtraDashboards = _settings.ExtraDashboards.Where(e => e != displaced).ToList();
            ApplyExtraDashboards();
            _tray?.ShowInfo("Impious Bonum", $"The dashboard showing \"{displaced.Layout}\" was on that screen, so it's been removed.");
        }
        _settings.Save();
        Place();
        _editor?.SetScreens(EditorScreens(), ScreenIndex(_editingScreen));
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        // Raised off the UI thread, often several times while displays settle.
        Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(750);
            Place();
            _settingsWindow?.Reload();
        });
}
