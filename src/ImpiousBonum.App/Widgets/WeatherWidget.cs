using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Weather;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// The weather where you are: an icon, the temperature and conditions now, and a row of days or hours ahead.
/// The location is set once for the PC (the Weather page), so every layout shows the same place.
/// </summary>
public sealed class WeatherWidget : Widget
{
    public static WidgetDescriptor Descriptor { get; } = new(
        "weather", "Weather",
        "The weather now and the days or hours ahead, from Open-Meteo.com. Set your location in the editor (select the widget) or on the Weather page.",
        900, 180,
        [
            Setting.Toggle("showCurrent", "Weather now", true, group: Setting.Content,
                help: "The big icon and temperature for now. Off leaves just the forecast, e.g. for a widget of the hours ahead beside another of the days."),
            Setting.Choice("forecast", "Forecast", "days", ["days", "hours", "none"]),
            Setting.Number("count", "Days or hours", 5, 1, 14, group: Setting.Content),
            Setting.Number("hourStep", "Hours apart", 1, 1, 12, group: Setting.Content, help: "For an hourly forecast, e.g. 3 for every three hours."),
            Setting.Toggle("showDetails", "Details", true, group: Setting.Content, help: "Conditions, today's high and low, and feels like, beside the temperature."),
            Setting.Toggle("showLocation", "Location", false, group: Setting.Content),
            Setting.Number("fontSize", "Temperature size", 80, 6, 400),
            Setting.Number("forecastSize", "Forecast size", 22, 6, 200),
            Setting.Choice("iconStyle", "Icons", "outline", ["outline", "colour"], group: Setting.Appearance,
                help: "outline draws them in the icon colour below; colour in their own: a yellow sun, white clouds, blue rain, white lightning."),
            Setting.Color("iconColor", "Icon colour", "accent", help: "For outline icons."),
            Setting.Color("textColor", "Text colour", "foreground", help: "The temperatures and conditions."),
            Setting.Color("detailColor", "Detail colour", "secondary", help: "Highs and lows, feels like, times and days, and the chance of rain."),
        ],
        (settings, theme) => new WeatherWidget(settings, theme));

    private readonly string _forecast;
    private readonly int _count;
    private readonly int _hourStep;
    private readonly bool _showLocation;
    private readonly double _fontSize;
    private readonly double _forecastSize;
    private readonly Brush _iconBrush;
    private readonly bool _colourIcons;
    private readonly Brush _text;
    private readonly Brush _detail;
    private readonly bool _showCurrent;
    private readonly ContentControl _icon;
    private readonly TextBlock _temperature;
    private readonly StackPanel? _details;
    private readonly UniformGrid? _ahead;
    private WeatherReport? _shown;
    private bool _shownEmpty;

    public WeatherWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _forecast = settings.String("forecast");
        _count = (int)settings.Number("count");
        _hourStep = (int)settings.Number("hourStep");
        _showLocation = settings.Bool("showLocation");
        _fontSize = settings.Number("fontSize");
        _forecastSize = settings.Number("forecastSize");
        _iconBrush = theme.Resolve(settings.String("iconColor"), theme.Accent);
        _colourIcons = settings.String("iconStyle") == "colour";
        _text = theme.Resolve(settings.String("textColor"), theme.Foreground);
        _detail = theme.Resolve(settings.String("detailColor"), theme.Secondary);
        _showCurrent = settings.Bool("showCurrent");

