using ImpiousBonum.Core.Audio;

namespace ImpiousBonum.Core.Tests;

public sealed class SpectrumTests
{
    private const int Rate = 48_000;

    private static float[] Sine(double hz, double amplitude, int count = 2048) =>
        Enumerable.Range(0, count).Select(i => (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / Rate))).ToArray();

    [Fact]
    public void A_tone_lights_up_its_own_band_most()
    {
        var spectrum = new Spectrum(2048, 32, Rate);
        foreach (var hz in new[] { 100.0, 1_000, 8_000 })
        {
            var levels = new double[32];
            spectrum.Compute(Sine(hz, 0.5), levels);
            var loudest = Array.IndexOf(levels, levels.Max());
            var (start, end) = Spectrum.Bands(2048, 32, Rate);
            var bin = hz / ((double)Rate / 2048);
            Assert.InRange(bin, start[loudest] - 1, end[loudest] + 1);
            Assert.True(levels.Max() > 0.8, $"{hz} Hz at half volume should be near the top, was {levels.Max():0.00}.");
        }
    }

    [Fact]
    public void Silence_and_no_samples_are_empty_bars()
    {
        var spectrum = new Spectrum(1024, 16, Rate);
        var levels = Enumerable.Repeat(1.0, 16).ToArray();
        spectrum.Compute(new float[1024], levels);
        Assert.All(levels, l => Assert.Equal(0, l));
        spectrum.Compute([], levels);
        Assert.All(levels, l => Assert.Equal(0, l));
    }

    [Fact]
    public void Quieter_is_lower()
    {
        var spectrum = new Spectrum(2048, 24, Rate);
        var loud = new double[24];
        var quiet = new double[24];
        spectrum.Compute(Sine(440, 0.8), loud);
        spectrum.Compute(Sine(440, 0.01), quiet);
        Assert.True(quiet.Max() < loud.Max() - 0.3);
    }

    [Theory]
    [InlineData(1024, 8)]
    [InlineData(2048, 64)]
    [InlineData(512, 128)]
    public void Bands_cover_the_range_in_order_with_at_least_one_bin_each(int size, int bands)
    {
        var (start, end) = Spectrum.Bands(size, bands, Rate);
        Assert.Equal(bands, start.Length);
        for (var b = 0; b < bands; b++)
        {
            Assert.True(end[b] > start[b]);
            Assert.True(end[b] <= size / 2);
            if (b > 0)
                Assert.True(start[b] >= start[b - 1]);
        }
    }

    [Fact]
    public void The_fft_finds_a_pure_tone()
    {
        var re = Enumerable.Range(0, 64).Select(i => Math.Cos(2 * Math.PI * 5 * i / 64)).ToArray();
        var im = new double[64];
        Spectrum.Fft(re, im);
        Assert.Equal(32, Math.Sqrt(re[5] * re[5] + im[5] * im[5]), 6);
        Assert.Equal(0, Math.Sqrt(re[6] * re[6] + im[6] * im[6]), 6);
    }

    [Fact]
    public void Bars_jump_up_and_fall_slowly()
    {
        var shown = new double[] { 0, 0.9 };
        Spectrum.Smooth(shown, [0.7, 0.1], 0.2);
        Assert.Equal([0.7, 0.7], shown, (a, b) => Math.Abs(a - b) < 1e-9);
        Spectrum.Smooth(shown, [0, 0], 0.5);
        Assert.Equal([0.2, 0.2], shown, (a, b) => Math.Abs(a - b) < 1e-9);
    }

    [Fact]
    public void Odd_sizes_are_refused() => Assert.Throws<ArgumentOutOfRangeException>(() => new Spectrum(1000, 8, Rate));
}
