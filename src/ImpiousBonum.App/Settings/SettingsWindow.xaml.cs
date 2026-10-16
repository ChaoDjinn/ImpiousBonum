using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using ImpiousBonum.App.Editor;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Settings;

/// <summary>
/// The app's window: a sidebar of pages, as in Windows 11's Settings, with the layout editor as the first. Everything
/// the tray menu used to hold is here, and changes take effect as they're made. Only the layout editor has unsaved
/// changes, which it asks about before another page (or closing) takes its place.
/// </summary>
public partial class SettingsWindow : Window
{
    private const double NavWidth = 256;
    private const double CollapsedNavWidth = 56;
    /// <summary>Narrower than this, the sidebar shows only icons, leaving the room to the page.</summary>
    private const double CollapseBelowWidth = 1180;

    private readonly ISettingsHost _host;
    private readonly Dictionary<SettingsPage, FrameworkElement> _pages = [];
    private readonly Dictionary<SettingsPage, ListBoxItem> _navItems = [];
    private readonly List<TextBlock> _navLabels = [];
    private readonly Ellipse _updateDot;
    private LayoutEditor? _editor;
    private string? _screenToEdit;
    private bool _syncingNav;
    private bool _collapsedByUser;

    internal SettingsWindow(ISettingsHost host)
    {
        InitializeComponent();
        _host = host;
        PlaceOnMainScreen();
        Logo.Source = AboutPage.LoadLogo();
        _updateDot = new Ellipse
        {
            Width = 8,
            Height = 8,
            Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "An update is ready",
        };
        _updateDot.SetResourceReference(Shape.FillProperty, "AccentFillColorDefaultBrush");
        BuildNav();
        SizeChanged += (_, _) => UpdateCollapse();
    }

    /// <summary>The page showing, if any yet.</summary>
    public SettingsPage? Current { get; private set; }

    /// <summary>The layout editor, while the Layouts page shows.</summary>
    public LayoutEditor? Editor => _editor;

    /// <summary>
    /// Centred on the main screen and no bigger than it. WPF's CenterScreen uses the screen under the mouse, which when
    /// the window is opened from the dashboard is the dashboard's own screen: often a strip too short for it, which it
    /// then hung off, covering the dashboard being edited anyway.
    /// </summary>
    private void PlaceOnMainScreen()
    {
        var (left, top, width, height) = Centred(SystemParameters.WorkArea, Width, Height);
        (Left, Top, Width, Height) = (left, top, width, height);
    }

    /// <summary>A window of <paramref name="width"/>×<paramref name="height"/> centred in <paramref name="area"/>, shrunk to fit inside it.</summary>
    internal static (double Left, double Top, double Width, double Height) Centred(Rect area, double width, double height)
    {
        width = Math.Min(width, area.Width * 0.95);
        height = Math.Min(height, area.Height * 0.95);
        return (area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2, width, height);
    }

    // ---- Navigation ---------------------------------------------------------------------------------

    /// <summary>Shows a page, leaving the one showing first (the layout editor asks about unsaved changes). False if it stayed.</summary>
    public bool ShowPage(SettingsPage page)
    {
        if (Current == page)
        {
            SelectNav(page);
            return true;
        }
        if (!Leave())
        {
            SelectNav(Current);
            return false;
        }

        Current = page;
        SelectNav(page);
        if (page == SettingsPage.Layouts)
        {
            _editor = _host.BeginEditing(_screenToEdit);
            _editor.StateChanged += (_, _) => UpdateTitle();
            _editor.GalleryRequested += (_, _) => ShowGallery(_host.EditedMonitorId);
            if (_host.Store is { } store)
                _editor.Refresh(store, DateTime.Now);
            PageHost.Content = _editor;
        }
        else
        {
            var content = PageFor(page);
            ((IPage)content).Load();
            PageHost.Content = content;
        }
        _screenToEdit = null;
        UpdateTitle();
        return true;
    }

    /// <summary>The Layouts page, on the layout of one dashboard: the main one (null) or another, by its monitor's id.</summary>
    public bool EditScreen(string? screen)
    {
        _screenToEdit = screen;
        return ShowPage(SettingsPage.Layouts);
    }

    /// <summary>The Gallery page, for layouts that suit one monitor's screen (by id), or every screen (null).</summary>
    public bool ShowGallery(string? monitorId)
    {
        ((GalleryPage)PageFor(SettingsPage.Gallery)).ForMonitor(monitorId);
        return ShowPage(SettingsPage.Gallery);
    }

    /// <summary>The page showing reads the settings again, after something outside the window changed them.</summary>
    public void Reload()
    {
        if (Current is { } page && page != SettingsPage.Layouts && _pages.TryGetValue(page, out var content))
            ((IPage)content).Load();
    }

    private bool Leave()
    {
        switch (Current)
        {
            case null:
                return true;
            case SettingsPage.Layouts:
                if (_editor is not null && !_editor.ConfirmLeave())
                    return false;
                _editor = null;
                PageHost.Content = null;
                _host.EndEditing();
                return true;
            default:
                var page = (IPage)_pages[Current.Value];
                if (!page.CanLeave())
                    return false;
                page.Left();
                return true;
        }
    }

