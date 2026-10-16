using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ImpiousBonum.App.Editor;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Settings;

/// <summary>
/// Gallery: browse shared layouts with a live preview, filtered to those that suit one of your screens and by how much
/// they show at once, and put one straight on that screen (adding a dashboard there if it has none). Its Themes tab
/// lists shared themes, each shown on the default layout, to add to your themes.
/// </summary>
public partial class GalleryPage : UserControl, IPage
{
    internal static readonly (GalleryDetail Detail, string Label)[] Details =
    [
        (GalleryDetail.Any, "Any amount"),
        (GalleryDetail.Few, $"A few big things (up to {GalleryFit.FewWidgets} widgets)"),
        (GalleryDetail.Lots, $"Lots at once ({GalleryFit.FewWidgets + 1} or more)"),
    ];

    private const string LayoutsIntro =
        "Ready-made layouts to start from. Pick the screen you want one for to see those that suit its shape. The previews show your own readings.";

    private const string ThemesIntro =
        "Fonts and colours other people have made, for any of your layouts. Each is shown on the default layout, with your own readings.";

    private readonly ISettingsHost _host;
    private readonly SettingsWindow _window;
    private readonly Dictionary<GalleryEntry, LayoutPackageContents> _downloaded = new();
    private readonly Dictionary<GalleryTheme, JsonObject> _downloadedThemes = new();
    private readonly DashboardView _preview = new() { AnimateBackground = false };
    private IReadOnlyList<GalleryEntry> _entries = [];
    private IReadOnlyList<GalleryTheme> _themes = [];
    private IReadOnlyList<DisplayMonitor> _monitors = [];
    private CancellationTokenSource? _loading;
    private bool _listed;
    private bool _filling;
    private string? _forMonitor;
    private bool _forLayouts;
    /// <summary>Where "Edit it" goes after a layout was put on a screen: that dashboard (null for the main one).</summary>
    private (bool Shown, string? Screen) _used;

    internal GalleryPage(ISettingsHost host, SettingsWindow window)
    {
        InitializeComponent();
        _host = host;
        _window = window;
        PreviewBox.Child = _preview;
        DetailBox.ItemsSource = Details.Select(d => d.Label).ToList();
        DetailBox.SelectedIndex = 0;
        ShowKind();
    }

    /// <summary>
    /// The next time it's shown, it's for a layout for this monitor's screen (by id), as when the editor asks for a
    /// layout for its screen.
    /// </summary>
    public void ForMonitor(string? monitorId)
    {
        _forMonitor = monitorId;
        _forLayouts = true;
    }

    /// <summary>True while the Themes tab is picked.</summary>
    private bool ShowingThemes => ThemesTab.IsChecked == true;

    public async void Load()
    {
        if (_forLayouts)
            LayoutsTab.IsChecked = true;
        _forLayouts = false;
        _filling = true;
        var keep = _forMonitor ?? Selected?.Id;
        _monitors = DisplayMonitor.GetAll();
        ScreenBox.ItemsSource = _monitors.Select(Describe).Prepend("Any screen").ToList();
        ScreenBox.SelectedIndex = keep is null ? 0 : _monitors.ToList().FindIndex(m => m.Id == keep) + 1;
        _forMonitor = null;
        _filling = false;
        UpdateUseButton();

        if (!_listed)
            await LoadListAsync();
        else
            Filter();
    }

    public void Refresh()
    {
        if (_host.Store is { } store && PreviewBox.Visibility == Visibility.Visible)
            _preview.Refresh(store, DateTime.Now);
    }

    public void Left() => _loading?.Cancel();

    /// <summary>"DELL U2720Q · 2560×1440 · main dashboard"</summary>
    private string Describe(DisplayMonitor monitor)
    {
        var main = DisplayMonitor.Resolve(_host.Settings, _monitors)?.Id == monitor.Id;
        var extra = _host.Settings.ExtraDashboards.FirstOrDefault(e => e.MonitorId == monitor.Id);
        var role = main ? "main dashboard" : extra is not null ? $"dashboard: {extra.Layout}" : "no dashboard yet";
        return $"{monitor.Name ?? monitor.ShortDevice} · {monitor.Bounds.Width}×{monitor.Bounds.Height} · {role}";
    }

