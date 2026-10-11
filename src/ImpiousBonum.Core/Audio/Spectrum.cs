namespace ImpiousBonum.Core.Audio;

/// <summary>
/// Turns a block of audio into bar heights for a visualiser: a Hann-windowed FFT, its power summed into bands spaced
/// evenly in pitch (log frequency) between <see cref="MinHz"/> and <see cref="MaxHz"/>, each mapped from decibels to 0–1.
/// Allocates its buffers once, so calling it many times a second makes no garbage.
/// </summary>
public sealed class Spectrum
{
    public const double MinHz = 40;
    public const double MaxHz = 16_000;

    /// <summary>Quieter than this (in dB below full scale) shows as an empty bar.</summary>
    public const double FloorDb = -75;

    private readonly int _size;
    private readonly double[] _window;
    private readonly double[] _re;
    private readonly double[] _im;
    private readonly int[] _bandStart;
    private readonly int[] _bandEnd;

    /// <param name="size">FFT length, a power of two (e.g. 2048).</param>
    /// <param name="bands">How many bars.</param>
    public Spectrum(int size, int bands, int sampleRate)
    {
        if (size < 16 || (size & (size - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(size), "The FFT length must be a power of two, at least 16.");
        _size = size;
        _window = new double[size];
        for (var i = 0; i < size; i++)
            _window[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (size - 1)));
        _re = new double[size];
        _im = new double[size];
        (_bandStart, _bandEnd) = Bands(size, Math.Max(1, bands), sampleRate);
    }

    public int Size => _size;

    public int BandCount => _bandStart.Length;

    /// <summary>
    /// Each band's FFT bins, [start, end): log-spaced edges, but always at least one bin each, so low bands with
    /// fewer bins than bars still move.
    /// </summary>
    internal static (int[] Start, int[] End) Bands(int size, int bands, int sampleRate)
    {
        var binHz = (double)sampleRate / size;
        var maxHz = Math.Min(MaxHz, sampleRate / 2.0);
        var start = new int[bands];
        var end = new int[bands];
        var previous = Math.Max(1, (int)Math.Floor(MinHz / binHz));
        for (var b = 0; b < bands; b++)
        {
            var hz = MinHz * Math.Pow(maxHz / MinHz, (b + 1.0) / bands);
            var edge = Math.Min(size / 2, Math.Max(previous + 1, (int)Math.Round(hz / binHz)));
            start[b] = Math.Min(previous, size / 2 - 1);
            end[b] = Math.Max(start[b] + 1, edge);
            previous = end[b];
        }
        return (start, end);
    }

    /// <summary>
    /// Fills <paramref name="levels"/> (one per band, 0–1) from the newest <see cref="Size"/> samples in
    /// <paramref name="samples"/> (mono, −1 to 1). Fewer samples are padded with silence.
    /// </summary>
    public void Compute(ReadOnlySpan<float> samples, Span<double> levels)
    {
        var offset = Math.Max(0, samples.Length - _size);
        var count = Math.Min(samples.Length, _size);
        for (var i = 0; i < _size; i++)
        {
            _re[i] = i < count ? samples[offset + i] * _window[i] : 0;
            _im[i] = 0;
        }
        Fft(_re, _im);

        // A full-scale sine with this window peaks at about size/4 in magnitude: call that 0 dB.
        var reference = _size / 4.0;
        for (var b = 0; b < _bandStart.Length && b < levels.Length; b++)
        {
            var peak = 0.0;
            for (var k = _bandStart[b]; k < _bandEnd[b]; k++)
                peak = Math.Max(peak, _re[k] * _re[k] + _im[k] * _im[k]);
            var db = 10 * Math.Log10(peak / (reference * reference) + 1e-12);
            levels[b] = Math.Clamp((db - FloorDb) / -FloorDb, 0, 1);
        }
    }

    /// <summary>In-place iterative radix-2 FFT.</summary>
    internal static void Fft(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (var length = 2; length <= n; length <<= 1)
        {
            var angle = -2 * Math.PI / length;
            var (wRe, wIm) = (Math.Cos(angle), Math.Sin(angle));
            for (var i = 0; i < n; i += length)
            {
                var (curRe, curIm) = (1.0, 0.0);
                for (var k = 0; k < length / 2; k++)
                {
                    var a = i + k;
                    var b = a + length / 2;
                    var tRe = re[b] * curRe - im[b] * curIm;
                    var tIm = re[b] * curIm + im[b] * curRe;
                    re[b] = re[a] - tRe;
                    im[b] = im[a] - tIm;
                    re[a] += tRe;
                    im[a] += tIm;
                    (curRe, curIm) = (curRe * wRe - curIm * wIm, curRe * wIm + curIm * wRe);
                }
            }
        }
    }

    /// <summary>
    /// Moves bars towards new levels: straight up to a louder level, falling back by at most
    /// <paramref name="fall"/> per frame, which reads as motion rather than flicker.
    /// </summary>
    public static void Smooth(Span<double> shown, ReadOnlySpan<double> target, double fall)
    {
        for (var i = 0; i < shown.Length && i < target.Length; i++)
            shown[i] = target[i] >= shown[i] ? target[i] : Math.Max(target[i], shown[i] - fall);
    }
}
