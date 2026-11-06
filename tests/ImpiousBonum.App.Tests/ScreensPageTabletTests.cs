using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ImpiousBonum.App.Controls;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Settings;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Tests;

/// <summary>The Screens page's tablet screens: picked under the map like a monitor, each with its layout, size and link.</summary>
// Shares AppPaths (a static) with the layout store tests, so they mustn't run in parallel.
[Collection("AppPaths")]
public sealed class ScreensPageTabletTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ImpiousBonum.Tests", Guid.NewGuid().ToString("N"));

    public ScreensPageTabletTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Without_a_tablet_screen_there_is_only_the_button_to_add_one()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            var settings = new AppSettings();
            using var store = new LayoutStore(settings);
            var (page, _) = Open(settings, store);
            Assert.Empty(page.TabletMap.Children);
            Assert.Single(Logical<Button>(page), b => AutomationProperties.GetName(b) == "Add a tablet screen…");
            Assert.DoesNotContain(Logical<SettingsCard>(page), c => c.Header == "Tablet link");
        });
    }

    [Fact]
    public void A_tablet_screen_is_picked_like_a_monitor_and_shows_its_layout_size_and_link()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            var tablet = new TabletScreen("Galaxy Tab", 2560, 1600, "Tab");
            var settings = new AppSettings { TabletScreens = [tablet], TabletToken = "s3cret-token_value" };
            using var store = new LayoutStore(settings);
            store.Save("Tab", tablet.NewLayout(new()));
            store.Save("Clock", new LayoutDocument());
            var (page, host) = Open(settings, store);

            // Drawn its shape, under the map.
            var tile = Assert.IsType<RadioButton>(Assert.Single(page.TabletMap.Children));
            Assert.Equal("Galaxy Tab · 2560×1600 (tablet): Tab", AutomationProperties.GetName(tile));
            Assert.Equal((102.4, 64.0), (tile.Width, tile.Height));

            tile.IsChecked = true;
            Assert.Equal("Galaxy Tab · 2560×1600 (tablet)", page.ScreenHeading.Text);
            Assert.StartsWith("2560×1600 pixels, landscape.", Card(page, "Size").Description);
            Assert.Equal("Tab", Choice(page, "Layout").SelectedItem);

            // Its link needs the tablet view: turning that on from here shows the screen's own link.
            Assert.Contains("the tablet view, which is off", Card(page, "Tablet link").Description);
            Click(Button(page, "Turn on the tablet view"));
            Assert.Equal([("view", null, null), ("show", null, "Galaxy Tab")], host.Asked);
            Assert.Equal("Galaxy Tab · 2560×1600 (tablet)", page.ScreenHeading.Text);
            Click(Button(page, "Copy"));
            Assert.Equal(("copy", null, "Galaxy Tab"), host.Asked[^1]);
            // On a PC with no local network there's no link to show, only a message.
            Assert.True(Card(page, "Tablet link").Description is { } link
                && (link.EndsWith("&screen=Galaxy%20Tab", StringComparison.Ordinal) || !link.StartsWith("http", StringComparison.Ordinal)));

            // Another layout on it.
            Choice(page, "Layout").SelectedItem = "Clock";
            Assert.Equal([tablet with { Layout = "Clock" }], settings.TabletScreens);
            Assert.Equal("Clock", Choice(page, "Layout").SelectedItem);

            // Removed, its layout stays.
            Click(Button(page, "Remove"));
            Assert.Empty(settings.TabletScreens);
            Assert.Empty(page.TabletMap.Children);
            Assert.NotEqual("Galaxy Tab · 2560×1600 (tablet)", page.ScreenHeading.Text);
            Assert.Contains("Clock", store.List());
        });
    }

    [Fact]
    public void A_portrait_tablet_screen_is_drawn_upright()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            var settings = new AppSettings { TabletScreens = [new TabletScreen("Kitchen Fire", 800, 1280, "Default")] };
            using var store = new LayoutStore(settings);
            var (page, _) = Open(settings, store);
            var tile = Assert.IsType<RadioButton>(Assert.Single(page.TabletMap.Children));
            Assert.Equal((40.0, 64.0), (tile.Width, tile.Height));
            tile.IsChecked = true;
            Assert.StartsWith("800×1280 pixels, portrait.", Card(page, "Size").Description);
        });
    }

    private static (ScreensPage Page, ScreensHost Host) Open(AppSettings settings, LayoutStore store)
    {
        var proxy = DispatchProxy.Create<ISettingsHost, ScreensHost>();
        var host = (ScreensHost)(object)proxy;
        host.Settings = settings;
        host.Layouts = store;
        var page = new ScreensPage(proxy, null!);
        page.Load();
        return (page, host);
    }

    /// <summary>A settings host that keeps the tablet screens it's given and notes the tablet view and links it was asked for.</summary>
    public class ScreensHost : DispatchProxy
    {
        public AppSettings Settings { get; set; } = new();

        public LayoutStore? Layouts { get; set; }

        public bool Running { get; set; }

        public List<(string, string?, string?)> Asked { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "get_Settings":
                    return Settings;
                case "get_Layouts":
                    return Layouts;
                case "get_WindowOptions":
                    return new WindowOptions(false, true, false, false, true);
                case "get_TabletRunning":
                    return Running;
                case nameof(ISettingsHost.SetTabletView):
                    Running = (bool)args![0]!;
                    Asked.Add(("view", null, null));
                    return null;
                case nameof(ISettingsHost.SetTabletScreens):
                    Settings.TabletScreens = (List<TabletScreen>)args![0]!;
                    return null;
                case nameof(ISettingsHost.ShowTabletLink):
                    Asked.Add(("show", (string?)args![0], (string?)args[1]));
                    return null;
                case nameof(ISettingsHost.CopyTabletLink):
                    Asked.Add(("copy", (string?)args![0], (string?)args[1]));
                    return null;
                default:
                    return targetMethod?.ReturnType is { IsValueType: true } type && type != typeof(void) ? Activator.CreateInstance(type) : null;
            }
        }
    }

    private static SettingsCard Card(DependencyObject page, string header) => Logical<SettingsCard>(page).Single(c => c.Header == header);

    private static ComboBox Choice(DependencyObject page, string name) => Logical<ComboBox>(page).Single(b => AutomationProperties.GetName(b) == name);

    private static Button Button(DependencyObject page, string name) => Logical<Button>(page).First(b => AutomationProperties.GetName(b) == name);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static IEnumerable<T> Logical<T>(DependencyObject? root) where T : DependencyObject =>
        root is null ? [] : LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().SelectMany(child => child is T match ? [match, .. Logical<T>(child)] : Logical<T>(child));
}