    private DisplayMonitor? Selected => ScreenBox.SelectedIndex > 0 && ScreenBox.SelectedIndex <= _monitors.Count ? _monitors[ScreenBox.SelectedIndex - 1] : null;

    private async Task LoadListAsync()
    {
        ShowPreviewMessage("Loading the gallery…");
        EntryList.ItemsSource = null;
        try
        {
            var index = await Gallery.ListAsync(_host.Settings.GalleryAddress ?? Gallery.DefaultAddress);
            _entries = index.Layouts;
            _themes = index.Themes;
            _listed = true;
            Filter();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or System.Text.Json.JsonException or UriFormatException or InvalidDataException)
        {
            ShowPreviewMessage($"Couldn't reach the gallery: {Message(ex)}");
        }
    }

    /// <summary>The themes, or the layouts for the screen and amount picked, closest in size to the screen first.</summary>
    private void Filter()
    {
        if (ShowingThemes)
        {
            List(_themes, _themes.Count == 0 ? "The gallery has no themes yet." : "Pick a theme to see it.");
            return;
        }

        var screen = Selected is { } monitor ? (monitor.Bounds.Width, monitor.Bounds.Height) : ((int, int)?)null;
        var detail = Details[Math.Max(0, DetailBox.SelectedIndex)].Detail;
        var shown = GalleryFit.For(_entries, screen, detail);
        List(shown, shown.Count > 0 ? "Pick a layout to see it."
            : _entries.Count == 0 ? "The gallery is empty."
            : screen is { } s ? $"Nothing in the gallery suits a {s.Item1}×{s.Item2} screen{(detail == GalleryDetail.Any ? "" : " with that much on it")} yet. Pick \"Any screen\" to see everything, then make it fit in the editor."
            : "Nothing in the gallery shows that much at once yet.");
    }

