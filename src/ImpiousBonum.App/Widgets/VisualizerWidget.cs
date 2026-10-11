using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ImpiousBonum.App.Audio;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Audio;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// Bars that move with whatever the PC is playing. Unlike other widgets it draws many times a second, on its own
/// timer, and only while it's on screen; the sound is captured only while a visualiser is showing.
/// </summary>
public sealed class VisualizerWidget : Widget
{
    public static WidgetDescriptor Descriptor { get; } = new(
        "visualizer", "Audio visualiser",
        "Bars that move with whatever the PC is playing, from low notes on the left to high on the right. Draws many times a second, so it uses a little more CPU than other widgets.",
        600, 160,
        [
            Setting.Number("bands", "Bars", 32, 4, 128, group: Setting.Content),
            Setting.Choice("style", "Style", "bars", ["bars", "mirror"], group: Setting.Appearance,
                help: "bars rise from the bottom; mirror grows up and down from the middle."),
            Setting.Number("fps", "Frames a second", 25, 5, 60, group: Setting.Appearance,
                help: "Smoother as it goes up, and a little more CPU. 25 looks fluid."),
            Setting.Number("gap", "Gap", 3, 0, 50, group: Setting.Appearance),
            Setting.Number("cornerRadius", "Corner radius", 2, 0, 50, group: Setting.Appearance),
            Setting.Number("fall", "Fall speed", 0.06, 0.01, 1, group: Setting.Appearance,
                help: "How far a bar can drop each frame, as a share of the height. Lower falls more gently."),
            Setting.Toggle("peaks", "Peak markers", true, group: Setting.Appearance, help: "A line at each bar's recent highest, drifting down."),
            Setting.Color("color", "Colour", "accent"),
            Setting.Color("peakColor", "Peak colour", "foreground"),
        ],
        (settings, theme) => new VisualizerWidget(settings, theme));

    private const int FftSize = 2048;

    private readonly VisualizerElement _bars;
    private readonly DispatcherTimer _timer;
    private readonly float[] _samples = new float[FftSize];
    private readonly double[] _target;
    private readonly double _fall;
    private Spectrum? _spectrum;
    private int _rate;
    private bool _listening;

    public VisualizerWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        var bands = (int)settings.Number("bands");
        _target = new double[bands];
        _fall = settings.Number("fall");
        _bars = new VisualizerElement(bands)
        {
            Mirror = settings.String("style") == "mirror",
            Gap = settings.Number("gap"),
            Radius = settings.Number("cornerRadius"),
            Fill = theme.Resolve(settings.String("color"), theme.Accent),
            PeakFill = settings.Bool("peaks") ? theme.Resolve(settings.String("peakColor"), theme.Foreground) : null,
        };
        Children.Add(_bars);

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(1 / settings.Number("fps")) };
        _timer.Tick += (_, _) => Frame();
        // Only listen while on screen: a hidden page, a closed preview or a snapshot captures nothing.
        IsVisibleChanged += (_, _) => Listen(IsVisible);
        Unloaded += (_, _) => Listen(false);
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
    }

    private void Listen(bool on)
    {
        if (on == _listening)
            return;
        _listening = on;
        if (on)
        {
            AudioLoopback.Shared.Acquire();
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            AudioLoopback.Shared.Release();
            _bars.Clear();
        }
    }

    private void Frame()
    {
        var count = AudioLoopback.Shared.Read(_samples);
        if (count == 0)
            Array.Clear(_target);
        else
        {
            var rate = AudioLoopback.Shared.SampleRate;
            if (_spectrum is null || _spectrum.BandCount != _target.Length || _rate != rate)
            {
                _spectrum = new Spectrum(FftSize, _target.Length, rate);
                _rate = rate;
            }
            _spectrum.Compute(_samples.AsSpan(0, count), _target);
        }
        _bars.Show(_target, _fall);
    }

    /// <summary>Draws the bars, and their peaks, in one pass.</summary>
    private sealed class VisualizerElement(int bands) : FrameworkElement
    {
        private readonly double[] _levels = new double[bands];
        private readonly double[] _peaks = new double[bands];

        public bool Mirror { get; init; }

        public double Gap { get; init; }

        public double Radius { get; init; }

        public required Brush Fill { get; init; }

        public Brush? PeakFill { get; init; }

        public void Show(ReadOnlySpan<double> target, double fall)
        {
            Spectrum.Smooth(_levels, target, fall);
            // Peaks hold the highest point and drift down at a third of the speed.
            Spectrum.Smooth(_peaks, _levels, fall / 3);
            InvalidateVisual();
        }

        public void Clear()
        {
            Array.Clear(_levels);
            Array.Clear(_peaks);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var width = ActualWidth;
            var height = ActualHeight;
            if (width <= 0 || height <= 0 || _levels.Length == 0)
                return;
            var barWidth = Math.Max(1, (width - Gap * (_levels.Length - 1)) / _levels.Length);
            var peakHeight = Math.Max(2, height / 60);
            for (var i = 0; i < _levels.Length; i++)
            {
                var x = i * (barWidth + Gap);
                var h = _levels[i] * height;
                if (h >= 0.5)
                {
                    var y = Mirror ? (height - h) / 2 : height - h;
                    dc.DrawRoundedRectangle(Fill, null, new Rect(x, y, barWidth, h), Radius, Radius);
                }
                if (PeakFill is not null && _peaks[i] * height >= 1)
                {
                    var p = _peaks[i] * height;
                    var top = Mirror ? (height - p) / 2 : height - p;
                    dc.DrawRectangle(PeakFill, null, new Rect(x, Math.Max(0, top - peakHeight), barWidth, peakHeight));
                    if (Mirror)
                        dc.DrawRectangle(PeakFill, null, new Rect(x, Math.Min(height - peakHeight, top + p), barWidth, peakHeight));
                }
            }
        }
    }
}