    private FrameworkElement PageFor(SettingsPage page)
    {
        if (_pages.TryGetValue(page, out var existing))
            return existing;
        FrameworkElement created = page switch
        {
            SettingsPage.Gallery => new GalleryPage(_host, this),
            SettingsPage.Screens => new ScreensPage(_host, this),
            SettingsPage.TabletAndUsb => new TabletAndUsbPage(_host),
            SettingsPage.PagesAndSchedule => new PagesAndSchedulePage(_host),
            SettingsPage.GameLayouts => new GameLayoutsPage(_host),
            SettingsPage.Sensors => new SensorsPage(_host),
            SettingsPage.Energy => new EnergyPage(_host),
            SettingsPage.Weather => new WeatherPage(_host),
            SettingsPage.Calendars => LinkListPage.ForCalendars(_host),
            SettingsPage.NewsFeeds => LinkListPage.ForNewsFeeds(_host),
            SettingsPage.ClaudeUsage => new ClaudeUsagePage(_host),
            SettingsPage.CustomData => new CustomDataPage(_host),
            SettingsPage.StatusChecks => new StatusChecksPage(_host),
            SettingsPage.Alerts => new AlertsPage(_host),
            SettingsPage.Hotkeys => new HotkeysPage(_host),
            SettingsPage.Logging => new LoggingPage(_host),
            SettingsPage.General => new GeneralPage(_host),
            SettingsPage.About => new AboutPage(_host),
            _ => throw new ArgumentOutOfRangeException(nameof(page), page, null),
        };
        _pages[page] = created;
        return created;
    }

    private void BuildNav()
    {
        foreach (var info in PageInfo.All)
        {
            if (info.StartsGroup)
                Nav.Items.Add(Styled(new ListBoxItem(), "NavSeparator"));

            var label = new TextBlock { Text = info.Title, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            _navLabels.Add(label);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Styled(new TextBlock { Text = info.Glyph, FontSize = 16, Width = 20, TextAlignment = TextAlignment.Center }, "Glyph"));
            row.Children.Add(label);
            if (info.Page == SettingsPage.About)
                row.Children.Add(_updateDot);

            var item = Styled(new ListBoxItem { Content = row, Tag = info.Page, ToolTip = info.Title }, "NavItem");
            System.Windows.Automation.AutomationProperties.SetName(item, info.Title);
            _navItems[info.Page] = item;
            Nav.Items.Add(item);
        }
    }

    /// <summary>Gives <paramref name="element"/> one of the shared styles in Styles.xaml.</summary>
    internal static T Styled<T>(T element, string style) where T : FrameworkElement
    {
        element.SetResourceReference(StyleProperty, style);
        return element;
    }

    private void SelectNav(SettingsPage? page)
    {
        _syncingNav = true;
        Nav.SelectedItem = page is { } p ? _navItems[p] : null;
        _syncingNav = false;
    }

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingNav && Nav.SelectedItem is ListBoxItem { Tag: SettingsPage page })
            // After the click has finished, so a "save your changes?" box doesn't fight the list for the mouse.
            Dispatcher.BeginInvoke(() => ShowPage(page));
    }

    private void OnCollapse(object sender, RoutedEventArgs e)
    {
        _collapsedByUser = !_collapsedByUser;
        UpdateCollapse();
    }

    /// <summary>Icons only when asked, or when the window is too narrow for the page and the names.</summary>
    private void UpdateCollapse()
    {
        var collapsed = _collapsedByUser || ActualWidth < CollapseBelowWidth;
        NavColumn.Width = new GridLength(collapsed ? CollapsedNavWidth : NavWidth);
        var labels = collapsed ? Visibility.Collapsed : Visibility.Visible;
        foreach (var label in _navLabels)
            label.Visibility = labels;
        AppName.Visibility = Logo.Visibility = labels;
        CollapseButton.ToolTip = collapsed ? "Show the page names" : "Hide the page names";
    }

    // ---- What the app tells it ----------------------------------------------------------------------

    /// <summary>Each second: the editor's live preview, or the page's statuses.</summary>
    public void Refresh(MetricStore store, DateTime now)
    {
        if (_editor is not null)
            _editor.Refresh(store, now);
        else if (Current is { } page && _pages.TryGetValue(page, out var content))
            ((IPage)content).Refresh();
        _updateDot.Visibility = _host.Updates.Ready is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>A saved theme changed on disk; the editor redraws with it.</summary>
    public void RefreshTheme() => _editor?.RefreshTheme();

    private void UpdateTitle() =>
        Title = _editor is not null ? _editor.Title : $"{PageInfo.Of(Current ?? SettingsPage.Layouts).Title} · Impious Bonum";

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
            return;
        if (!Leave())
        {
            e.Cancel = true;
            return;
        }
        Current = null;
    }
}
