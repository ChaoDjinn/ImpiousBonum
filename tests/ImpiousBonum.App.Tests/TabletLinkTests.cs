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

/// <summary>The Tablet and USB screen page's link: for the main dashboard, or a layout of its own.</summary>
// Shares AppPaths (a static) with the layout store tests, so they mustn't run in parallel.
[Collection("AppPaths")]
public sealed class TabletLinkTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ImpiousBonum.Tests", Guid.NewGuid().ToString("N"));

    public TabletLinkTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void The_link_can_be_for_a_layout_of_its_own()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            using var store = new LayoutStore(new AppSettings());
            store.Save("Weather Station", new LayoutDocument());
            var host = DispatchProxy.Create<ISettingsHost, LinkHost>();
            var links = (LinkHost)(object)host;
            links.Settings = new AppSettings { TabletView = true, TabletToken = "s3cret-token_value" };
            links.Layouts = store;
            var page = new TabletAndUsbPage(host);
            page.Load();

            // The main dashboard's, to begin with.
            var choice = Choice(page, "Layout for the link");
            Assert.Equal("Same as the main dashboard", choice.SelectedItem);
            Click(Button(page, "Show QR code…"));
            Click(Button(page, "Copy"));
            Assert.Equal([("show", null), ("copy", null)], links.Asked);
            Assert.DoesNotContain("layout=", Card(page, "Link").Description ?? "");

            choice.SelectedItem = "Weather Station";
            Click(Button(page, "Show QR code…"));
            Click(Button(page, "Copy"));
            Assert.Equal([("show", "Weather Station"), ("copy", "Weather Station")], links.Asked.Skip(2));
            // On a PC with no local network there's no link to show, only a message.
            Assert.True(Card(page, "Link").Description is { } link && (link.EndsWith("&layout=Weather%20Station", StringComparison.Ordinal) || !link.StartsWith("http", StringComparison.Ordinal)));

            // A layout that's gone isn't offered, and the link goes back to the main dashboard's.
            store.Delete("Weather Station");
            page.Load();
            Assert.Equal("Same as the main dashboard", Choice(page, "Layout for the link").SelectedItem);
            Click(Button(page, "Show QR code…"));
            Assert.Equal(("show", null), links.Asked[^1]);
        });
    }

    [Fact]
    public void With_the_tablet_view_off_there_is_no_layout_to_pick()
    {
        Sta.Run(() =>
        {
            AppPaths.UseDataDirectory(_directory);
            using var store = new LayoutStore(new AppSettings());
            var host = DispatchProxy.Create<ISettingsHost, LinkHost>();
            ((LinkHost)(object)host).Layouts = store;
            ((LinkHost)(object)host).Running = false;
            var page = new TabletAndUsbPage(host);
            page.Load();
            Assert.False(Card(page, "Layout for the link").IsEnabled);
            Assert.False(Card(page, "Link").IsEnabled);
        });
    }

    /// <summary>A settings host with the tablet view on, noting which links it was asked to show or copy.</summary>
    public class LinkHost : DispatchProxy
    {
        public AppSettings Settings { get; set; } = new();

        public LayoutStore? Layouts { get; set; }

        public bool Running { get; set; } = true;

        public List<(string, string?)> Asked { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "get_Settings":
                    return Settings;
                case "get_Layouts":
                    return Layouts;
                case "get_TabletRunning":
                    return Running;
                case nameof(ISettingsHost.ShowTabletLink):
                    Asked.Add(("show", (string?)args![0]));
                    return null;
                case nameof(ISettingsHost.CopyTabletLink):
                    Asked.Add(("copy", (string?)args![0]));
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
