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
/// The weather where you are: an icon, the temperature and conditions now, a row of days or hours ahead, and below them
/// the most serious severe weather warning while there is one. The location is set once for the PC (the Weather page),
/// so every layout shows the same place. It can move between the location and other places saved there too, one at a
/// time, moving on every few seconds or when tapped.
/// </summary>
public sealed class WeatherWidget : Widget
{
    /// <summary>The names the editor offers for <c>places</c>: the location's, then the Weather page's more places, in its order.</summary>
    public const string PlaceNames = "weatherPlaces";

    public static WidgetDescriptor Descriptor { get; } = new(
        "weather", "Weather",
        "The weather now and the days or hours ahead, from Open-Meteo.com, and severe weather warnings if they're on. Set your location in the editor (select the widget) or on the Weather page, and add more places there to move between.",
        900, 180,
        [
            Setting.Toggle("showCurrent", "Weather now", true, group: Setting.Content,
                help: "The big icon and temperature for now. Off leaves just the forecast, e.g. for a widget of the hours ahead beside another of the days."),
            Setting.Choice("forecast", "Forecast", "days", ["days", "hours", "none"]),
            Setting.Number("count", "Days or hours", 5, 1, 48, group: Setting.Content, help: "Up to 16 days, or 48 hours."),
            Setting.Number("hourStep", "Hours apart", 1, 1, 12, group: Setting.Content, help: "For an hourly forecast, e.g. 3 for every three hours."),
            Setting.Number("rainFrom", "Chance of rain from", 30, 0, 100, group: Setting.Content,
                help: "Show the chance of rain from this percentage; 0 always shows it."),
            Setting.Toggle("showDetails", "Details", true, group: Setting.Content, help: "Conditions, today's high and low, and feels like, beside the temperature."),
            Setting.Toggle("showLocation", "Location", false, group: Setting.Content),
            Setting.Picks("places", "Places", null, source: PlaceNames, none: "No location yet: set one on the Weather page.",
                help: "Which places to show, in order: your location and the more places on the Weather page, by their names up to the first comma, such as Leeds "
                    + "(or Paris (Texas) where two places would share one). None shows just your location. "
                    + "With more than one, the widget shows one at a time with its name, moving on every Seconds per place, and a tap moves it (and any other weather widget showing the same places) on straight away; "
                    + "the tablet view moves on by itself. A place other than your location always shows its name. "
                    + "Warnings are your location's, so they only show while it's on show."),
            Setting.Number("placeSeconds", "Seconds per place", 10, 3, 600, group: Setting.Content, help: "With more than one place, how long each one shows."),
            Setting.Toggle("showWarnings", "Warnings", true, group: Setting.Content,
                help: "With severe weather warnings on (the Weather page), the most serious one along the bottom while there are any: in the warning colour, or the critical colour for red, severe and extreme ones. "
                    + "Only with Weather now, so a forecast-only widget beside it doesn't show it again. "
                    + "Its size follows Forecast size, or Temperature size without a forecast. Tap it to read the warning (not in the tablet view)."),
            Setting.Number("fontSize", "Temperature size", 80, 6, 400),
            Setting.Number("forecastSize", "Forecast size", 22, 6, 200),
            Setting.ShrinkToFit(),
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
    private readonly double _rainFrom;
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
    private readonly TapTarget? _warning;

    /// <summary>The weather now and the forecast, shrinking together, when shrink to fit is on.</summary>
    private readonly ShrinkToFit? _fitted;
    private WeatherReport? _shown;
    private bool _shownEmpty;

    /// <summary>What the warning strip shows (the most serious warning and how many there are), or null while it's empty.</summary>
    private string? _warningShown;
    private string? _warningLink;

    /// <summary>The places chosen; none for just the main place.</summary>
    private readonly PickList _places;
    private readonly TimeSpan _placeTime;

    /// <summary>Whether more than one place is chosen, so it moves between them, has more to tap and always names them.</summary>
    private readonly bool _moves;

    /// <summary>A forecast-only widget's place name, above the forecast, while it names one (see <see cref="OnShow"/>).</summary>
    private readonly TextBlock? _placeLine;

    /// <summary>
    /// What the places chosen were last found among (the provider's list of them, or without one the location's name),
    /// and which they are in it, with every place's short name.
    /// </summary>
    private object? _choiceFor;
    private IReadOnlyList<int> _chosen = [];
    private IReadOnlyList<string> _shortNames = [];

    /// <summary>Which of the places chosen is on show, and since when; it moves on with time, or a tap.</summary>
    private int _place;
    private DateTime _placeSince;

    /// <summary>The last refresh's readings and time, so a tap can move on to the next place straight away.</summary>
    private MetricStore? _store;
    private DateTime _refreshed;

    /// <summary>The place's name shown with the weather, or null for none.</summary>
    private string? _nameShown;

    public WeatherWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _places = settings.Picks("places");
        _placeTime = TimeSpan.FromSeconds(settings.Number("placeSeconds"));
        _moves = _places.Names.Count > 1;
        // With any place chosen there may be one other than the location on show, which is always named.
        var named = _places.Names.Count > 0;
        _forecast = settings.String("forecast");
        _count = (int)settings.Number("count");
        _hourStep = (int)settings.Number("hourStep");
        _rainFrom = settings.Number("rainFrom");
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
        // The place's name goes with the details, so one with places chosen has them even without Details or Location.
        if (settings.Bool("showDetails") || _showLocation || named)
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
            FrameworkElement forecast = _ahead;
            if (!_showCurrent && named)
            {
                // Without the weather now there are no details to name the place in, so its name goes above the forecast.
                _placeLine = Line(string.Empty, _forecastSize * 0.85, _detail);
                _placeLine.Visibility = Visibility.Collapsed;
                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(_placeLine);
                stack.Children.Add(_ahead);
                forecast = stack;
            }
            SetColumn(forecast, _showCurrent ? 1 : 0);
            if (!_showCurrent)
                SetColumnSpan(forecast, 2);
            Children.Add(forecast);
        }
        // A wide font or a big temperature makes the weather now and the forecast smaller together, rather than squeezing the forecast.
        FitContent(FitDirection.Both, VerticalAlignment.Center);
        _fitted = Children.OfType<ShrinkToFit>().FirstOrDefault();
        if (_moves)
            TapForNextPlace();

        // Only with the weather now: a forecast-only widget is usually the second of two, and a warning should show once.
        if (settings.Bool("showWarnings") && _showCurrent)
        {
            // Under the rest, outside its fit so it keeps its size, and only there while there's a warning (see PlaceWarning).
            _warning = new TapTarget { Background = Brushes.Transparent, Visibility = Visibility.Collapsed, Margin = new Thickness(0, WarningSize / 3, 0, 0) };
            _warning.Tapped += (_, _) =>
            {
                if (_warningLink is { } link)
                    RequestAction(new WidgetAction(WidgetAction.Open, link));
            };
            SetColumnSpan(_warning, 2);
            Children.Add(_warning);
        }
        ShowEmpty();
    }

