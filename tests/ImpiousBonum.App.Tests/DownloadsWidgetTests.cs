using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Games;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Tests;

public sealed class DownloadsWidgetTests
{
    private const long Gigabyte = 1L << 30;
    private const long Megabyte = 1L << 20;

    private static readonly Theme Plain = Theme.From(new ThemeSettings());

    private static readonly SteamDownload Tunic = new(2, "Tunic", SteamDownloadState.Downloading, 6 * Gigabyte, 15 * Gigabyte) { Speed = 40 * Megabyte };
    private static readonly SteamDownload Abzu = new(4, "Abzu", SteamDownloadState.Paused, Gigabyte, 4 * Gigabyte);
    private static readonly SteamDownload Celeste = new(3, "Celeste", SteamDownloadState.Queued, 0, 2 * Gigabyte);

    [Fact]
    public void Each_download_has_a_row_with_how_far_it_has_got_and_a_bar_dimmed_while_it_waits()
    {
        Sta.Run(() => InBritishEnglish(() =>
        {
            var store = Store(Tunic, Abzu, Celeste);
            var widget = WidgetFactory.Create(new JsonObject { ["type"] = "downloads" }, Plain);
            widget.Refresh(store, DateTime.Now);

            var rows = Rows(widget);
            Assert.Equal(["Tunic", "Abzu", "Celeste"], rows.Select(r => Texts(r)[0]));
            Assert.Equal(["6.0 / 15.0 GB · 40.0 MB/s · 3m", "Paused · 1.0 / 4.0 GB", "Queued · 2.0 GB"], rows.Select(r => Texts(r)[1]));
            Assert.Equal(40, Bar(rows[0]).ColumnDefinitions[0].Width.Value);
            Assert.Equal(60, Bar(rows[0]).ColumnDefinitions[1].Width.Value);
            Assert.Equal([Plain.Accent, Plain.Secondary, Plain.Secondary], rows.Select(r => Fill(r).Background));
        }));
    }

    [Fact]
    public void Rows_follow_steam_s_downloads_and_the_widget_hides_without_any()
    {
        Sta.Run(() => InBritishEnglish(() =>
        {
            var store = Store(Tunic);
            var widget = WidgetFactory.Create(new JsonObject { ["type"] = "downloads" }, Plain);
            widget.Refresh(store, DateTime.Now);
            Assert.Equal(["Tunic"], Rows(widget).Select(r => Texts(r)[0]));
            Assert.Equal(Visibility.Visible, widget.Visibility);

            // Further along: the same row, updated.
            var row = Rows(widget)[0];
            SteamDownloadsProvider.Publish(store, [Tunic with { Done = 9 * Gigabyte }]);
            widget.Refresh(store, DateTime.Now);
            Assert.Same(row, Rows(widget)[0]);
            Assert.Equal("9.0 / 15.0 GB · 40.0 MB/s · 2m", Texts(row)[1]);

            // Another queued behind it.
            SteamDownloadsProvider.Publish(store, [Tunic, Celeste]);
            widget.Refresh(store, DateTime.Now);
            Assert.Equal(["Tunic", "Celeste"], Rows(widget).Select(r => Texts(r)[0]));

            // All done: nothing shows, but it's there to place while editing.
            SteamDownloadsProvider.Publish(store, []);
            widget.Refresh(store, DateTime.Now);
            Assert.Empty(Rows(widget));
            Assert.Equal(Visibility.Collapsed, widget.Visibility);
            widget.Editing = true;
            Assert.Equal(Visibility.Visible, widget.Visibility);
        }));
    }

    [Fact]
    public void Sizes_and_speed_can_be_left_out()
    {
        InBritishEnglish(() =>
        {
            Assert.Equal("40 % · 40.0 MB/s · 3m", DownloadsWidget.Describe(Tunic, sizes: false, speed: true));
            Assert.Equal("6.0 / 15.0 GB", DownloadsWidget.Describe(Tunic, sizes: true, speed: false));
            Assert.Equal("Paused · 25 %", DownloadsWidget.Describe(Abzu, sizes: false, speed: true));
            // A size not known yet, and no speed yet.
            Assert.Equal("Downloading", DownloadsWidget.Describe(Tunic with { Done = 0, Total = 0, Speed = null }, sizes: true, speed: true));
            Assert.Equal("Installing · 700.0 / 900.0 MB", DownloadsWidget.Describe(
                new SteamDownload(5, "Hades", SteamDownloadState.Installing, 700 * Megabyte, 900 * Megabyte), sizes: true, speed: true));
        });
    }

    /// <summary>A store with Steam's downloads in it, as the provider writes them.</summary>
    private static MetricStore Store(params SteamDownload[] downloads)
    {
        var store = new MetricStore();
        using var provider = new SteamDownloadsProvider(new NoFiles());
        provider.Initialize(store);
        SteamDownloadsProvider.Publish(store, downloads);
        return store;
    }

    private static void InBritishEnglish(Action test)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
        try
        {
            test();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static List<StackPanel> Rows(Widget widget) =>
        ((StackPanel)((ShrinkToFit)widget.Children[0]).Child).Children.Cast<StackPanel>().ToList();

    private static List<string> Texts(StackPanel row) =>
        ((Grid)((ShrinkToFit)row.Children[0]).Child).Children.OfType<TextBlock>().Select(t => t.Text).ToList();

    private static Grid Bar(StackPanel row) => (Grid)row.Children[1];

    private static Border Fill(StackPanel row) => (Border)Bar(row).Children[0];

    /// <summary>No Steam, so the provider is only there to register its readings.</summary>
    private sealed class NoFiles : IGameFiles
    {
        public string? SteamPath() => null;

        public string? ReadText(string path) => null;

        public byte[]? ReadBytes(string path) => null;

        public bool FileExists(string path) => false;

        public IEnumerable<string> Files(string folder, string pattern) => [];

        public IEnumerable<string> Folders(string folder) => [];

        public IReadOnlyList<(string Path, DateTime Written)> FilesWritten(string folder, string pattern) => [];

        public string? Description(string exe) => null;

        public byte[]? Icon(string exe) => null;

        public string? EpicDataPath() => null;

        public IReadOnlyList<RegistryEntry> LocalMachineEntries(string path) => [];
    }
}
