using ImpiousBonum.App.Shell;

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
}