    /// <summary>
    /// With a warning, the weather and the warning under it are centred together; without one, the widget has no rows at
    /// all, so it's laid out just as it always was.
    /// </summary>
    private void PlaceWarning(bool shown)
    {
        RowDefinitions.Clear();
        if (shown)
        {
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }
        foreach (UIElement child in Children)
            SetRow(child, !shown ? 0 : child == _warning ? 2 : 1);
    }

    /// <summary>
    /// When the weather and the warning are taller than the widget, the weather gives way rather than the warning going
    /// off the bottom, where it couldn't be seen at all: with shrink to fit it gets smaller to fit above the warning,
    /// without it its top and bottom are cut off.
    /// </summary>
    protected override Size MeasureOverride(Size constraint)
    {
        var room = double.PositiveInfinity;
        if (_warning is { Visibility: Visibility.Visible } && RowDefinitions.Count == 4)
        {
            _warning.Measure(new Size(constraint.Width, double.PositiveInfinity));
            room = Math.Max(0, constraint.Height - _warning.DesiredSize.Height);
            RowDefinitions[1].MaxHeight = room;
        }
        // The row is sized to its content, so the fit is only told the room it has through its own MaxHeight.
        if (_fitted is not null)
            _fitted.MaxHeight = room;
        return base.MeasureOverride(constraint);
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        _store = store;
        _refreshed = now;
        var (report, name, main) = OnShow(store, now);

        if (_warning is not null)
        {
            // The warnings are the main place's, so they only show with it.
            var warnings = main ? store.GetAttachment(WeatherWarningsProvider.Warnings) as IReadOnlyList<WeatherWarning> : null;
            // The list is replaced on every sample; rebuild only when what's shown changes.
            var key = warnings is [var worst, ..] ? $"{worst.Id}|{worst.Level}|{worst.Headline}|{worst.Link}|{warnings.Count}" : null;
            if (key != _warningShown)
            {
                _warningShown = key;
                ShowWarnings(warnings);
            }
        }

        if (report is null)
        {
            if (!_shownEmpty || name != _nameShown)
                ShowEmpty(name);
            return;
        }
        if (ReferenceEquals(report, _shown) && name == _nameShown)
            return;
        _shown = report;
        _shownEmpty = false;
        Show(report, name);
    }

