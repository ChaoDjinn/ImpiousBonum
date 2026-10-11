using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ImpiousBonum.App.Layout;

namespace ImpiousBonum.App.Tests;

public sealed class GradientAndGlowTests
{
    [Fact]
    public void A_second_accent_and_background_colour_make_gradients()
    {
        Sta.Run(() =>
        {
            var theme = Theme.From(new ThemeSettings { Accent = "#FF0000", Accent2 = "#0000FF", Background = "#000000", BackgroundGradient = "#202020" });

            var accent = Assert.IsType<LinearGradientBrush>(theme.Accent);
            Assert.Equal([Colors.Red, Colors.Blue], accent.GradientStops.Select(s => s.Color));
            Assert.Equal(0, accent.EndPoint.Y - accent.StartPoint.Y, 6);
            Assert.Equal(Colors.Red, theme.AccentColor);

            var background = Assert.IsType<LinearGradientBrush>(theme.Background);
            Assert.Equal(0, background.EndPoint.X - background.StartPoint.X, 6);
            Assert.True(theme.Accent.IsFrozen && theme.Background.IsFrozen);
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a colour")]
    [InlineData("#00FFFFFF")]
    public void No_usable_second_colour_keeps_them_plain(string? second)
    {
        Sta.Run(() =>
        {
            var theme = Theme.From(new ThemeSettings { Accent2 = second, BackgroundGradient = second });
            Assert.IsType<SolidColorBrush>(theme.Accent);
            Assert.IsType<SolidColorBrush>(theme.Background);
        });
    }

    [Fact]
    public void Glow_is_off_by_default_and_follows_the_accent_or_its_own_colour()
    {
        Sta.Run(() =>
        {
            Assert.Null(Theme.From(new ThemeSettings()).Glow);
            Assert.Null(Theme.From(new ThemeSettings { Glow = 0 }).Glow);

            var accentGlow = Assert.IsType<DropShadowEffect>(Theme.From(new ThemeSettings { Accent = "#00FF00", Glow = 0.5 }).Glow);
            Assert.Equal(Colors.Lime, accentGlow.Color);
            Assert.Equal(0, accentGlow.ShadowDepth);
            Assert.True(accentGlow.IsFrozen);

            var own = Assert.IsType<DropShadowEffect>(Theme.From(new ThemeSettings { Glow = 3, GlowColor = "#FF00FF" }).Glow);
            Assert.Equal(Colors.Magenta, own.Color);
            Assert.Equal(24, own.BlurRadius);
        });
    }

    [Fact]
    public void Every_widget_on_the_dashboard_gets_the_glow()
    {
        Sta.Run(() =>
        {
            var view = new DashboardView();
            var layout = new LayoutDocument { Widgets = [new JsonObject { ["type"] = "text" }, new JsonObject { ["type"] = "clock" }] };
            view.Build(layout, new ThemeSettings { Glow = 0.4 });
            Assert.All(view.Children.OfType<System.Windows.UIElement>(), w => Assert.IsType<DropShadowEffect>(w.Effect));

            view.Build(layout, new ThemeSettings());
            Assert.All(view.Children.OfType<System.Windows.UIElement>(), w => Assert.Null(w.Effect));
        });
    }

    [Fact]
    public void The_validator_checks_the_new_settings()
    {
        var layout = JsonSerializer.Deserialize<LayoutDocument>(
            """{ "theme": { "accent2": "pink-ish", "backgroundGradient": "#123456", "glow": 2, "glowColor": "" } }""", JsonDefaults.Options)!;
        var issues = LayoutValidator.Validate(layout);
        Assert.Contains(issues, i => i.Contains("'accent2'"));
        Assert.Contains(issues, i => i.Contains("'glow'"));
        Assert.DoesNotContain(issues, i => i.Contains("backgroundGradient") || i.Contains("glowColor"));
    }
}