        var now = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _icon = new ContentControl { Focusable = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, _fontSize * 0.2, 0) };
        _temperature = CreateText(_fontSize, _text);
        _temperature.VerticalAlignment = VerticalAlignment.Center;
        now.Children.Add(_icon);
        now.Children.Add(_temperature);
        if (settings.Bool("showDetails") || _showLocation)
        {
            _details = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(_fontSize * 0.25, 0, 0, 0) };
            now.Children.Add(_details);
        }

        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (_showCurrent)
            Children.Add(now);

        if (_forecast != "none")
        {
            // Without the weather now, the forecast has the whole width.
            _ahead = new UniformGrid { Rows = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(_showCurrent ? _fontSize * 0.5 : 0, 0, 0, 0) };
            SetColumn(_ahead, _showCurrent ? 1 : 0);
            if (!_showCurrent)
                SetColumnSpan(_ahead, 2);
            Children.Add(_ahead);
        }
        ShowEmpty();
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        var report = store.GetAttachment(WeatherProvider.Forecast) as WeatherReport;
        if (report is null)
        {
            if (!_shownEmpty)
                ShowEmpty();
            return;
        }
        if (ReferenceEquals(report, _shown))
            return;
        _shown = report;
        _shownEmpty = false;
        Show(report, store.TryGet(WeatherProvider.Location, out var location) ? location.Text : null);
    }

    private void ShowEmpty()
    {
        _shown = null;
        _shownEmpty = true;
        _icon.Content = Icon("cloud", _fontSize * 0.9, Theme.Secondary);
        _temperature.Text = "—";
        _details?.Children.Clear();
        _details?.Children.Add(Line("No weather yet", _fontSize * 0.3, _text));
        _details?.Children.Add(Line("Set a location on the Weather page", _fontSize * 0.22, _detail));
        _ahead?.Children.Clear();
        // With only a forecast showing, say why it's empty there instead.
        if (!_showCurrent && _ahead is not null)
            _ahead.Children.Add(Line("No weather yet: set a location on the Weather page", _forecastSize, _detail));
    }

    private void Show(WeatherReport report, string? location)
    {
        _icon.Content = Icon(WeatherCodes.Icon(report.Code, report.IsDay), _fontSize * 0.9, _iconBrush);
        _temperature.Text = Degrees(report.Temperature);

        if (_details is not null)
        {
            _details.Children.Clear();
            if (Settings.Bool("showDetails"))
            {
                _details.Children.Add(Line(WeatherCodes.Describe(report.Code), _fontSize * 0.3, _text));
                var range = report.Today is { } today ? $"H {Degrees(today.Temperature)} · L {Degrees(today.Low)}" : null;
                _details.Children.Add(Line(string.Join("  ·  ", new[] { range, $"Feels {Degrees(report.FeelsLike)}" }.OfType<string>()), _fontSize * 0.22, _detail));
            }
            if (_showLocation && !string.IsNullOrEmpty(location))
                _details.Children.Add(Line(location.Split(',')[0], _fontSize * 0.22, _detail));
        }

        if (_ahead is null)
            return;
        _ahead.Children.Clear();
        var periods = _forecast == "hours"
            ? report.HoursAhead.Where((_, i) => i % _hourStep == _hourStep - 1).Take(_count)
            : report.Days.Take(_count);
        foreach (var period in periods)
            _ahead.Children.Add(Column(period, report));
    }

    /// <summary>One day or hour: when, an icon, the temperature (and low), and the chance of rain if it's worth mentioning.</summary>
    private StackPanel Column(WeatherPeriod period, WeatherReport report)
    {
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var when = _forecast == "hours"
            ? period.Time.ToString("t", CultureInfo.CurrentCulture)
            : period.Time.Date == report.Time.Date ? "Today" : period.Time.ToString("ddd", CultureInfo.CurrentCulture);
        column.Children.Add(Line(when, _forecastSize * 0.85, _detail, HorizontalAlignment.Center));

        var icon = Icon(WeatherCodes.Icon(period.Code, period.IsDay), _forecastSize * 1.7, _iconBrush);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.Margin = new Thickness(0, _forecastSize * 0.15, 0, _forecastSize * 0.15);
        column.Children.Add(icon);

        var temperature = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center };
        temperature.Inlines.Add(Run(Degrees(period.Temperature), _text));
        if (period.Low is { } low)
            temperature.Inlines.Add(Run(" " + Degrees(low), _detail));
        ApplyFont(temperature, _forecastSize);
        column.Children.Add(temperature);

        if (period.RainChance is >= 30 and var chance)
            column.Children.Add(Line($"{chance:0}%", _forecastSize * 0.75, _detail, HorizontalAlignment.Center));
        return column;
    }

    private FrameworkElement Icon(string name, double size, Brush brush) =>
        _colourIcons ? Icons.CreateColour(name, size, 1.6, brush) : Icons.Create(name, brush, size, 1.6);

    private TextBlock Line(string text, double size, Brush brush, HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        var line = CreateText(size, brush, alignment);
        line.Text = text;
        line.TextTrimming = TextTrimming.CharacterEllipsis;
        return line;
    }

    private void ApplyFont(TextBlock text, double size)
    {
        text.FontFamily = Theme.FontFamily;
        text.FontWeight = Theme.FontWeight;
        text.FontSize = size;
    }

    private static System.Windows.Documents.Run Run(string text, Brush brush) => new(text) { Foreground = brush };

    /// <summary>"12°", rounded; the units are whatever the forecast was fetched in.</summary>
    internal static string Degrees(double? value) =>
        // Adding 0.0 turns the -0 that rounding -0.4 gives into 0, so it doesn't read "-0°".
        value is double v && !double.IsNaN(v) ? (Math.Round(v) + 0.0).ToString("0", CultureInfo.CurrentCulture) + "°" : "—";
}