    /// <summary>
    /// The place on show: its forecast, the name to show with it (null for none), and whether it's the main place. With no
    /// places chosen, or none of them found, it's the main place, named only with Location on, as it always was.
    /// Otherwise it's named while it moves between places, and whenever it isn't the main place, so another place's
    /// weather never looks like your own (with only one of several chosen still found, say).
    /// </summary>
    private (WeatherReport? Report, string? Name, bool Main) OnShow(MetricStore store, DateTime now)
    {
        var report = store.GetAttachment(WeatherProvider.Forecast) as WeatherReport;
        var location = store.TryGet(WeatherProvider.Location, out var sample) ? sample.Text : null;
        if (_places.Names.Count > 0)
        {
            // Without more places there's just the main place, by the name its forecast came with (none before it has one).
            var attached = store.GetAttachment(WeatherProvider.Places) as IReadOnlyList<PlaceForecast>;
            var places = attached ?? (location is null ? [] : [new PlaceForecast(location, report)]);
            // The provider keeps the same list until a place or a forecast changes, so this is only worked out again then.
            var choiceFor = (object?)attached ?? location;
            if (!Equals(choiceFor, _choiceFor))
            {
                var names = places.Select(p => p.Name).ToList();
                (_choiceFor, _chosen, _shortNames) = (choiceFor, Chosen(_places.Names, names), ShortNames(names));
            }
            if (_chosen.Count > 0)
            {
                MoveOn(_chosen.Count, now);
                var place = _chosen[_place];
                var name = _showLocation || _moves || place != 0 ? _shortNames[place] : null;
                return (places[place].Report, name, place == 0);
            }
        }
        return (report, _showLocation ? location?.Split(',')[0] : null, true);
    }

    /// <summary>
    /// Moves on to the next of <paramref name="count"/> places every <c>placeSeconds</c>, counting from the first refresh
    /// that has more than one (and again if the clock goes back).
    /// </summary>
    private void MoveOn(int count, DateTime now)
    {
        if (count < 2)
        {
            _place = 0;
            _placeSince = default;
            return;
        }
        if (_placeSince == default || now < _placeSince)
        {
            _placeSince = now;
        }
        else if (now - _placeSince >= _placeTime)
        {
            _place++;
            // From when it was due, so refreshes that come a little late don't add up; after a long gap (asleep, say), from now.
            _placeSince = now - _placeSince < _placeTime * 2 ? _placeSince + _placeTime : now;
        }
        _place %= count;
    }

    /// <summary>
    /// A tap: on to the next place straight away, which then has its full time. Any other weather widget on the dashboard
    /// moving between the same places at the same pace moves on with it, so a pair side by side (the weather now over
    /// a forecast, say) keeps showing the same place.
    /// </summary>
    private void NextPlace()
    {
        if (_store is null)
            return;
        var place = _place + 1;
        var widgets = Parent is Panel dashboard ? dashboard.Children.OfType<WeatherWidget>().Where(MovesWith) : [this];
        foreach (var widget in widgets)
            widget.ShowPlace(place, _refreshed);
    }

