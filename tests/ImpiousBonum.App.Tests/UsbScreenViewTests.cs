using System.Text.Json.Nodes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Screens;

namespace ImpiousBonum.App.Tests;

public sealed class UsbScreenViewTests
{
    [Theory]
    [InlineData("landscape", ScreenOrientation.Landscape, 480, 320)]
    [InlineData("landscape-reversed", ScreenOrientation.ReverseLandscape, 480, 320)]
    [InlineData("portrait", ScreenOrientation.Portrait, 320, 480)]
    [InlineData("portrait-reversed", ScreenOrientation.ReversePortrait, 320, 480)]
    [InlineData("sideways", ScreenOrientation.Landscape, 480, 320)]
    public void A_layout_is_drawn_at_the_screens_size_for_its_way_up(string orientation, ScreenOrientation expected, int width, int height)
    {
        Sta.Run(() =>
        {
            var settings = UsbScreenSettings.Default with { Orientation = orientation };
            Assert.Equal(expected, settings.ScreenOrientation);

            using var view = new UsbScreenView();
            view.Apply(settings);
            view.Build(new LayoutDocument { Width = 1920, Height = 480, Widgets = [new JsonObject { ["type"] = "clock" }] }, new ThemeSettings { Background = "#102030" });
            var frame = view.Draw(new MetricStore(), DateTime.Now);

            Assert.NotNull(frame);
            Assert.Equal((width, height), (frame.Value.Width, frame.Value.Height));
            Assert.Equal(width * height * 4, frame.Value.Pixels.Length);
            // The letterbox around a wide layout is the theme's background, not black.
            Assert.Equal(new byte[] { 0x30, 0x20, 0x10 }, frame.Value.Pixels[..3]);
        });
    }

    [Fact]
    public void Off_by_default_and_saved_with_the_settings()
    {
        Sta.Run(() =>
        {
            using var view = new UsbScreenView();
            view.Apply(UsbScreenSettings.Default);
            Assert.Equal("Off", view.Status);
            Assert.Null(view.Draw(new MetricStore(), DateTime.Now));
        });
        Assert.False(UsbScreenSettings.Default.Enabled);
        Assert.Equal(UsbScreenSettings.Auto, UsbScreenSettings.Default.Port);
    }
}
