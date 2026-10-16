using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// The sun's day and the moon: an arc from sunrise to sunset with the sun on it while it's up and the day's length
/// inside, and beside it the moon as it looks from the weather's location, with its phase, how much of it is lit and the
/// next full moon. Both are worked out on the PC (see <see cref="MoonProvider"/>); the sun needs a location, the moon
/// doesn't.
/// </summary>
public sealed class SunMoonWidget : Widget
{
    public static WidgetDescriptor Descriptor { get; } = new(
        "sun", "Sun and moon",
        "The day from sunrise to sunset with the sun on its way across, and the moon as it looks tonight, with its phase and the next full moon. Worked out on your PC: the sun needs your location (the Weather page), the moon doesn't.",
        560, 200,
        [
            Setting.Choice("show", "Show", "both", ["both", "sun", "moon"], group: Setting.Content,
                help: "both has the sun's day on the left and the moon on the right; sun or moon shows just the one."),
            Setting.Toggle("showMoonTimes", "Moonrise and moonset", true, group: Setting.Content,
                help: "Under the moon: when it rose and when it sets while it's up, or when it next rises and sets while it's down. They need your location."),
            Setting.Number("fontSize", "Text size", 20, 6, 200,
                help: "The day's length and the moon's phase; sunrise, sunset and the moon's other lines are a little smaller."),
            Setting.Number("moonSize", "Moon size", 90, 10, 600, help: "How wide the moon is."),
            Setting.Number("thickness", "Arc thickness", 6, 1, 60, group: Setting.Appearance),
            Setting.ShrinkToFit(),
            Setting.Color("color", "Sun colour", "accent", help: "The sun, and the part of the arc it's been along today."),
            Setting.Color("moonColor", "Moon colour", "secondary", help: "The moon's lit side."),
            Setting.Color("trackColor", "Track colour", "track", help: "The rest of the arc, the horizon and the moon's dark side. track is the theme's bar track."),
            Setting.Color("textColor", "Text colour", "foreground", help: "The day's length and the moon's phase."),
            Setting.Color("detailColor", "Detail colour", "secondary",
                help: "Sunrise and sunset, and the moon's other lines: how much of it is lit, the next full moon, moonrise and moonset."),
        ],
        (settings, theme) => new SunMoonWidget(settings, theme));

    private readonly SunArcElement? _arc;
    private readonly MoonElement? _moon;
    private readonly TextBlock? _phase;
    private readonly TextBlock? _lit;
    private readonly TextBlock? _nextFull;
    private readonly TextBlock? _moonTimes;

    /// <summary>The moon and its lines, shrinking together when shrink to fit is on, and kept to part of the width beside the sun.</summary>
    private readonly FrameworkElement? _moonPart;

    /// <summary>What's on show, so the moon is only drawn again when a new report comes (once a minute).</summary>
    private SkyReport? _shown;
    private bool _shownEmpty;