    /// <summary>Whether <paramref name="other"/> moves between the same places as this one, at the same pace.</summary>
    private bool MovesWith(WeatherWidget other) =>
        other == this || (other._moves && other._placeTime == _placeTime && other._places.ToString().Equals(_places.ToString(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Shows the <paramref name="place"/>th of the places chosen (round again past the last), from <paramref name="now"/>.</summary>
    private void ShowPlace(int place, DateTime now)
    {
        if (_store is not { } store)
            return;
        _place = place;
        _placeSince = now;
        Refresh(store, now);
    }

    /// <summary>
    /// Puts everything so far, with its columns, in a tap target that moves on to the next place. The warning strip goes
    /// outside it, so a tap there still opens the warning.
    /// </summary>
    private void TapForNextPlace()
    {
        var content = new Grid();
        var columns = ColumnDefinitions.ToList();
        var children = Children.Cast<UIElement>().ToList();
        ColumnDefinitions.Clear();
        Children.Clear();
        columns.ForEach(content.ColumnDefinitions.Add);
        children.ForEach(child => content.Children.Add(child));
        var tap = new TapTarget { Child = content, Background = Brushes.Transparent };
        tap.Tapped += (_, _) => NextPlace();
        Children.Add(tap);
    }

    /// <summary>
    /// What a widget shows without a forecast. <paramref name="place"/> names a place that's on show but has none yet;
    /// without one, it's most likely there's no location yet.
    /// </summary>
    private void ShowEmpty(string? place = null)
    {
        _shown = null;
        _shownEmpty = true;
        _nameShown = place;
        _icon.Content = Icon("cloud", _fontSize * 0.9, Theme.Secondary);
        _temperature.Text = "—";
        if (_details is not null)
        {
            _details.Children.Clear();
            _details.Children.Add(Line("No weather yet", _fontSize * 0.3, _text));
            _details.Children.Add(Line(place ?? "Set a location on the Weather page", _fontSize * 0.22, _detail));
            _details.Visibility = Visibility.Visible;
        }
        ShowPlaceLine(place);
        _ahead?.Children.Clear();
        // With only a forecast showing, say why it's empty there instead.
        if (!_showCurrent && _ahead is not null)
            _ahead.Children.Add(Line(place is null ? "No weather yet: set a location on the Weather page" : "No weather yet", _forecastSize, _detail));
    }

    /// <param name="name">The place's name to show with it, or null for none.</param>
    private void Show(WeatherReport report, string? name)
    {
        _nameShown = name;
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
            if (!string.IsNullOrEmpty(name))
                _details.Children.Add(Line(name, _fontSize * 0.22, _detail));
            // Without Details, and no name to show, it takes no room (nor its margin) beside the temperature.
            _details.Visibility = _details.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        ShowPlaceLine(name);

        if (_ahead is null)
            return;
        _ahead.Children.Clear();
        var periods = _forecast == "hours"
            ? report.HoursAhead.Where((_, i) => i % _hourStep == _hourStep - 1).Take(_count)
            : report.Days.Take(_count);
        foreach (var period in periods)
            _ahead.Children.Add(Column(period, report));
    }

    /// <summary>A forecast-only widget's place name above the forecast, taking no room without one.</summary>
    private void ShowPlaceLine(string? name)
    {
        if (_placeLine is null)
            return;
        _placeLine.Text = name ?? string.Empty;
        _placeLine.Visibility = string.IsNullOrEmpty(name) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// What the editor's Places offers, and the widget shows, for each of the places <paramref name="names"/> (the main
    /// place first): its <see cref="ShortName"/>, such as Leeds, or where another's would be the same, with the next
    /// part of its name too, as in Paris (Texas), so each can be told apart and picked.
    /// </summary>
    public static IReadOnlyList<string> ShortNames(IReadOnlyList<string> names)
    {
        var first = names.Select(ShortName).ToList();
        return [.. names.Select((name, i) => first.Count(f => f.Equals(first[i], StringComparison.OrdinalIgnoreCase)) > 1 && TwoParts(name) is { } longer ? longer : first[i])];
    }

    /// <summary>
    /// A place's name up to the first comma, such as Leeds. Coordinates typed in have no name, so they keep both numbers
    /// without the comma, such as 50.21 -5.48, to tell which place it is (and so it can still be picked).
    /// </summary>
    internal static string ShortName(string name) =>
        OpenMeteo.ParseCoordinates(name) is null ? name.Split(',')[0].Trim() : string.Join(' ', name.Split(',', StringSplitOptions.TrimEntries));

    /// <summary>A place's name up to the second comma, as in Paris (Texas), or null for one with no more to it.</summary>
    private static string? TwoParts(string name) =>
        OpenMeteo.ParseCoordinates(name) is null && name.Split(',', StringSplitOptions.TrimEntries) is [var first, var second, ..] ? $"{first} ({second})" : null;

    /// <summary>Which of the places <paramref name="names"/> (the main place first) are chosen, in the order chosen; names not found are left out.</summary>
    internal static IReadOnlyList<int> Chosen(IReadOnlyList<string> chosen, IReadOnlyList<string> names)
    {
        var find = Finder(names);
        var found = new List<int>();
        foreach (var name in chosen)
        {
            if (find(name) is >= 0 and var index && !found.Contains(index))
                found.Add(index);
        }
        return found;
    }

    /// <summary>
    /// The short name of the place among <paramref name="names"/> that <paramref name="chosen"/> stands for, when it's
    /// spelt another way, or null for none: what the editor's Places ticks for it, so it agrees with the widget.
    /// </summary>
    public static string? PlaceOption(string chosen, IReadOnlyList<string> names) =>
        Finder(names)(chosen) is >= 0 and var index ? ShortNames(names)[index] : null;

    /// <summary>
    /// Finds which of the places <paramref name="names"/> a name chosen stands for, or -1. Short names change as places
    /// of the same name come and go, so it's one's short name, or failing that one's name up to the second comma (chosen
    /// as Paris (Texas) while there was another Paris), or the first whose name starts with it (chosen as Paris before
    /// another Paris was added).
    /// </summary>
    private static Func<string, int> Finder(IReadOnlyList<string> names)
    {
        List<string?>[] spellings = [[.. ShortNames(names)], [.. names.Select(TwoParts)], [.. names.Select(ShortName)]];
        return chosen => spellings.Select(list => list.FindIndex(n => chosen.Equals(n, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(i => i >= 0, -1);
    }

    /// <summary>One day or hour: when, an icon, the temperature (and low), and the chance of rain if it's at least <c>rainFrom</c>.</summary>
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

        if (period.RainChance is double chance && chance >= _rainFrom)
            column.Children.Add(Line($"{chance:0}%", _forecastSize * 0.75, _detail, HorizontalAlignment.Center));
        return column;
    }

    /// <summary>
    /// The most serious warning: a warning sign and its headline in the theme's warning colour (critical for red, severe
    /// and extreme ones), then "and 2 more" if there are others. Nothing at all while there are none.
    /// </summary>
    private void ShowWarnings(IReadOnlyList<WeatherWarning>? warnings)
    {
        if (_warning is null)
            return;
        if (warnings is not [var worst, ..])
        {
            _warning.Child = null;
            _warning.Visibility = Visibility.Collapsed;
            _warningLink = null;
            PlaceWarning(false);
            return;
        }

        var brush = worst.IsCritical ? Theme.Critical : Theme.Warning;
        var size = WarningSize;
        var row = new DockPanel();
        var icon = Icons.Create("warning", brush, size * 1.2, 2);
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.Margin = new Thickness(0, 0, size * 0.35, 0);
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        if (warnings.Count > 1)
        {
            // Docked on the right so a long headline is cut short rather than the count.
            var more = Line($"and {warnings.Count - 1} more", size, _detail);
            more.VerticalAlignment = VerticalAlignment.Center;
            more.Margin = new Thickness(size * 0.5, 0, 0, 0);
            DockPanel.SetDock(more, Dock.Right);
            row.Children.Add(more);
        }
        var headline = Line(worst.Headline, size, brush);
        headline.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(headline);

        _warning.Child = row;
        _warning.Visibility = Visibility.Visible;
        _warningLink = worst.Link;
        PlaceWarning(true);
    }

    /// <summary>The warning's text size: a little smaller than the forecast's, or without one, the size of the details beside the temperature.</summary>
    private double WarningSize => _ahead is null ? _fontSize * 0.22 : _forecastSize * 0.9;

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
