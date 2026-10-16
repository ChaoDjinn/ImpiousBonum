using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Remote;

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

    [Fact]
    public void A_layout_a_link_names_is_drawn_while_watched_and_let_go_when_no_one_asks() => Sta.Run(() =>
    {
        var time = new FakeTime();
        var loads = new List<string>();
        using var view = new TabletView
        {
            FindLayout = name => string.Equals(name, "Streamer", StringComparison.OrdinalIgnoreCase) ? "Streamer" : null,
            LoadLayout = name =>
            {
                loads.Add(name);
                return (LayoutStore.LoadDefault(), new ThemeSettings { Background = "#800000" });
            },
        };
        using var server = new TabletServer(0, Token, time);
        view.Serve(server);
        view.Build(LayoutStore.LoadDefault(), new ThemeSettings());
        var store = new MetricStore();

        // Nothing is made until a tablet asks for it.
        view.Refresh(store, Now);
        Assert.Empty(view.Layouts);
        Assert.Empty(loads);

        Assert.Equal(503, server.Respond(Request($"/frame?k={Token}&layout=streamer")).Status);
        view.Refresh(store, Now);
        Assert.Equal(["Streamer"], view.Layouts);
        Assert.Equal(["Streamer"], loads);
        Assert.NotNull(server.FrameOf("Streamer"));
        // The main dashboard has no tablet watching, so it isn't drawn.
        Assert.Null(server.Frame);

        // A save reads it again, but only when it's next drawn.
        view.ReloadLayouts();
        Assert.Single(loads);
        server.Respond(Request($"/frame?k={Token}&layout=Streamer"));
        view.Refresh(store, Now);
        Assert.Equal(2, loads.Count);

        // Unwatched, it's kept for a while without drawing, then let go.
        time.Advance(TabletServer.WatchWindow);
        view.ReloadLayouts();
        view.Refresh(store, Now);
        Assert.Equal(2, loads.Count);
        Assert.Equal(["Streamer"], view.Layouts);

        time.Advance(TabletServer.LayoutLifetime);
        view.Refresh(store, Now);
        Assert.Empty(view.Layouts);
        Assert.Empty(server.Layouts);
    });

    [Fact]
    public void Stopping_lets_every_layout_go() => Sta.Run(() =>
    {
        using var view = new TabletView { FindLayout = name => name, LoadLayout = _ => (LayoutStore.LoadDefault(), new ThemeSettings()) };
        var server = new TabletServer(0, Token);
        view.Serve(server);
        server.Respond(Request($"/frame?k={Token}&layout=Streamer"));
        view.Refresh(new MetricStore(), Now);
        Assert.Single(view.Layouts);

        view.Stop();
        Assert.Empty(view.Layouts);
        Assert.False(view.IsRunning);
    });

    [Fact]
    public void Readings_are_shared_only_while_turned_on() => Sta.Run(() =>
    {
        var store = new MetricStore();
        using var view = new TabletView { Readings = store };
        using var server = new TabletServer(0, Token);
        view.Serve(server);
        Assert.Same(store, server.Readings);

        view.Readings = null;
        Assert.Null(server.Readings);
        Assert.Equal(404, server.Respond(Request($"/api/metrics?k={Token}")).Status);
    });

    private const string Token = "s3cret-token_value";

    private static HttpRequestHead Request(string target) => HttpRequestHead.Parse($"GET {target} HTTP/1.1\r\nHost: pc")!;

    /// <summary>A clock that only moves when told to.</summary>
    private sealed class FakeTime : TimeProvider
    {
        private long _ticks = TimeSpan.TicksPerDay;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;

        public override long GetTimestamp() => _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
