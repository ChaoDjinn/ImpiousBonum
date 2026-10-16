using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImpiousBonum.App.Layout;

namespace ImpiousBonum.App.Tests;

public sealed class ParticleTests
{
    private const double Frame = 0.04;

    [Fact]
    public void None_or_nothing_to_show_makes_no_layer()
    {
        Sta.Run(() =>
        {
            Assert.Null(ParticleLayer.Create(new ThemeSettings(), 1920, 480, animate: true));
            Assert.Null(ParticleLayer.Create(new ThemeSettings { Particles = "snow", ParticleAmount = 0 }, 1920, 480, animate: true));
            Assert.Null(ParticleLayer.Create(new ThemeSettings { Particles = "sleet" }, 1920, 480, animate: true));

            var view = new DashboardView();
            view.Build(new LayoutDocument(), new ThemeSettings());
            Assert.Empty(view.Children.OfType<ParticleLayer>());

            view.Build(new LayoutDocument(), new ThemeSettings { Particles = "snow" });
            var layer = Assert.Single(view.Children.OfType<ParticleLayer>());
            Assert.Same(layer, view.Children[0]);
            Assert.False(layer.IsHitTestVisible);
        });
    }

    [Fact]
    public void A_few_dozen_for_a_strip_more_with_a_bigger_amount_and_never_too_many()
    {
        var strip = ParticleField.Count(ParticleKind.Snow, 1920, 480, 0.5);
        Assert.InRange(strip, 24, 60);
        Assert.True(ParticleField.Count(ParticleKind.Snow, 1920, 480, 1) > strip);
        Assert.True(ParticleField.Count(ParticleKind.Leaves, 1920, 480, 0.5) < strip);
        // Drawn twice the size on a 4K canvas, so about as many as on 1080p.
        Assert.Equal(ParticleField.Count(ParticleKind.Snow, 1920, 1080, 0.5), ParticleField.Count(ParticleKind.Snow, 3840, 2160, 0.5));
        Assert.Equal(ParticleField.MaxCount, ParticleField.Count(ParticleKind.Confetti, 10_000, 10_000, 1));
        Assert.Equal(ParticleField.MaxCount, ParticleField.Count(ParticleKind.Snow, 3840, 2160, 5));
        Assert.Equal(4, ParticleField.Count(ParticleKind.Embers, 100, 50, 0.1));
        Assert.Equal(0, ParticleField.Count(ParticleKind.None, 1920, 480, 1));
        Assert.Equal(0, ParticleField.Count(ParticleKind.Snow, 1920, 480, double.NaN));
    }

    [Theory]
    [InlineData(ParticleKind.Snow)]
    [InlineData(ParticleKind.Leaves)]
    [InlineData(ParticleKind.Petals)]
    [InlineData(ParticleKind.Confetti)]
    public void Falling_ones_stay_on_the_canvas_and_start_again_at_the_top(ParticleKind kind)
    {
        var field = new ParticleField(kind, 800, 300, 1, shades: 3);
        var respawned = 0;
        // Five minutes at 25 frames a second.
        for (var frame = 0; frame < 7500; frame++)
        {
            var before = field.Particles.Select(p => p.Y).ToArray();
            field.Step(Frame);
            for (var i = 0; i < field.Particles.Count; i++)
            {
                var p = field.Particles[i];
                Assert.InRange(p.X, -field.Margin, field.Width + field.Margin);
                Assert.InRange(p.Y, -field.Reach(p), field.Height + field.Reach(p));
                Assert.InRange(p.Shade, 0, 2);
                if (p.Y < before[i])
                {
                    // Gone off the bottom: back just above the top.
                    Assert.Equal(-field.Reach(p), p.Y, 6);
                    respawned++;
                }
            }
        }
        Assert.True(respawned > field.Particles.Count, $"Only {respawned} came back.");
    }

