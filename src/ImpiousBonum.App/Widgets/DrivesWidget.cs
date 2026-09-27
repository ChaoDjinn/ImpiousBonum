using System.Windows;
using System.Windows.Controls;
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
            Setting.PlainText("label", "Row label", "{letter}:/", help: "{letter} is replaced with the drive letter."),
            Setting.PlainText("text", "Row value", "{free} free / {total}", help: "Placeholders: {free}, {used}, {total}, {label}."),
            Setting.Number("fontSize", "Font size", 25, 6, 200),
            Setting.Number("barHeight", "Bar height", 5, 1, 100, group: Setting.Appearance),
            Setting.Number("spacing", "Row spacing", 12, 0, 200, group: Setting.Appearance),
        ],
        (settings, theme) => new DrivesWidget(settings, theme));

    private readonly StackPanel _rows = new();
    private readonly HashSet<string>? _only;
    private readonly string _labelFormat;
    private readonly string _textFormat;
    private readonly double _fontSize;
    private readonly double _barHeight;
    private readonly double _spacing;
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
            row.Value.Text = row.Template.Render(store);
            var used = store.TryGet(DriveProvider.UsedPercent(row.Letter), out var pct) && pct.Value is double v ? Math.Clamp(v, 0, 100) : 0;
            row.Bar.ColumnDefinitions[0].Width = new GridLength(used, GridUnitType.Star);
            row.Bar.ColumnDefinitions[1].Width = new GridLength(100 - used, GridUnitType.Star);
        }
    }

    private void Rebuild(IEnumerable<string> letters)
    {
        _rows.Children.Clear();
        _current.Clear();

        foreach (var letter in letters.Where(l => _only is null || _only.Contains(l)))
        {
            var header = new Grid();
            var label = CreateText(_fontSize);
            label.Text = _labelFormat.Replace("{letter}", letter, StringComparison.OrdinalIgnoreCase);
            var value = CreateText(_fontSize * 0.9, Theme.Secondary, HorizontalAlignment.Right);
            value.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(label);
            header.Children.Add(value);

            var bar = new Grid { Height = _barHeight, Background = Theme.Track, Margin = new Thickness(0, 2, 0, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100, GridUnitType.Star) });
            bar.Children.Add(new Border { Background = Theme.Accent });

            var row = new StackPanel { Margin = new Thickness(0, 0, 0, _spacing) };
            row.Children.Add(header);
            row.Children.Add(bar);
            _rows.Children.Add(row);

            var template = _textFormat
                .Replace("{free}", $"{{{DriveProvider.Free(letter)}}}", StringComparison.OrdinalIgnoreCase)
                .Replace("{total}", $"{{{DriveProvider.Total(letter)}}}", StringComparison.OrdinalIgnoreCase)
                .Replace("{used}", $"{{{DriveProvider.Used(letter)}}}", StringComparison.OrdinalIgnoreCase)
                .Replace("{label}", $"{{{DriveProvider.Label(letter)}}}", StringComparison.OrdinalIgnoreCase);
            _current.Add(new Row(letter, ValueTemplate.Parse(template), value, bar));
        }
    }

    private sealed record Row(string Letter, ValueTemplate Template, TextBlock Value, Grid Bar);
}