    public SunMoonWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        var show = settings.String("show");
        var fontSize = settings.Number("fontSize");
        var track = theme.Resolve(settings.String("trackColor"), theme.Track);
        var text = theme.Resolve(settings.String("textColor"), theme.Foreground);
        var detail = theme.Resolve(settings.String("detailColor"), theme.Secondary);
        var shrink = settings.Bool(Setting.ShrinkToFitKey);

        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (show != "moon")
        {
            _arc = new SunArcElement
            {
                Thickness = settings.Number("thickness"),
                SunBrush = theme.Resolve(settings.String("color"), theme.Accent),
                Track = track,
                Typeface = new Typeface(theme.FontFamily, FontStyles.Normal, theme.FontWeight, FontStretches.Normal),
                TextSize = fontSize,
                TextBrush = text,
                DetailBrush = detail,
                ShrinkText = shrink,
            };
            // Without the moon, the sun has the whole width.
            SetColumnSpan(_arc, show == "sun" ? 2 : 1);
            Children.Add(_arc);
        }
        if (show != "sun")
        {
            var size = settings.Number("moonSize");
            _moon = new MoonElement
            {
                Diameter = size,
                Lit = theme.Resolve(settings.String("moonColor"), theme.Secondary),
                Dark = track,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(fontSize * 0.6, 0, 0, 0) };
            _phase = Line(fontSize, text);
            _lit = Line(fontSize * 0.8, detail);
            _nextFull = Line(fontSize * 0.8, detail);
            lines.Children.Add(_phase);
            lines.Children.Add(_lit);
            lines.Children.Add(_nextFull);
            if (settings.Bool("showMoonTimes"))
            {
                _moonTimes = Line(fontSize * 0.8, detail);
                lines.Children.Add(_moonTimes);
            }
            var moon = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                // Beside the sun it keeps to the right; on its own it's in the middle.
                HorizontalAlignment = _arc is null ? HorizontalAlignment.Center : HorizontalAlignment.Right,
                Margin = new Thickness(_arc is null ? 0 : fontSize, 0, 0, 0),
            };
            moon.Children.Add(_moon);
            moon.Children.Add(lines);
            SetColumn(moon, _arc is null ? 0 : 1);
            if (_arc is null)
                SetColumnSpan(moon, 2);
            _moonPart = Fit(moon, FitDirection.Both);
            Children.Add(_moonPart);
        }
        ShowEmpty();
    }

    /// <summary>Beside the sun, the moon gets no more than three fifths of the width, shrinking to fit it if it must.</summary>
    protected override Size MeasureOverride(Size constraint)
    {
        if (_arc is not null && _moonPart is not null)
            _moonPart.MaxWidth = double.IsInfinity(constraint.Width) ? double.PositiveInfinity : constraint.Width * 0.6;
        return base.MeasureOverride(constraint);
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        var sky = store.GetAttachment(MoonProvider.Sky) as SkyReport;
        if (sky is null)
        {
            if (!_shownEmpty)
                ShowEmpty();
            return;
        }
        var time = new DateTimeOffset(now);
        if (!ReferenceEquals(sky, _shown))
        {
            _shown = sky;
            _shownEmpty = false;
            ShowMoon(sky);
        }
        // The sun moves along every second; the arc only redraws when it's moved far enough to see.
        if (_arc is not null)
            ShowSun(sky.Sun, time);
        if (_moonTimes is not null && MoonTimes(sky.Moon, time) is var times && times != _moonTimes.Text)
        {
            _moonTimes.Text = times;
            _moonTimes.Visibility = times.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>Before there's any report: an empty arc and moon.</summary>
    private void ShowEmpty()
    {
        _shown = null;
        _shownEmpty = true;
        _arc?.Update(null, string.Empty, string.Empty, string.Empty, string.Empty);
        _moon?.Update(0, true);
        if (_phase is not null)
            _phase.Text = MetricFormatter.Missing;
        if (_lit is not null && _nextFull is not null)
            _lit.Text = _nextFull.Text = string.Empty;
        if (_moonTimes is not null)
        {
            _moonTimes.Text = string.Empty;
            _moonTimes.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowMoon(SkyReport sky)
    {
        if (_moon is null)
            return;
        var moon = sky.Moon;
        // Seen from the southern hemisphere it's upside down, so it waxes on the left.
        _moon.Update(moon.Illumination, moon.Waxing != sky.Southern);
        _phase!.Text = moon.Phase;
        _lit!.Text = $"{Math.Round(moon.Illumination * 100):0}% lit";
        _nextFull!.Text = $"Next full moon {MoonProvider.Date(moon.NextFull, TimeZoneInfo.Local)}";
    }

    /// <summary>
    /// "Rose 15:24 · sets 02:36" while the moon's up, "Rises 15:24 · sets 02:36" while it's down; empty without a
    /// location, so it takes no room.
    /// </summary>
    internal static string MoonTimes(MoonReport moon, DateTimeOffset now)
    {
        if (moon.Rise is null && moon.Set is null)
            return string.Empty;
        var rise = moon.Rise is { } rose ? MoonProvider.TimeOfDay(rose, TimeZoneInfo.Local) : MetricFormatter.Missing;
        var set = moon.Set is { } sets ? MoonProvider.TimeOfDay(sets, TimeZoneInfo.Local) : MetricFormatter.Missing;
        return $"{(moon.Rise <= now ? "Rose" : "Rises")} {rise} · sets {set}";
    }

    private void ShowSun(SunDay? day, DateTimeOffset now)
    {
        if (day is null)
        {
            _arc!.Update(null, string.Empty, string.Empty, "Set a location", "on the Weather page");
            return;
        }
        var (sun, text, caption) = Sun(day, now);
        _arc!.Update(sun,
            day.Rise is { } rise ? MoonProvider.TimeOfDay(rise, TimeZoneInfo.Local) : string.Empty,
            day.Set is { } set ? MoonProvider.TimeOfDay(set, TimeZoneInfo.Local) : string.Empty,
            text, caption);
    }

    /// <summary>
    /// How far the sun is through <paramref name="day"/> at <paramref name="now"/>, from 0 at sunrise to 1 at sunset (null
    /// while it's down), and what goes inside the arc: the day's length, or on a day with no sunrise or sunset in the
    /// far north or south, that it's up or down all day.
    /// </summary>
    internal static (double? Sun, string Text, string Caption) Sun(SunDay day, DateTimeOffset now)
    {
        if (day.Rise is null && day.Set is null)
        {
            // Up all day, it goes round with the clock; down all day, it doesn't show.
            return day.Daylight > TimeSpan.Zero
                ? (Fraction(now, day.Start, day.End), "Up all day", string.Empty)
                : (null, "Down all day", string.Empty);
        }
        var from = day.Rise ?? day.Start;
        var to = day.Set is { } set && set > from ? set : day.End;
        return (Fraction(now, from, to), WeatherReadings.Daylight(day.Daylight), "daylight");
    }

    /// <summary>How far <paramref name="now"/> is from <paramref name="from"/> to <paramref name="to"/>, or null outside them.</summary>
    private static double? Fraction(DateTimeOffset now, DateTimeOffset from, DateTimeOffset to) =>
        now >= from && now <= to && to > from ? (now - from) / (to - from) : null;

    private TextBlock Line(double size, Brush brush)
    {
        var line = CreateText(size, brush);
        line.TextTrimming = TextTrimming.CharacterEllipsis;
        return line;
    }
}
