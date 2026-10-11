using System.Windows.Forms;
using ImpiousBonum.App.Shell;
using Color = System.Drawing.Color;

namespace ImpiousBonum.App.Tests;

public sealed class TrayTests
{
    private static DisplayMonitor Monitor(string device, string? name, bool primary = false) =>
        new($@"\\?\DISPLAY#{device}", $@"\\.\{device}", "Generic PnP Monitor", new PixelRect(0, 0, 1920, 1080), primary) { Name = name };

    [Fact]
    public void Monitors_go_by_their_own_names_or_else_their_device_names()
    {
        Assert.Equal("DELL U2720Q · 1920×1080 (main)", Monitor("DISPLAY1", "DELL U2720Q", primary: true).DisplayName);
        Assert.Equal("DISPLAY7 · 1920×1080", Monitor("DISPLAY7", null).DisplayName);
    }

    [Fact]
    public void Monitors_of_the_same_model_can_be_told_apart()
    {
        var named = DisplayMonitor.Distinguished([Monitor("DISPLAY1", "DELL U2720Q"), Monitor("DISPLAY2", "dell u2720q"), Monitor("DISPLAY3", "ZeroMOD"), Monitor("DISPLAY4", null)]);

        Assert.Equal(["DELL U2720Q (DISPLAY1)", "dell u2720q (DISPLAY2)", "ZeroMOD", null], named.Select(m => m.Name));
    }

    [Fact]
    public void A_disconnected_extra_dashboard_is_named_by_its_device()
    {
        var extra = new ExtraDashboard(@"\\?\DISPLAY#GONE", @"\\.\DISPLAY5", "Gaming");
        Assert.Equal("DISPLAY5 (not connected)", extra.MonitorName([]));
    }

    [Fact]
    public void Help_links_go_to_the_docs_of_the_installed_release()
    {
        Assert.Equal("https://github.com/ChaoDjinn/ImpiousBonum/blob/v0.10.0/docs/guide.md", Links.Guide("0.10.0"));
        Assert.Equal("https://github.com/ChaoDjinn/ImpiousBonum/blob/main/docs/guide.md", Links.Guide(null));
        Assert.Equal("https://github.com/ChaoDjinn/ImpiousBonum/blob/v0.10.0/CHANGELOG.md", Links.Changelog("0.10.0"));
        Assert.Equal("https://github.com/ChaoDjinn/ImpiousBonum/blob/main/docs/widgets.md", Links.Widgets(null));
    }

    [Fact]
    public void About_details_say_what_a_problem_report_needs()
    {
        var details = AboutWindow.Details("0.10.0", "Running");
        Assert.StartsWith("Impious Bonum 0.10.0 · ", details);
        Assert.Contains(".NET", details);
        Assert.EndsWith("sensor service: Running", details);
        Assert.StartsWith("Impious Bonum (development build) · ", AboutWindow.Details(null, "Not running"));
    }

    [Fact]
    public void The_app_icon_is_built_in_with_a_size_for_every_scaling()
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream(TrayIcon.IconResource);
        Assert.NotNull(stream);
        var decoder = new System.Windows.Media.Imaging.IconBitmapDecoder(stream, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        var sizes = decoder.Frames.Select(f => f.PixelWidth).ToHashSet();
        Assert.Superset(new HashSet<int> { 16, 20, 24, 32, 40, 48, 256 }, sizes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_menu_palettes_are_easy_to_read(bool dark)
    {
        var palette = dark ? TrayMenuPalette.Dark : TrayMenuPalette.Light;
        Assert.True(Contrast(palette.Text, palette.Background) >= 7, "text");
        Assert.True(Contrast(palette.Text, palette.Hover) >= 7, "text on the highlight");
        Assert.True(Contrast(palette.SecondaryText, palette.Background) >= 4.5, "secondary text");
        Assert.True(Contrast(palette.Accent, palette.Background) >= 3, "check marks");
    }

    [Fact]
    public void Styling_reaches_submenus_and_draws_icons()
    {
        TrayMenuPalette.Override = TrayMenuPalette.Light;
        try
        {
            using var menu = new ContextMenuStrip();
            var edit = new ToolStripMenuItem("Edit layout…").WithIcon(TrayMenuStyle.Icons.Edit);
            var plain = new ToolStripMenuItem("Start with Windows");
            var parent = new ToolStripMenuItem("Display");
            var child = new ToolStripMenuItem("When you're away");
            child.DropDownItems.Add("Don't dim");
            parent.DropDownItems.Add(child);
            menu.Items.AddRange([edit, plain, new ToolStripSeparator(), parent]);

            TrayMenuStyle.Attach(menu);

            Assert.NotNull(edit.Image);
            Assert.Null(plain.Image);
            Assert.Equal(TrayMenuPalette.Light.Background, menu.BackColor);
            Assert.Same(menu.Renderer, parent.DropDown.Renderer);
            Assert.Same(menu.Renderer, child.DropDown.Renderer);
        }
        finally
        {
            TrayMenuPalette.Override = null;
        }
    }

    [Fact]
    public void Submenus_open_away_from_the_tray_and_keep_going_that_way()
    {
        // A 3840-wide main screen with a tray menu at its right edge, and another monitor further right.
        var area = new System.Drawing.Rectangle(0, 0, 3840, 2100);
        var root = new System.Drawing.Rectangle(3500, 1200, 320, 880);

        // The first submenu goes left, where the room is, not onto the next monitor.
        Assert.True(TrayMenuStyle.Side(root, area, 400, menuOpenedLeft: null));
        // Its submenus keep going left, rather than back over the main menu.
        var display = new System.Drawing.Rectangle(3100, 1300, 400, 700);
        Assert.True(TrayMenuStyle.Side(display, area, 300, menuOpenedLeft: true));
        // A menu near the left edge goes right; one going left that runs out of room turns round.
        Assert.False(TrayMenuStyle.Side(new System.Drawing.Rectangle(10, 0, 300, 400), area, 300, menuOpenedLeft: null));
        Assert.False(TrayMenuStyle.Side(new System.Drawing.Rectangle(200, 0, 300, 400), area, 300, menuOpenedLeft: true));
    }

    /// <summary>WCAG contrast ratio, 1 to 21.</summary>
    private static double Contrast(Color a, Color b)
    {
        static double Luminance(Color c)
        {
            static double Channel(byte v) => v / 255.0 is var s && s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}
