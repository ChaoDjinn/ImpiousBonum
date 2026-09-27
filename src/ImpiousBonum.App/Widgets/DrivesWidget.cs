using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// One row per drive: name, free/total text, and a bar showing how full it is. Rows follow drives as they are added or removed.
/// Settings: <c>drives</c> ("all" or e.g. "C,D"), <c>label</c> (default "{letter}:/"), <c>text</c> (default "{free} free / {total}"),
/// <c>fontSize</c>, <c>barHeight</c>, <c>spacing</c>.
/// </summary>
public sealed class DrivesWidget : Widget
{
    private readonly StackPanel _rows = new();
    private readonly HashSet<string>? _only;
    private readonly string _labelFormat;
    private readonly string _textFormat;
    private readonly double _fontSize;
    private readonly double _barHeight;
    private readonly double _spacing;
    private readonly List<Row> _current = [];
    private string _letters = string.Empty;

    public DrivesWidget(JsonObject definition, Theme theme) : base(definition, theme)
    {
        var drives = definition.GetString("drives", "all")!;
        _only = drives.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null
            : drives.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(d => d[..1]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _labelFormat = definition.GetString("label", "{letter}:/")!;
        _textFormat = definition.GetString("text", "{free} free / {total}")!;
        _fontSize = definition.GetDouble("fontSize", 25);
        _barHeight = definition.GetDouble("barHeight", 5);
        _spacing = definition.GetDouble("spacing", 12);
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
