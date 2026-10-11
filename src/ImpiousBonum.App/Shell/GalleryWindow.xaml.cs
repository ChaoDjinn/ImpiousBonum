using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using ImpiousBonum.App.Editor;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Shell;

/// <summary>Tray → Layout → Gallery…: browse shared layouts with a live preview, and import one.</summary>
public partial class GalleryWindow : Window
{
    private readonly string _address;
    private readonly LayoutStore _layouts;
    private readonly MetricStore? _store;
    private readonly Dictionary<GalleryEntry, LayoutPackageContents> _downloaded = new();
    private readonly DashboardView _preview = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? _loading;

    internal GalleryWindow(string address, LayoutStore layouts, MetricStore? store)
    {
        InitializeComponent();
        _address = address;
        _layouts = layouts;
        _store = store;
        PreviewBox.Child = _preview;
        _tick.Tick += (_, _) => RefreshPreview();
        Loaded += async (_, _) => await LoadListAsync();
        Closed += (_, _) =>
        {
            _tick.Stop();
            _loading?.Cancel();
        };
    }

    /// <summary>The layout imported and its saved name, or null if none was.</summary>
    public string? Imported { get; private set; }

    private async Task LoadListAsync()
    {
        ShowPreviewMessage("Loading the gallery…");
        EntryList.ItemsSource = null;
        try
        {
            var entries = await Gallery.ListAsync(_address);
            EntryList.ItemsSource = entries;
            ShowPreviewMessage(entries.Count == 0 ? "The gallery is empty." : "Pick a layout to see it.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or System.Text.Json.JsonException or UriFormatException)
        {
            ShowPreviewMessage($"Couldn't reach the gallery: {Message(ex)}");
        }
    }

    private async void OnSelected(object sender, RoutedEventArgs e)
    {
        ImportButton.IsEnabled = false;
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
                package = await Gallery.DownloadAsync(_address, entry, token);
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

        ShowPreview(package);
        ImportButton.IsEnabled = true;
    }

    /// <summary>Draws the layout as the dashboard would, with this PC's readings, refreshed every second.</summary>
    internal void ShowPreview(LayoutPackageContents package)
    {
        _preview.Build(package.Layout, _layouts.Themes.Resolve(package.Layout.Theme));
        PreviewText.Visibility = Visibility.Collapsed;
        PreviewBox.Visibility = Visibility.Visible;
        RefreshPreview();
        _tick.Start();
    }

    private void RefreshPreview()
    {
        if (_store is not null && PreviewBox.Visibility == Visibility.Visible)
            _preview.Refresh(_store, DateTime.Now);
    }

    private void ShowPreviewMessage(string message)
    {
        _tick.Stop();
        PreviewBox.Visibility = Visibility.Collapsed;
        PreviewText.Visibility = Visibility.Visible;
        PreviewText.Text = message;
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        if (EntryList.SelectedItem is not GalleryEntry entry || !_downloaded.TryGetValue(entry, out var package))
            return;
        if (LayoutTransfer.ImportPackage(this, _layouts, package) is not { } name)
            return;
        Imported = name;
        DialogResult = true;
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        _downloaded.Clear();
        await LoadListAsync();
    }

    private static string Message(Exception ex) => ex is TaskCanceledException ? "it took too long to answer." : ex.Message;
}
