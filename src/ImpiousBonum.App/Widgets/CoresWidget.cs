using System.Windows;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// Every logical processor's load at once: a row of bars, or a grid of cells coloured from the theme's track colour
/// (idle) to its accent (busy). Shows how evenly work is spread, which one overall percentage hides.
/// </summary>
public sealed class CoresWidget : Widget
{
    public static WidgetDescriptor Descriptor { get; } = new(
        "cores", "CPU threads",
        "Every logical processor's load at once, as bars or a heatmap: shows whether work is spread out or stuck on a few threads.",
        420, 160,
        [
            Setting.Choice("style", "Style", "bars", ["bars", "heatmap"], group: Setting.Appearance,
                help: "bars fill up from the bottom; heatmap is a grid of cells that go from the track colour to the accent as they get busy."),
            Setting.Number("rows", "Heatmap rows", 0, 0, 64, group: Setting.Appearance, help: "0 picks a grid that suits the box."),
            Setting.Number("gap", "Gap", 3, 0, 50, group: Setting.Appearance),
            Setting.Number("cornerRadius", "Corner radius", 2, 0, 50, group: Setting.Appearance),
            Setting.Color("color", "Busy colour", "accent"),
            Setting.Color("trackColor", "Idle colour", "track", help: "An idle thread's bar track or heatmap cell. track is the theme's bar track."),
            Setting.Thresholds("Changes a thread's colour. An empty metric uses that thread's load."),
        ],
        (settings, theme) => new CoresWidget(settings, theme));

    private readonly CoresElement _cores;
    private readonly ThresholdColors _thresholds;
    private readonly Brush _busy;

    public CoresWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _thresholds = new ThresholdColors(settings, theme);
        _busy = theme.Resolve(settings.String("color"), theme.Accent);
        _cores = new CoresElement
        {
            Heatmap = settings.String("style") == "heatmap",
            Rows = (int)settings.Number("rows"),
            Gap = settings.Number("gap"),
            Radius = settings.Number("cornerRadius"),
            Track = theme.Resolve(settings.String("trackColor"), theme.Track),
        };
        Children.Add(_cores);
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        var loads = new List<(double Load, Brush Color)>();
        for (var i = 0; store.TryGet(CpuProvider.CoreLoad(i), out var sample); i++)
        {
            var load = sample.Value is double v && !double.IsNaN(v) ? Math.Clamp(v / 100, 0, 1) : 0;
            loads.Add((load, _thresholds.Pick(store, CpuProvider.CoreLoad(i), _busy)));
        }
        _cores.Show(loads);
    }

    /// <summary>A grid with this many rows and columns for <paramref name="count"/> cells in a box of this shape: cells as square as possible.</summary>
    internal static (int Rows, int Columns) Grid(int count, double width, double height, int rows)
    {
        if (count <= 0)
            return (0, 0);
        if (rows > 0)
            return (Math.Min(rows, count), (int)Math.Ceiling(count / (double)Math.Min(rows, count)));
        var best = (Rows: 1, Columns: count);
        var bestScore = double.MaxValue;
        for (var r = 1; r <= count; r++)
        {
            var c = (int)Math.Ceiling(count / (double)r);
            // How far the cells are from square, plus a little for empty cells.
            var aspect = width / c / (height / r);
            var score = Math.Abs(Math.Log(aspect)) + (r * c - count) * 0.05;
            if (score < bestScore)
            {
                bestScore = score;
                best = (r, c);
            }
        }
        return best;
    }

    private sealed class CoresElement : FrameworkElement
    {
        private IReadOnlyList<(double Load, Brush Color)> _loads = [];

        public bool Heatmap { get; init; }

        public int Rows { get; init; }

        public double Gap { get; init; }

        public double Radius { get; init; }

        public Brush Track { get; init; } = Brushes.DimGray;

        public void Show(IReadOnlyList<(double Load, Brush Color)> loads)
        {
            if (loads.Count == _loads.Count && loads.Zip(_loads).All(p => Math.Abs(p.First.Load - p.Second.Load) < 0.005 && ReferenceEquals(p.First.Color, p.Second.Color)))
                return;
            _loads = loads;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var count = _loads.Count;
            if (count == 0 || ActualWidth <= 0 || ActualHeight <= 0)
                return;

            var (rows, columns) = Heatmap ? Grid(count, ActualWidth, ActualHeight, Rows) : (1, count);
            var cellWidth = (ActualWidth - Gap * (columns - 1)) / columns;
            var cellHeight = (ActualHeight - Gap * (rows - 1)) / rows;
            if (cellWidth <= 0 || cellHeight <= 0)
                return;

            for (var i = 0; i < count; i++)
            {
                var (load, color) = _loads[i];
                var cell = new Rect((i % columns) * (cellWidth + Gap), (i / columns) * (cellHeight + Gap), cellWidth, cellHeight);
                dc.DrawRoundedRectangle(Track, null, cell, Radius, Radius);
                if (Heatmap)
                {
                    // The busy colour over the track, as strong as the load.
                    dc.PushOpacity(load);
                    dc.DrawRoundedRectangle(color, null, cell, Radius, Radius);
                    dc.Pop();
                }
                else if (load > 0)
                {
                    var filled = cell with { Y = cell.Bottom - cell.Height * load, Height = cell.Height * load };
                    dc.DrawRoundedRectangle(color, null, filled, Math.Min(Radius, filled.Height / 2), Math.Min(Radius, filled.Height / 2));
                }
            }
        }
    }
}