    [Fact]
    public void Embers_rise_fade_and_start_again_at_the_bottom()
    {
        var field = new ParticleField(ParticleKind.Embers, 800, 300, 1, shades: 4);
        var respawned = 0;
        for (var frame = 0; frame < 7500; frame++)
        {
            var before = field.Particles.Select(p => p.Y).ToArray();
            field.Step(Frame);
            for (var i = 0; i < field.Particles.Count; i++)
            {
                var p = field.Particles[i];
                Assert.InRange(p.X, -field.Margin, field.Width + field.Margin);
                Assert.InRange(p.Y, -field.Reach(p), field.Height + field.Reach(p));
                Assert.InRange(field.Opacity(p), 0, 1);
                if (p.Y > before[i])
                {
                    Assert.Equal(field.Height + field.Reach(p), p.Y, 6);
                    Assert.Equal(0, p.Age);
                    respawned++;
                }
            }
        }
        Assert.True(respawned > field.Particles.Count, $"Only {respawned} came back.");

        // Each starts dark, glows, and has faded out by the end of its life.
        var ember = new Particle { Life = 4, Depth = 1, Flip = Math.PI / 2 };
        Assert.Equal(0, field.Opacity(ember));
        ember.Age = 1;
        Assert.True(field.Opacity(ember) > 0.8);
        ember.Age = 4;
        Assert.Equal(0, field.Opacity(ember), 6);
    }

    [Fact]
    public void They_drift_across_and_come_back_on_the_other_side()
    {
        var field = new ParticleField(ParticleKind.Snow, 800, 300, 1, shades: 1);
        var p = field.Particles[0];
        p.Y = 100;
        p.X = field.Width + field.Margin - 0.01;
        p.Sway = 0;
        // The breeze starts from the left (see Wind), carrying it past the right edge.
        for (var i = 0; i < 200 && p.X > 0; i++)
            field.Step(Frame);
        Assert.InRange(p.X, -field.Margin, field.Width / 2);
    }

    [Fact]
    public void A_long_pause_doesnt_make_them_jump()
    {
        var field = new ParticleField(ParticleKind.Confetti, 1920, 480, 1, shades: 3);
        var before = field.Particles.Select(p => p.Y).ToArray();
        field.Step(30);
        var fastest = field.Particles.Max(p => p.Fall);
        for (var i = 0; i < before.Length; i++)
        {
            if (field.Particles[i].Y >= before[i])
                Assert.True(field.Particles[i].Y - before[i] <= fastest * ParticleField.LongestStep + 1e-9);
        }
    }

    [Theory]
    [InlineData(400_000, true, 40)]
    [InlineData(1_000_000, true, 40)]
    [InlineData(2_000_000, true, 80)]
    [InlineData(8_294_400, true, 167)]
    [InlineData(8_294_400, false, 40)]
    public void Big_dashboards_drawn_without_the_graphics_card_start_slower(double pixels, bool software, double milliseconds) =>
        Assert.Equal(milliseconds, ParticleLayer.FrameTimeFor(pixels, software).TotalMilliseconds, 0);

    [Fact]
    public void They_slow_down_while_the_app_is_busy_and_stand_still_past_the_slowest()
    {
        var fastest = ParticleLayer.FrameTime;
        Assert.Equal(TimeSpan.FromMilliseconds(80), ParticleLayer.Pace(fastest, fastest, 0.3));
        Assert.Equal(ParticleLayer.SlowestFrame, ParticleLayer.Pace(TimeSpan.FromMilliseconds(640), fastest, 0.3));
        Assert.Null(ParticleLayer.Pace(ParticleLayer.SlowestFrame, fastest, 0.3));

        // Quicker again once it's quiet, but never quicker than they started.
        Assert.Equal(TimeSpan.FromMilliseconds(80), ParticleLayer.Pace(TimeSpan.FromMilliseconds(120), fastest, 0.01));
        Assert.Equal(fastest, ParticleLayer.Pace(TimeSpan.FromMilliseconds(50), fastest, 0.01));
        Assert.Equal(TimeSpan.FromMilliseconds(80), ParticleLayer.Pace(TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(80), 0));

        // In between, as they are.
        Assert.Equal(TimeSpan.FromMilliseconds(160), ParticleLayer.Pace(TimeSpan.FromMilliseconds(160), fastest, 0.07));
    }

