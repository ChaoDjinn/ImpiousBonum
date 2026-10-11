using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Widgets;

/// <summary>One row per drive: name, free/total text, and a bar showing how full it is. Rows follow drives as they are added or removed.</summary>
public sealed class DrivesWidget : Widget
{
    public static WidgetDescriptor Descriptor { get; } = new(
        "drives", "Drives", "One row per drive with free space and a bar showing how full it is. Follows drives as they are plugged in or removed.",
        425, 230,
        [
            Setting.PlainText("drives", "Drives", "all", help: "\"all\", or letters separated by commas, e.g. \"C,D\"."),
            Setting.DriveTemplate("label", "Row label", "{letter}:/", help: RowHelp),
            Setting.DriveTemplate("text", "Row value", "{free} free / {total}", help: RowHelp),
            Setting.Number("fontSize", "Font size", 25, 6, 200),
            Setting.Number("barHeight", "Bar height", 5, 1, 100, group: Setting.Appearance),
            Setting.Number("spacing", "Row spacing", 12, 0, 200, group: Setting.Appearance),
            Setting.Color("color", "Bar colour", "accent"),
            Setting.Color("trackColor", "Track colour", "track", help: "The empty part of each bar. track is the theme's bar track."),
            Setting.Color("labelColor", "Label colour", "foreground"),
            Setting.Color("valueColor", "Value colour", "secondary"),
            Setting.Thresholds("Changes each drive's bar. An empty metric uses that drive's used percentage (disk.C.usedPct for C:)."),
        ],
        (settings, theme) => new DrivesWidget(settings, theme));

    private const string RowHelp =
        "Each row's own drive: {letter}, {free}, {used}, {total}, {usedPct}, {label}, {read}, {write}, {active}, and with the "
        + "sensor service {life}, {temp}, {status}, {model}, {hours}. In a metric id, * is the row's drive, e.g. {disk.*.read}. "
        + "Other metrics work too.";

    private readonly StackPanel _rows = new();
    private readonly HashSet<string>? _only;
    private readonly string _labelFormat;
    private readonly string _textFormat;
    private readonly double _fontSize;
    private readonly double _barHeight;
    private readonly double _spacing;
    private readonly ThresholdColors _thresholds;
    private readonly Brush _fill;
    private readonly Brush _track;
    private readonly Brush _label;
    private readonly Brush _value;
    private readonly List<Row> _current = [];
    private string _letters = string.Empty;

    public DrivesWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        var drives = settings.String("drives");
        _only = drives.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null
            : drives.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(d => d[..1]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _labelFormat = settings.String("label");
        _textFormat = settings.String("text");
        _fontSize = settings.Number("fontSize");
        _barHeight = settings.Number("barHeight");
        _spacing = settings.Number("spacing");
        _thresholds = new ThresholdColors(settings, theme);
        _fill = theme.Resolve(settings.String("color"), theme.Accent);
        _track = theme.Resolve(settings.String("trackColor"), theme.Track);
        _label = theme.Resolve(settings.String("labelColor"), theme.Foreground);
        _value = theme.Resolve(settings.String("valueColor"), theme.Secondary);
        Children.Add(_rows);
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        var letters = store.TryGet(DriveProvider.Letters, out var sample) ? sample.Text ?? string.Empty : string.Empty;
        if (letters != _letters)
        {
            _letters = letters;
            Rebuild(letters.Split(',', StringSplitOptions.RemoveEmptyEntries));
        }

        foreach (var row in _current)
        {
            row.LabelText.Text = row.Label.Render(store);
            row.Value.Text = row.Template.Render(store);
            var used = store.TryGet(DriveProvider.UsedPercent(row.Letter), out var pct) && pct.Value is double v ? Math.Clamp(v, 0, 100) : 0;
            row.Bar.ColumnDefinitions[0].Width = new GridLength(used, GridUnitType.Star);
            row.Bar.ColumnDefinitions[1].Width = new GridLength(100 - used, GridUnitType.Star);
            row.Fill.Background = _thresholds.Pick(store, DriveProvider.UsedPercent(row.Letter), _fill);
        }
    }

    private void Rebuild(IEnumerable<string> letters)
    {
        _rows.Children.Clear();
        _current.Clear();

        foreach (var letter in letters.Where(l => _only is null || _only.Contains(l)))
        {
            var header = new Grid();
            var label = CreateText(_fontSize, _label);
            var value = CreateText(_fontSize * 0.9, _value, HorizontalAlignment.Right);
            value.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(label);
            header.Children.Add(value);

            var bar = new Grid { Height = _barHeight, Background = _track, Margin = new Thickness(0, 2, 0, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100, GridUnitType.Star) });
            var fill = new Border { Background = _fill };
            bar.Children.Add(fill);

            var row = new StackPanel { Margin = new Thickness(0, 0, 0, _spacing) };
            row.Children.Add(header);
            row.Children.Add(bar);
            _rows.Children.Add(row);

            _current.Add(new Row(
                letter,
                ValueTemplate.Parse(DriveTemplate.Expand(_labelFormat, letter)), label,
                ValueTemplate.Parse(DriveTemplate.Expand(_textFormat, letter)), value,
                bar, fill));
        }
    }

    private sealed record Row(string Letter, ValueTemplate Label, TextBlock LabelText, ValueTemplate Template, TextBlock Value, Grid Bar, Border Fill);
}
