using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Settings;

/// <summary>
/// Screens: a map of the monitors as Windows arranges them, each showing the dashboard on it if any. Picking one shows
/// what can be done there: choose its layout, edit it, size the main dashboard's window, add a dashboard or move the
/// main one there. Below, what goes for every dashboard.
/// </summary>
public partial class ScreensPage : UserControl, IPage
{
    internal static readonly (int Minutes, string Label)[] AwayChoices =
        [(0, "Don't dim"), (5, "After 5 minutes"), (15, "After 15 minutes"), (30, "After 30 minutes"), (60, "After an hour")];

    internal static readonly (int Brightness, string Label)[] AwayLevels = [(40, "To 40%"), (20, "To 20%"), (0, "Turn it black")];

    private readonly ISettingsHost _host;
    private readonly SettingsWindow _window;
    private IReadOnlyList<DisplayMonitor> _monitors = [];
    private string? _selected;
    private bool _loading;

    internal ScreensPage(ISettingsHost host, SettingsWindow window)
    {
        InitializeComponent();
        _host = host;
        _window = window;
        AwayBox.ItemsSource = AwayChoices.Select(c => c.Label).ToList();
        AwayLevelBox.ItemsSource = AwayLevels.Select(c => c.Label).ToList();
        MapCard.SizeChanged += (_, _) => DrawMap();
    }

    private AppSettings Settings => _host.Settings;

    public void Load()
    {
        _loading = true;
        _monitors = DisplayMonitor.GetAll();
        var main = DisplayMonitor.Resolve(Settings, _monitors);
        if (_selected is null || _monitors.All(m => m.Id != _selected))
            _selected = main?.Id ?? _monitors.FirstOrDefault(m => m.IsPrimary)?.Id;

        var options = _host.WindowOptions;
        OnTopSwitch.IsChecked = options.AlwaysOnTop;
        FullscreenSwitch.IsChecked = options.HideOverFullscreen;
        FullscreenCard.IsEnabled = options.AlwaysOnTop;
        AwayBox.SelectedIndex = Math.Max(0, Array.FindIndex(AwayChoices, c => c.Minutes == Settings.AwayDimMinutes));
        AwayLevelBox.SelectedIndex = Math.Max(0, Array.FindIndex(AwayLevels, c => c.Brightness == Settings.AwayBrightness));
        AwayLevelBox.IsEnabled = Settings.AwayDimMinutes > 0;

        var missing = new List<string>();
        if (main is null && (Settings.MonitorId is not null || Settings.MonitorDevice is not null))
            missing.Add($"The main dashboard's screen ({DisplayMonitor.ShortName(Settings.MonitorDevice ?? "?")}) isn't connected, so it's hidden. Pick a screen to move it there.");
        foreach (var extra in Settings.ExtraDashboards.Where(e => e.Resolve(_monitors) is null))
            missing.Add($"The dashboard showing \"{extra.Layout}\" is on {extra.MonitorName(_monitors)}; it shows again when that screen is back.");
        MissingText.Text = string.Join("\n", missing);
        MissingText.Visibility = missing.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        DrawMap();
        ShowSelected();
        _loading = false;
    }

    // ---- The map ------------------------------------------------------------------------------------

    /// <summary>What's on a monitor: the main dashboard, another one, or none.</summary>
    private (bool Main, ExtraDashboard? Extra) DashboardOn(DisplayMonitor monitor) =>
        (DisplayMonitor.Resolve(Settings, _monitors)?.Id == monitor.Id,
         Settings.ExtraDashboards.FirstOrDefault(e => e.MonitorId == monitor.Id));

    private void DrawMap()
    {
        Map.Children.Clear();
        if (_monitors.Count == 0)
            return;

        var left = _monitors.Min(m => m.Bounds.X);
        var top = _monitors.Min(m => m.Bounds.Y);
        var width = _monitors.Max(m => m.Bounds.X + m.Bounds.Width) - left;
        var height = _monitors.Max(m => m.Bounds.Y + m.Bounds.Height) - top;
        var room = Math.Max(200, MapCard.ActualWidth - 42);
        var scale = Math.Min(room / width, Map.Height / height);
        var offset = (room - width * scale) / 2;

        foreach (var monitor in _monitors)
        {
            var (main, extra) = DashboardOn(monitor);
            var b = monitor.Bounds;
            var tile = new RadioButton
            {
                GroupName = "monitors",
                IsChecked = monitor.Id == _selected,
                Tag = main || extra is not null ? "dashboard" : null,
                Width = Math.Max(24, b.Width * scale - 6),
                Height = Math.Max(24, b.Height * scale - 6),
                Content = TileText(monitor, main, extra),
                ToolTip = $"{monitor.DisplayName}\n{Role(main, extra)}",
            };
            tile.Style = (Style)Resources["MonitorTile"];
            AutomationProperties.SetName(tile, $"{monitor.DisplayName}: {Role(main, extra)}");
            Canvas.SetLeft(tile, offset + (b.X - left) * scale + 3);
            Canvas.SetTop(tile, (b.Y - top) * scale + 3);
            tile.Checked += (_, _) =>
            {
                if (_selected == monitor.Id)
                    return;
                _selected = monitor.Id;
                ShowSelected();
            };
            Map.Children.Add(tile);
        }
    }

    private static string Role(bool main, ExtraDashboard? extra) =>
        main ? "Main dashboard" : extra is not null ? $"Dashboard: {extra.Layout}" : "No dashboard";

