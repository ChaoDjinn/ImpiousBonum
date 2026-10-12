using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using ImpiousBonum.App.Editor;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Settings;

/// <summary>
/// Gallery: browse shared layouts with a live preview, filtered to those that suit one of your screens and by how much
/// they show at once, and put one straight on that screen (adding a dashboard there if it has none).
/// </summary>
public partial class GalleryPage : UserControl, IPage
{
    internal static readonly (GalleryDetail Detail, string Label)[] Details =
    [
        (GalleryDetail.Any, "Any amount"),
        (GalleryDetail.Few, $"A few big things (up to {GalleryFit.FewWidgets} widgets)"),
        (GalleryDetail.Lots, $"Lots at once ({GalleryFit.FewWidgets + 1} or more)"),
    ];

    private readonly ISettingsHost _host;
    private readonly SettingsWindow _window;
    private readonly Dictionary<GalleryEntry, LayoutPackageContents> _downloaded = new();
    private readonly DashboardView _preview = new() { AnimateBackground = false };
    private IReadOnlyList<GalleryEntry> _entries = [];
    private IReadOnlyList<DisplayMonitor> _monitors = [];
    private CancellationTokenSource? _loading;
    private bool _listed;
    private bool _filling;
    private string? _forMonitor;
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
    }

    /// <summary>The next time it's shown, it's for this monitor's screen (by id), as when the editor asks for a layout for its screen.</summary>
    public void ForMonitor(string? monitorId) => _forMonitor = monitorId;

    public async void Load()
    {
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
            _entries = await Gallery.ListAsync(_host.Settings.GalleryAddress ?? Gallery.DefaultAddress);
            _listed = true;
            Filter();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or System.Text.Json.JsonException or UriFormatException or InvalidDataException)
        {
            ShowPreviewMessage($"Couldn't reach the gallery: {Message(ex)}");
        }
    }

    /// <summary>The layouts for the screen and amount picked, closest in size to the screen first.</summary>
    private void Filter()
    {
        var screen = Selected is { } monitor ? (monitor.Bounds.Width, monitor.Bounds.Height) : ((int, int)?)null;
        var detail = Details[Math.Max(0, DetailBox.SelectedIndex)].Detail;
        var shown = GalleryFit.For(_entries, screen, detail);
        var selected = EntryList.SelectedItem as GalleryEntry;
        EntryList.ItemsSource = shown;
        if (selected is not null && shown.Contains(selected))
            EntryList.SelectedItem = selected;
        else if (shown.Count == 0)
            ShowPreviewMessage(_entries.Count == 0 ? "The gallery is empty."
                : screen is { } s ? $"Nothing in the gallery suits a {s.Item1}×{s.Item2} screen{(detail == GalleryDetail.Any ? "" : " with that much on it")} yet. Pick \"Any screen\" to see everything, then make it fit in the editor."
                : "Nothing in the gallery shows that much at once yet.");
        else
            ShowPreviewMessage("Pick a layout to see it.");
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || !IsInitialized)
            return;
        UpdateUseButton();
        if (_listed)
            Filter();
    }

    private void UpdateUseButton()
    {
        UseText.Text = Selected is { } monitor
            ? (DisplayMonitor.Resolve(_host.Settings, _monitors)?.Id == monitor.Id || _host.Settings.ExtraDashboards.Any(e => e.MonitorId == monitor.Id)
                ? $"Show on {monitor.Name ?? monitor.ShortDevice}"
                : $"Add a dashboard on {monitor.Name ?? monitor.ShortDevice}")
            : "Add to my layouts";
    }

    private async void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        UseButton.IsEnabled = false;
        if (EntryList.SelectedItem is not GalleryEntry entry)
            return;
        _loading?.Cancel();
        _loading = new CancellationTokenSource();
        var token = _loading.Token;

        if (!_downloaded.TryGetValue(entry, out var package))
        {
            ShowPreviewMessage($"Downloading {entry.Name}…");
            try
            {
                package = await Gallery.DownloadAsync(_host.Settings.GalleryAddress ?? Gallery.DefaultAddress, entry, token);
                _downloaded[entry] = package;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or UriFormatException)
            {
                ShowPreviewMessage($"Couldn't get {entry.Name}: {Message(ex)}");
                return;
            }
        }
        if (token.IsCancellationRequested || !ReferenceEquals(EntryList.SelectedItem, entry))
            return;

        _preview.Build(package.Layout, _host.Layouts.Themes.Resolve(package.Layout.Theme));
        PreviewText.Visibility = Visibility.Collapsed;
        PreviewBox.Visibility = Visibility.Visible;
        Refresh();
        UseButton.IsEnabled = true;
    }

    /// <summary>Adds the layout to yours and, with a screen picked, shows it there.</summary>
    private void OnUse(object sender, RoutedEventArgs e)
    {
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

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (_used.Shown)
            _window.EditScreen(_used.Screen);
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        _downloaded.Clear();
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
