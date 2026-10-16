using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Settings;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Tests;

/// <summary>The Gallery page's Themes tab, on the repository's own gallery folder.</summary>
// Shares AppPaths (a static) with the layout store tests, so they mustn't run in parallel.
[Collection("AppPaths")]
public sealed class GalleryPageTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ImpiousBonum.Tests", Guid.NewGuid().ToString("N"));

    public GalleryPageTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void The_themes_tab_lists_the_gallerys_themes_and_shows_each_on_the_default_layout()
    {
        Sta.Run(() =>
        {
            // The page's downloads come back to the dispatcher, as they do in the app.
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            AppPaths.UseDataDirectory(_directory);
            using var layouts = new LayoutStore(new AppSettings());
            var host = DispatchProxy.Create<ISettingsHost, GalleryHost>();
            ((GalleryHost)(object)host).Settings = new AppSettings { GalleryAddress = GalleryTests.RepositoryGallery() };
            ((GalleryHost)(object)host).Layouts = layouts;
            var page = new GalleryPage(host, null!);

            page.Load();
            Settle(() => page.EntryList.Items.Count > 0);
            Assert.IsType<GalleryEntry>(page.EntryList.Items[0]);
            Assert.Equal("Add to my layouts", page.UseText.Text);

            page.ThemesTab.IsChecked = true;
            var themes = GalleryTests.RepositoryThemes().ToList();
            Assert.Equal(themes.Select(t => t.Name), page.EntryList.Items.Cast<GalleryTheme>().Select(t => t.Name));
            Assert.Equal(Visibility.Collapsed, page.LayoutFilters.Visibility);
            Assert.Equal("Add to my themes", page.UseText.Text);

            page.EntryList.SelectedIndex = 0;
            Settle(() => page.UseButton.IsEnabled);
            var preview = Assert.IsType<DashboardView>(page.PreviewBox.Child);
            Assert.Equal(Visibility.Visible, page.PreviewBox.Visibility);
            Assert.Equal(LayoutLibrary.LoadDefault().Widgets.Count, preview.Children.Count);
            Assert.Equal((Color)ColorConverter.ConvertFromString(ThemeLibrary.GetString(themes[0].Values, "background")!), FirstColour(preview.Background));

            page.LayoutsTab.IsChecked = true;
            Assert.IsType<GalleryEntry>(page.EntryList.Items[0]);
            Assert.Equal(Visibility.Visible, page.LayoutFilters.Visibility);
            Assert.False(page.UseButton.IsEnabled);
        });
    }

    /// <summary>Lets the dispatcher run until <paramref name="done"/>, as the page reads the gallery folder in the background.</summary>
    private static void Settle(Func<bool> done)
    {
        var waiting = Stopwatch.StartNew();
        while (!done())
        {
            if (waiting.Elapsed > TimeSpan.FromSeconds(10))
                throw new TimeoutException("The gallery page was still loading after 10 seconds.");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(() => frame.Continue = false, DispatcherPriority.ContextIdle);
            Dispatcher.PushFrame(frame);
            Thread.Sleep(5);
        }
    }

    private static Color FirstColour(Brush brush) => brush is GradientBrush gradient ? gradient.GradientStops[0].Color : ((SolidColorBrush)brush).Color;

    /// <summary>A settings host with just the settings and the saved layouts and themes.</summary>
    public class GalleryHost : DispatchProxy
    {
        public AppSettings Settings { get; set; } = new();

        public LayoutStore? Layouts { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            "get_Settings" => Settings,
            "get_Layouts" => Layouts,
            _ => targetMethod?.ReturnType is { IsValueType: true } type && type != typeof(void) ? Activator.CreateInstance(type) : null,
        };
    }
}