    [Fact]
    public void Widgets_go_without_the_glow_while_the_theme_has_particles()
    {
        Sta.Run(() =>
        {
            var layout = new LayoutDocument { Widgets = [new System.Text.Json.Nodes.JsonObject { ["type"] = "clock" }] };
            var view = new DashboardView();
            view.Build(layout, new ThemeSettings { Glow = 0.5 });
            Assert.NotNull(view.Children.OfType<Widgets.Widget>().Single().Effect);

            view.Build(layout, new ThemeSettings { Glow = 0.5, Particles = "snow" });
            Assert.Null(view.Children.OfType<Widgets.Widget>().Single().Effect);
        });
    }

    [Fact]
    public void The_same_theme_places_them_the_same_way()
    {
        var first = new ParticleField(ParticleKind.Petals, 1920, 480, 0.5, shades: 5);
        var second = new ParticleField(ParticleKind.Petals, 1920, 480, 0.5, shades: 5);
        Assert.Equal(first.Particles.Select(p => (p.X, p.Y, p.Size)), second.Particles.Select(p => (p.X, p.Y, p.Size)));
    }

    [Fact]
    public void They_move_only_while_the_dashboard_is_showing()
    {
        Sta.Run(() =>
        {
            var view = new DashboardView();
            view.Build(new LayoutDocument { Width = 400, Height = 100 }, new ThemeSettings { Particles = "leaves" });
            var layer = view.Children.OfType<ParticleLayer>().Single();
            Assert.False(layer.Playing);

            var window = new Window
            {
                Content = view, SizeToContent = SizeToContent.WidthAndHeight, Left = -30000, Top = -30000,
                ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
            };
            window.Show();
            Settle();
            Assert.True(layer.Playing);

            var start = layer.Field.Particles.Select(p => p.Y).ToArray();
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (waited.ElapsedMilliseconds < 300)
                Settle();
            Assert.NotEqual(start, layer.Field.Particles.Select(p => p.Y).ToArray());

            window.WindowState = WindowState.Minimized;
            Settle();
            Assert.False(layer.Playing);
            window.WindowState = WindowState.Normal;
            Settle();
            Assert.True(layer.Playing);

            window.Hide();
            Settle();
            Assert.False(layer.Playing);
            window.Show();
            Settle();
            Assert.True(layer.Playing);

            // Building again (a new layout, say) stops the old ones.
            view.Build(new LayoutDocument { Width = 400, Height = 100 }, new ThemeSettings { Particles = "embers" });
            Assert.False(layer.Playing);
            Assert.True(view.Children.OfType<ParticleLayer>().Single().Playing);

            window.Close();
            Settle();
            Assert.False(view.Children.OfType<ParticleLayer>().Single().Playing);
        });
    }

    [Fact]
    public void Copies_never_on_screen_get_a_still_frame_and_no_timer()
    {
        Sta.Run(() =>
        {
            var theme = new ThemeSettings { Particles = "snow", ParticleAmount = 1 };
            var still = ParticleLayer.Create(theme, 400, 200, animate: false)!;
            still.Play();
            Assert.False(still.Playing);

            var view = new DashboardView { AnimateBackground = false };
            view.Build(new LayoutDocument { Width = 400, Height = 200 }, theme);
            var window = new Window
            {
                Content = view, SizeToContent = SizeToContent.WidthAndHeight, Left = -30000, Top = -30000,
                ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None,
            };
            window.Show();
            Settle();
            Assert.False(view.Children.OfType<ParticleLayer>().Single().Playing);
            window.Close();

            // Drawn the same each time, so a snapshot or the tablet view doesn't change from one picture to the next.
            var first = Render(theme, 400, 200);
            Assert.Equal(first, Render(theme, 400, 200));
            Assert.True(first.Count(p => p.A > 0) > 200, "No snow drawn.");
        });
    }

