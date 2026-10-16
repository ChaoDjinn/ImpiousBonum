using System.Text.Json.Nodes;
using ImpiousBonum.App.Audio;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;
using NAudio.Wave;

namespace ImpiousBonum.App.Tests;

public sealed class VisualizerTests
{
    [Fact]
    public void Samples_are_read_from_the_formats_devices_use()
    {
        var floats = BitConverter.GetBytes(0.25f);
        Assert.Equal(0.25f, AudioLoopback.Sample(floats, 0, WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2)));
        Assert.Equal(0.25f, AudioLoopback.Sample(floats, 0, new WaveFormatExtensible(48_000, 32, 2)));

        Assert.Equal(-0.5f, AudioLoopback.Sample(BitConverter.GetBytes((short)-16384), 0, new WaveFormat(44_100, 16, 2)));
        // 24-bit little-endian 0x400000 is half scale.
        Assert.Equal(0.5f, AudioLoopback.Sample([0x00, 0x00, 0x40], 0, new WaveFormat(48_000, 24, 2)));
        Assert.Equal(-1f, AudioLoopback.Sample([0x00, 0x00, 0x80], 0, new WaveFormat(48_000, 24, 2)));
    }

    [Fact]
    public void Capture_starts_and_stops_without_throwing_even_with_no_sound_device()
    {
        AudioLoopback.Shared.Acquire();
        var samples = new float[2048];
        var count = AudioLoopback.Shared.Read(samples);
        Assert.InRange(count, 0, samples.Length);
        AudioLoopback.Shared.Release();
        Assert.Equal(0, AudioLoopback.Shared.Read(samples));
    }

    [Fact]
    public void The_widget_builds_in_both_styles()
    {
        Sta.Run(() =>
        {
            foreach (var style in new[] { "bars", "mirror" })
            {
                var widget = WidgetFactory.Create(new JsonObject { ["type"] = "visualizer", ["style"] = style, ["bands"] = 16, ["peaks"] = style == "bars" },
                    Theme.From(new ThemeSettings()));
                Assert.IsType<VisualizerWidget>(widget);
                widget.Measure(new System.Windows.Size(600, 160));
                widget.Arrange(new System.Windows.Rect(0, 0, 600, 160));
                widget.Refresh(new MetricStore(), DateTime.Now);
            }
        });
    }

    [Fact]
    public void A_snapshot_with_the_demo_readings_shows_their_music()
    {
        Sta.Run(() =>
        {
            int Drawn(MetricStore store)
            {
                var widget = WidgetFactory.Create(new JsonObject { ["type"] = "visualizer", ["bands"] = 16 }, Theme.From(new ThemeSettings()));
                widget.Measure(new System.Windows.Size(320, 100));
                widget.Arrange(new System.Windows.Rect(0, 0, 320, 100));
                widget.Refresh(store, DateTime.Now);
                widget.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(320, 100, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(widget);
                var pixels = new byte[320 * 100 * 4];
                bitmap.CopyPixels(pixels, 320 * 4, 0);
                return pixels.Where((_, i) => i % 4 == 3).Count(alpha => alpha > 0);
            }

            var demo = new MetricStore();
            Core.Demo.DemoReadings.Fill(demo, DateTime.Now);
            Assert.True(Drawn(demo) > 320 * 100 / 4);
            Assert.Equal(0, Drawn(new MetricStore()));
        });
    }
}
