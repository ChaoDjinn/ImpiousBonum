using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// The rain in the next two hours at the weather's location: a bar for each quarter of an hour, taller the heavier the
/// rain, with the time every half hour under them and a line such as "Rain from 14:30" above. It's always the location's,
/// like the weather readings, even while a weather widget beside it shows another place.
/// </summary>
public sealed class RainWidget : Widget
{
    public static WidgetDescriptor Descriptor { get; } = new(
        "rain", "Rain soon",
        "The rain in the next two hours at your location, from Open-Meteo.com: a bar for each quarter of an hour, taller the heavier the rain, under a line such as Rain from 14:30. Set your location in the editor (select the widget) or on the Weather page.",
        420, 160,
        [
            Setting.Toggle("showSummary", "Summary", true, group: Setting.Content,
                help: "The line above the bars: Dry for the next 2 hours, Rain from 14:30, Rain stopping around 15:15, or Light rain for the next 2 hours "
                    + "(moderate or heavy when it is). It says snow instead when the forecast is for snow."),
            Setting.Number("fontSize", "Font size", 22, 6, 200, help: "The summary's; the times under the bars are a little smaller."),
            Setting.ShrinkToFit(),
            Setting.Color("barColor", "Bar colour", "accent"),
            Setting.Color("textColor", "Text colour", "foreground", help: "The summary."),
            Setting.Color("timeColor", "Time colour", "secondary", help: "The times under the bars."),
            Setting.Color("trackColor", "Baseline colour", "track", help: "The line the bars stand on, which is all there is while it's dry."),
        ],
        (settings, theme) => new RainWidget(settings, theme));

    /// <summary>What the times under the bars are, to the summary's size.</summary>
    private const double TimeScale = 0.75;

    private readonly bool _showSummary;
    private readonly TextBlock _summary;

    /// <summary>The summary, in its shrink to fit when it has one: what's hidden, so its margin goes too.</summary>
    private readonly FrameworkElement _summaryLine;
    private readonly Chart _chart;

    /// <summary>The forecast shown, and the quarter-hour at the place it was shown for: what's shown changes with either.</summary>
    private WeatherReport? _shown;
    private DateTime _quarter;
    private bool _refreshed;

    public RainWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _showSummary = settings.Bool("showSummary");
        var fontSize = settings.Number("fontSize");
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _summary = CreateText(fontSize, theme.Resolve(settings.String("textColor"), theme.Foreground));
        _summary.TextTrimming = TextTrimming.CharacterEllipsis;
        _summary.Margin = new Thickness(0, 0, 0, fontSize * 0.45);
        _summaryLine = Fit(_summary, FitDirection.Width);
        Children.Add(_summaryLine);

        _chart = new Chart
        {
            Bars = theme.Resolve(settings.String("barColor"), theme.Accent),
            Times = theme.Resolve(settings.String("timeColor"), theme.Secondary),
            Track = theme.Resolve(settings.String("trackColor"), theme.Track),
            Typeface = new Typeface(theme.FontFamily, FontStyles.Normal, theme.FontWeight, FontStretches.Normal),
            TimeSize = fontSize * TimeScale,
            ShrinkText = settings.Bool(Setting.ShrinkToFitKey),
        };
        SetRow(_chart, 1);
        Children.Add(_chart);
        // The bars take what's left, and everything shrinks when there isn't room for the summary and a few bars.
        FitContent(FitDirection.Height);
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        // Always the main place's forecast: the rain is your location's, like the weather readings.
        var report = store.GetAttachment(WeatherProvider.Forecast) as WeatherReport;
        var local = report?.LocalTime(new DateTimeOffset(now.ToUniversalTime()));
        var quarter = local is { } time ? time.Date.AddMinutes(Math.Floor(time.TimeOfDay.TotalMinutes / 15) * 15) : default;
        if (_refreshed && ReferenceEquals(report, _shown) && quarter == _quarter)
            return;
        (_refreshed, _shown, _quarter) = (true, report, quarter);