    /// <summary>Lists <paramref name="items"/>, keeping the one picked if it's still there; otherwise the preview says <paramref name="message"/>.</summary>
    private void List<T>(IReadOnlyList<T> items, string message)
    {
        var selected = EntryList.SelectedItem;
        EntryList.ItemsSource = items;
        if (selected is T kept && items.Contains(kept))
            EntryList.SelectedItem = kept;
        else
            ShowPreviewMessage(message);
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || !IsInitialized)
            return;
        UpdateUseButton();
        if (_listed)
            Filter();
    }

    private void OnKindChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized)
            return;
        ShowKind();
        UpdateUseButton();
        if (_listed)
            Filter();
    }

    /// <summary>The page as it is for layouts or for themes: only layouts are picked by screen and how much they show.</summary>
    private void ShowKind()
    {
        var themes = ShowingThemes;
        IntroText.Text = themes ? ThemesIntro : LayoutsIntro;
        LayoutFilters.Visibility = themes ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(EntryList, themes ? "Themes" : "Layouts");
        StatusText.Text = themes ? "Once a theme's added, any layout can use it." : "Pictures and fonts in a layout show once it's added.";
        EditButton.Visibility = Visibility.Collapsed;
    }

    private void UpdateUseButton()
    {
        UseText.Text = ShowingThemes ? "Add to my themes"
            : Selected is { } monitor
            ? (DisplayMonitor.Resolve(_host.Settings, _monitors)?.Id == monitor.Id || _host.Settings.ExtraDashboards.Any(e => e.MonitorId == monitor.Id)
                ? $"Show on {monitor.Name ?? monitor.ShortDevice}"
                : $"Add a dashboard on {monitor.Name ?? monitor.ShortDevice}")
            : "Add to my layouts";
    }

    private async void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        UseButton.IsEnabled = false;
        if (EntryList.SelectedItem is not { } item)
            return;
        _loading?.Cancel();
        _loading = new CancellationTokenSource();
        var token = _loading.Token;

        LayoutDocument? layout = null;
        if (item is GalleryTheme theme && await DownloadOnceAsync(theme, theme.Name, _downloadedThemes, Gallery.DownloadThemeAsync, token) is { } values)
        {
            // A theme is shown on the default layout, as the editor would draw it.
            layout = LayoutLibrary.LoadDefault();
            layout.Theme = values.DeepClone().AsObject();
        }
        else if (item is GalleryEntry entry && await DownloadOnceAsync(entry, entry.Name, _downloaded, Gallery.DownloadAsync, token) is { } package)
            layout = package.Layout;
        if (layout is null || token.IsCancellationRequested || !ReferenceEquals(EntryList.SelectedItem, item))
            return;

        _preview.Build(layout, _host.Layouts.Themes.Resolve(layout.Theme));
        PreviewText.Visibility = Visibility.Collapsed;
        PreviewBox.Visibility = Visibility.Visible;
        Refresh();
        UseButton.IsEnabled = true;
    }

    /// <summary>
    /// An entry's layout or theme, downloaded the first time it's picked. Null if it couldn't be (the preview says why)
    /// or something else was picked meanwhile.
    /// </summary>
    private async Task<T?> DownloadOnceAsync<TEntry, T>(TEntry entry, string name, Dictionary<TEntry, T> downloaded,
        Func<string, TEntry, CancellationToken, Task<T>> download, CancellationToken token)
        where TEntry : notnull
        where T : class
    {
        if (downloaded.TryGetValue(entry, out var known))
            return known;
        ShowPreviewMessage($"Downloading {name}…");
        try
        {
            return downloaded[entry] = await download(_host.Settings.GalleryAddress ?? Gallery.DefaultAddress, entry, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or UriFormatException)
        {
            ShowPreviewMessage($"Couldn't get {name}: {Message(ex)}");
            return null;
        }
    }

    /// <summary>Adds the layout or theme to yours and, with a layout and a screen picked, shows the layout there.</summary>
    private void OnUse(object sender, RoutedEventArgs e)
    {
        if (EntryList.SelectedItem is GalleryTheme theme)
        {
            AddTheme(theme);
            return;
        }
        if (EntryList.SelectedItem is not GalleryEntry entry || !_downloaded.TryGetValue(entry, out var package))
            return;
        if (LayoutTransfer.ImportPackage(Window.GetWindow(this), _host.Layouts, package) is not { } name)
            return;
        AppLog.Info($"Added layout \"{name}\" from the gallery");

        _used = (false, null);
        if (Selected is not { } monitor)
        {
            StatusText.Text = $"\"{name}\" is in your layouts. Show it on a screen from the Screens or Layouts page.";
        }
        else if (DisplayMonitor.Resolve(_host.Settings, _monitors)?.Id == monitor.Id)
        {
            _host.ShowOnMain(name);
            _used = (true, null);
        }
        else if (_host.Settings.ExtraDashboards.Any(d => d.MonitorId == monitor.Id))
        {
            _host.SetExtraDashboards(_host.Settings.ExtraDashboards.Select(d => d.MonitorId == monitor.Id ? d with { Layout = name } : d).ToList());
            _used = (true, monitor.Id);
        }
        else
        {
            _host.AddDashboard(monitor, name);
            _used = (true, monitor.Id);
        }

        if (_used.Shown && Selected is { } shownOn)
            StatusText.Text = $"Showing \"{name}\" on {shownOn.Name ?? shownOn.ShortDevice}.";
        EditButton.Visibility = _used.Shown ? Visibility.Visible : Visibility.Collapsed;
        Load();
    }

    /// <summary>Adds a theme to your themes, for any layout to use.</summary>
    private void AddTheme(GalleryTheme theme)
    {
        if (!_downloadedThemes.TryGetValue(theme, out var values)
            || LayoutTransfer.AddTheme(Window.GetWindow(this), _host.Layouts.Themes, theme.Name, values) is not { } name)
            return;
        AppLog.Info($"Added theme \"{name}\" from the gallery");
        StatusText.Text = $"\"{name}\" is in your themes. To use it, click empty canvas in the editor and pick it under Theme.";
    }

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (_used.Shown)
            _window.EditScreen(_used.Screen);
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        _downloaded.Clear();
        _downloadedThemes.Clear();
        _listed = false;
        await LoadListAsync();
    }

    private void ShowPreviewMessage(string message)
    {
        PreviewBox.Visibility = Visibility.Collapsed;
        PreviewText.Visibility = Visibility.Visible;
        PreviewText.Text = message;
        UseButton.IsEnabled = false;
    }

    private static string Message(Exception ex) => ex is TaskCanceledException ? "it took too long to answer." : ex.Message;
}