    private object TileText(DisplayMonitor monitor, bool main, ExtraDashboard? extra)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(new TextBlock
        {
            Text = monitor.Name ?? monitor.ShortDevice,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var layout = main ? $"Main · {_host.Layouts.Active}" : extra?.Layout;
        if (layout is not null)
            panel.Children.Add(SettingsWindow.Styled(new TextBlock { Text = layout, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }, "NoteText"));
        // A small screen beside big ones gets a small tile: its words shrink to fit rather than being cut off.
        return new Viewbox { Child = panel, StretchDirection = StretchDirection.DownOnly };
    }

    // ---- The screen picked --------------------------------------------------------------------------

    private void ShowSelected()
    {
        ScreenCards.Children.Clear();
        if (_monitors.FirstOrDefault(m => m.Id == _selected) is not { } monitor)
        {
            ScreenHeading.Text = string.Empty;
            return;
        }

        ScreenHeading.Text = monitor.DisplayName;
        var (main, extra) = DashboardOn(monitor);
        var layouts = _host.Layouts.List();
        if (main)
            AddMainCards(layouts);
        else if (extra is not null)
            AddExtraCards(monitor, extra, layouts);
        else
            AddEmptyCards(monitor);
    }

    private void AddMainCards(IReadOnlyList<string> layouts)
    {
        var choose = Ui.Choice(layouts.Select(l => (l, l)), _host.Layouts.Active, "Layout", name =>
        {
            _host.ShowOnMain(name);
            Load();
        });
        ScreenCards.Children.Add(Ui.Card("", "Layout",
            "The main dashboard. Pages, the schedule and game layouts can show other layouts on it for a while.",
            Ui.Row(choose, Ui.Button("Edit", () => _window.EditScreen(null), ""))));

        var options = _host.WindowOptions;
        ScreenCards.Children.Add(Ui.Card("", "Size", "A window can be moved and resized, and share the screen with other windows.",
            Ui.Choice([(false, "Fill the screen"), (true, "A window")], options.Windowed, "Size", windowed => Change(options with { Windowed = windowed }))));
        if (!options.Windowed)
            return;

        ScreenCards.Children.Add(Ui.Card("", "Lock position", "Unlocked, drag the dashboard to move it and drag its edges to resize it",
            Ui.Switch(options.Locked, "Lock position", locked => Change(options with { Locked = locked }))));
        var clickThrough = Ui.Card("", "Click-through when locked", "Clicks go to the window underneath. Unlock it here to move it again.",
            Ui.Switch(options.ClickThrough, "Click-through when locked", on => Change(options with { ClickThrough = on })));
        clickThrough.IsEnabled = options.Locked;
        ScreenCards.Children.Add(clickThrough);
        ScreenCards.Children.Add(Ui.Card("", "Reset size and position", "Back to the layout's shape, in the middle of the screen",
            Ui.Button("Reset", () =>
            {
                _host.ResetWindow();
                Load();
            })));
    }

    private void AddExtraCards(DisplayMonitor monitor, ExtraDashboard extra, IReadOnlyList<string> layouts)
    {
        var choose = Ui.Choice(layouts.Select(l => (l, l)), layouts.FirstOrDefault(l => string.Equals(l, extra.Layout, StringComparison.OrdinalIgnoreCase)) ?? extra.Layout, "Layout",
            name => ChangeExtras(list => list.Select(e => e.MonitorId == extra.MonitorId ? e with { Layout = name } : e).ToList()));
        ScreenCards.Children.Add(Ui.Card("", "Layout", "Pages, the schedule and game layouts are the main dashboard's; this one always shows its own layout.",
            Ui.Row(choose, Ui.Button("Edit", () => _window.EditScreen(monitor.Id), ""))));
        ScreenCards.Children.Add(Ui.Card("", "Remove this dashboard", "Its layout stays in your layouts, to show here again or elsewhere.",
            Ui.Button("Remove", () => ChangeExtras(list => list.Where(e => e.MonitorId != extra.MonitorId).ToList()))));
    }

    private void AddEmptyCards(DisplayMonitor monitor)
    {
        ScreenCards.Children.Add(Ui.Card("", "Add a dashboard",
            "A dashboard filling this screen, with a new, empty layout of its own made to its size. It opens in the editor to fill.",
            Ui.Button("Add", () => _host.AddDashboard(monitor), accent: true)));
        ScreenCards.Children.Add(Ui.Card("", "Move the main dashboard here",
            "The main dashboard leaves its screen and shows on this one instead.",
            Ui.Button("Move here", () =>
            {
                _host.MoveMainTo(monitor);
                Load();
            })));
    }

    private void Change(WindowOptions options)
    {
        if (_loading)
            return;
        _host.SetWindowOptions(options);
        Load();
    }

    private void ChangeExtras(Func<List<ExtraDashboard>, List<ExtraDashboard>> change)
    {
        if (_loading)
            return;
        _host.SetExtraDashboards(change(Settings.ExtraDashboards.ToList()));
        Load();
    }

    // ---- Every dashboard ----------------------------------------------------------------------------

    private void OnOnTop(object sender, RoutedEventArgs e) => Change(_host.WindowOptions with { AlwaysOnTop = OnTopSwitch.IsChecked == true });

    private void OnFullscreen(object sender, RoutedEventArgs e) => Change(_host.WindowOptions with { HideOverFullscreen = FullscreenSwitch.IsChecked == true });

    private void OnAway(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AwayBox.SelectedIndex < 0 || AwayLevelBox.SelectedIndex < 0)
            return;
        _host.SetAway(AwayChoices[AwayBox.SelectedIndex].Minutes, AwayLevels[AwayLevelBox.SelectedIndex].Brightness);
        AwayLevelBox.IsEnabled = AwayChoices[AwayBox.SelectedIndex].Minutes > 0;
    }

    private void OnBringToFront(object sender, RoutedEventArgs e) => _host.BringDashboardsForward();
}