        var rain = report is not null && local is { } at ? RainAhead.For(report, at) : null;
        if (rain is null)
        {
            // Said even with the summary off, as nothing else would say why there are no bars.
            _summary.Text = report is null ? "No weather yet: set a location on the Weather page" : "No rain forecast yet";
            _summaryLine.Visibility = Visibility.Visible;
            _chart.Show([], WeatherUnits.Metric);
            return;
        }
        _summary.Text = rain.Summary;
        _summaryLine.Visibility = _showSummary ? Visibility.Visible : Visibility.Collapsed;
        _chart.Show(rain.Steps, report!.Units);
    }

    /// <summary>A drizzle's bar is never shorter than this, so it can be seen.</summary>
    private const double Lowest = 0.15;

    /// <summary>
    /// How tall a bar is, from 0 to 1, for <paramref name="amount"/> in a quarter of an hour: none while it's dry, then by
    /// the square root of how fast it falls, so light rain reaches up to half way and heavy rain all the way. That keeps
    /// a drizzle and a downpour apart, where a straight line would make one too small to see or the other off the top.
    /// </summary>
    internal static double BarHeight(double amount, WeatherUnits units) =>
        RainAhead.Intensity(amount, units) == RainIntensity.Dry ? 0 : Math.Clamp(Math.Sqrt(RainAhead.MillimetresPerHour(amount, units) / 10), Lowest, 1);

    /// <summary>
    /// Draws the bars on their baseline and the time every half hour under them. There's room for two hours of
    /// quarter-hours whether the forecast reaches that far or not, so the bars are always the same width and a short
    /// forecast ends early rather than stretching.
    /// </summary>
    internal sealed class Chart : FrameworkElement
    {
        private IReadOnlyList<QuarterHour> _steps = [];
        private WeatherUnits _units;

        public required Brush Bars { get; init; }

        public required Brush Times { get; init; }

        public required Brush Track { get; init; }

        public required Typeface Typeface { get; init; }

        public double TimeSize { get; init; } = 16;

        /// <summary>Draws the times smaller when they don't fit, rather than leaving some out.</summary>
        public bool ShrinkText { get; init; } = true;

        /// <summary>Each bar's height, from 0 (dry) to 1 (the top); for tests.</summary>
        internal IReadOnlyList<double> Heights => [.. _steps.Select(s => BarHeight(s.Precipitation, _units))];

        /// <summary>The times last drawn under the bars; for tests.</summary>
        internal IReadOnlyList<string> TimesShown { get; private set; } = [];

        /// <summary>The size the times were last drawn at: <see cref="TimeSize"/>, or smaller to fit; for tests.</summary>
        internal double ShownTimeSize { get; private set; }

        public void Show(IReadOnlyList<QuarterHour> steps, WeatherUnits units)
        {
            _steps = steps;
            _units = units;
            InvalidateVisual();
        }

        /// <summary>
        /// Room for the times and bars a little taller, so with shrink to fit a widget too short for them and the summary
        /// gets smaller as a whole, rather than squashing the bars flat.
        /// </summary>
        protected override Size MeasureOverride(Size availableSize) => new(0, TimeSize * 3);

        protected override void OnRender(DrawingContext dc)
        {
            var width = ActualWidth;
            var height = ActualHeight;
            TimesShown = [];
            if (_steps.Count == 0 || width <= 0 || height <= 0)
                return;
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var slot = width / RainAhead.Length;

            // The edges between quarter-hours that are on the hour or half past, from when the first one starts.
            var first = _steps[0].Time - RainAhead.Step;
            var marks = Enumerable.Range(0, _steps.Count + 1)
                .Select(edge => (X: edge * slot, Time: first + RainAhead.Step * edge))
                .Where(mark => mark.Time.Minute % 30 == 0)
                .Select(mark => (mark.X, Text: mark.Time.ToString("t", CultureInfo.CurrentCulture)))
                .ToList();
            var size = TimeSize;
            var times = marks.Select(mark => Text(mark.Text, size, dpi)).ToList();
            if (ShrinkText && times.Count > 0)
            {
                // Each has the half hour to the next to itself, with a little room between, and they take no more than
                // half the height, leaving the rest to the bars.
                var scale = Math.Min(ShrinkToFit.ScaleFor(times.Max(t => t.Width) * 1.25, slot * 2), ShrinkToFit.ScaleFor(times[0].Height, height / 2));
                if (scale < 1)
                {
                    size = Math.Max(6, size * scale);
                    times = [.. marks.Select(mark => Text(mark.Text, size, dpi))];
                }
            }
            ShownTimeSize = size;
            // Times too tall to leave any room for the bars are left out.
            var timeHeight = times.Count > 0 && times[0].Height * 1.3 < height * 0.75 ? times[0].Height : 0;

            // The bars stand on the baseline, above the times and a little gap with a tick to each.
            var floor = height - timeHeight * 1.3;
            var line = Math.Min(2, floor);
            dc.DrawRectangle(Track, null, new Rect(0, floor - line, _steps.Count * slot, line));
            var barWidth = slot * 0.76;
            for (var i = 0; i < _steps.Count; i++)
            {
                var bar = BarHeight(_steps[i].Precipitation, _units) * floor;
                if (bar <= 0)
                    continue;
                var radius = Math.Min(Math.Min(3, barWidth / 4), bar / 2);
                dc.DrawRoundedRectangle(Bars, null, new Rect(i * slot + (slot - barWidth) / 2, floor - bar, barWidth, bar), radius, radius);
            }

            if (timeHeight <= 0)
                return;
            // Centred under their edges, but kept inside the chart, and one that would run into the last is left out.
            var shown = new List<string>();
            var right = double.NegativeInfinity;
            var tick = timeHeight * 0.2;
            for (var i = 0; i < marks.Count; i++)
            {
                var text = times[i];
                var x = Math.Clamp(marks[i].X - text.Width / 2, 0, Math.Max(0, width - text.Width));
                if (x < right + size * 0.4)
                    continue;
                right = x + text.Width;
                var tickX = Math.Clamp(marks[i].X, 0.5, width - 0.5);
                dc.DrawRectangle(Times, null, new Rect(tickX - 0.5, floor, 1, tick));
                dc.DrawText(text, new Point(x, floor + timeHeight * 0.3));
                shown.Add(marks[i].Text);
            }
            TimesShown = shown;
        }

        private FormattedText Text(string text, double size, double pixelsPerDip) =>
            new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface, size, Times, pixelsPerDip);
    }
}