    [Fact]
    public void Each_kind_comes_in_its_own_colours_or_the_themes()
    {
        Sta.Run(() =>
        {
            var plain = new ThemeSettings();
            Assert.All(ParticleLayer.Palette(ParticleKind.Snow, plain), c => Assert.True(c.R > 220 && c.G > 220 && c.B > 220));
            Assert.All(ParticleLayer.Palette(ParticleKind.Embers, plain), c => Assert.True(c.R > c.G && c.G > c.B));
            Assert.All(ParticleLayer.Palette(ParticleKind.Leaves, plain), c => Assert.True(c.R > c.B));
            Assert.All(ParticleLayer.Palette(ParticleKind.Petals, plain), c => Assert.True(c.R > c.G));

            var confetti = ParticleLayer.Palette(ParticleKind.Confetti, new ThemeSettings { Accent = "#00FF00", Accent2 = "#0000FF", Warning = "#00FF00" });
            Assert.Contains(Colors.Lime, confetti);
            Assert.Contains(Colors.Blue, confetti);
            Assert.Equal(confetti.Count, confetti.Distinct().Count());

            var own = ParticleLayer.Palette(ParticleKind.Snow, new ThemeSettings { ParticleColor = "#FF0000" });
            Assert.Equal(Colors.Red, own[0]);
            Assert.All(own, c => Assert.True(c.R > c.G));
            Assert.All(Render(new ThemeSettings { Particles = "embers", ParticleAmount = 1 }, 400, 200).Where(p => p.A > 0), p => Assert.True(p.R >= p.B));
        });
    }

    [Fact]
    public void The_settings_load_from_a_layout_and_are_checked()
    {
        var good = JsonSerializer.Deserialize<LayoutDocument>(
            """{ "theme": { "base": "Christmas", "particles": "snow", "particleAmount": 0.8, "particleColor": "#E0F0FF" } }""", JsonDefaults.Options)!;
        var theme = new ThemeLibrary(null).Resolve(good.Theme);
        Assert.Equal(new ThemeLibrary(null).Resolve(new System.Text.Json.Nodes.JsonObject { ["base"] = "Christmas" }).Background, theme.Background);
        Assert.Equal("snow", theme.Particles);
        Assert.Equal(0.8, theme.ParticleAmount);
        Assert.Equal("#E0F0FF", theme.ParticleColor);
        Assert.Equal(ParticleKind.Snow, ParticleLayer.Parse(theme.Particles));
        Assert.Empty(LayoutValidator.Validate(good));

        var defaults = new ThemeSettings();
        Assert.Equal("none", defaults.Particles);
        Assert.Equal(ParticleKind.None, ParticleLayer.Parse(defaults.Particles));

        var bad = JsonSerializer.Deserialize<LayoutDocument>(
            """{ "theme": { "particles": "rain", "particleAmount": 2, "particleColor": "frosty" } }""", JsonDefaults.Options)!;
        var issues = LayoutValidator.Validate(bad);
        Assert.Contains(issues, i => i.Contains("'particles'"));
        Assert.Contains(issues, i => i.Contains("'particleAmount'"));
        Assert.Contains(issues, i => i.Contains("'particleColor'"));
    }

    [Fact]
    public void Built_in_themes_have_none()
    {
        // Things that cost CPU stay off until asked for.
        var library = new ThemeLibrary(null);
        foreach (var name in ThemeLibrary.BuiltInNames)
            Assert.Equal("none", library.Resolve(new System.Text.Json.Nodes.JsonObject { ["base"] = name }).Particles);
    }

    /// <summary>A view of this size with the theme's particles as a still frame, on transparent.</summary>
    private static Color[] Render(ThemeSettings theme, int width, int height)
    {
        var layer = ParticleLayer.Create(theme, width, height, animate: false)!;
        layer.Measure(new Size(width, height));
        layer.Arrange(new Rect(0, 0, width, height));
        layer.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(layer);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return Enumerable.Range(0, width * height).Select(i => Color.FromArgb(pixels[i * 4 + 3], pixels[i * 4 + 2], pixels[i * 4 + 1], pixels[i * 4])).ToArray();
    }

    private static void Settle()
    {
        var done = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(() => done.Continue = false, DispatcherPriority.ContextIdle);
        Dispatcher.PushFrame(done);
    }
}
