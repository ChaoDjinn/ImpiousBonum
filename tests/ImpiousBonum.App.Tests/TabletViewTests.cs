using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Tests;

public sealed class TabletViewTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 20, 30, 0);

    [Fact]
    public void Draws_nothing_before_a_layout_arrives() => Sta.Run(() =>
    {
        using var view = new TabletView();
        Assert.Null(view.Draw(new MetricStore(), Now));
        Assert.False(view.IsRunning);
    });

    [Fact]
    public void Draws_a_jpeg_and_skips_frames_that_look_the_same() => Sta.Run(() =>
    {
        using var view = new TabletView();
        var store = new MetricStore();
        view.Build(LayoutStore.LoadDefault(), new ThemeSettings());

        var first = view.Draw(store, Now);
        Assert.NotNull(first);
        Assert.Equal("image/jpeg", first.ContentType);
        Assert.Equal([0xFF, 0xD8], first.Image[..2]);

        Assert.Null(view.Draw(store, Now));

        // A different theme is a different picture, with a newer sequence number for the tablet's cache check.
        view.Build(LayoutStore.LoadDefault(), new ThemeSettings { Background = "#800000" });
        var restyled = view.Draw(store, Now);
        Assert.NotNull(restyled);
        Assert.True(restyled.Sequence > first.Sequence);
    });
}
