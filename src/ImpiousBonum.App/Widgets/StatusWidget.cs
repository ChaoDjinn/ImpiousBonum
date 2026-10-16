using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// Whether your own servers and services are up, from the Status checks page, as a list or a grid: each check's name,
/// with a dot in the accent colour while it's up and the critical colour while it's down, and how fast it answered.
/// </summary>
public sealed class StatusWidget : Widget
{
    /// <summary>The names the editor offers for <c>checks</c>: the Status checks page's, in its order.</summary>
    public const string CheckNames = "statusChecks";

    public const string Empty = "No status checks yet";

    public static WidgetDescriptor Descriptor { get; } = new(
        "status", "Status checks",
        "Whether your own servers and services are up (set them up on the Status checks page), as a list or a grid: each with a dot in the accent colour while it's up and the critical colour while it's down, its name and how fast it answered.",
        360, 200,
        [
            Setting.Picks("checks", "Checks", PickList.AllValue, source: CheckNames, all: "All checks", none: "No status checks yet: add them on the Status checks page.",
                help: "All checks, or just the ones chosen, in the order they're listed."),
            Setting.Number("columns", "Columns", 1, 1, 10, group: Setting.Content, help: "1 is a list; more makes a grid, filled a row at a time."),
            Setting.Toggle("time", "Show response time", true, group: Setting.Content, help: "How long each took to answer, e.g. 4 ms."),
            Setting.Toggle("since", "Show how long", false, group: Setting.Content, help: "How long each has been up or down, e.g. 3 h."),
            Setting.Number("fontSize", "Font size", 22, 6, 200),
            Setting.ShrinkToFit(),
            Setting.Number("spacing", "Spacing", 8, 0, 100, group: Setting.Appearance),
            Setting.Color("upColor", "Up", "accent", help: "The dot of a check that's up."),
            Setting.Color("downColor", "Down", "critical", help: "The dot of a check that's down, and the word Down."),
        ],
        (settings, theme) => new StatusWidget(settings, theme));

    private readonly UniformGrid _grid;
    private readonly PickList _checks;
    private readonly int _columns;
    private readonly bool _time;
    private readonly bool _since;
    private readonly double _fontSize;
    private readonly double _spacing;
    private readonly Brush _up;
    private readonly Brush _down;
    private string _shown = "\0";

    public StatusWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _checks = settings.Picks("checks");
        _columns = (int)settings.Number("columns");
        _time = settings.Bool("time");
        _since = settings.Bool("since");
        _fontSize = settings.Number("fontSize");
        _spacing = settings.Number("spacing");
        _up = theme.Resolve(settings.String("upColor"), theme.Accent);
        _down = theme.Resolve(settings.String("downColor"), theme.Critical);

        // Side by side, a gap between the columns; the grid reaches past the widget by half of it either side so the
        // outer columns line up with its edges.
        var gap = _columns > 1 ? _fontSize : 0;
        _grid = new UniformGrid { Columns = _columns, Margin = new Thickness(-gap / 2, 0, -gap / 2, 0) };
        // In a stack, so the rows keep their own height rather than share out the widget's.
        var stack = new StackPanel();
        stack.Children.Add(_grid);
        // Names end with … when they're too long; the checks get smaller when they don't all fit, or their times don't.
        Children.Add(Fit(stack, FitDirection.Both));
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        var shown = Pick(store.GetAttachment(StatusProvider.Checks) as IReadOnlyList<CheckStatus>, _checks);
        // Response times change with each check, how long by the minute; rebuild only when something on show does.
        var key = string.Join("\n", shown.Select(s => $"{s.Name}|{s.Up}|{Value(s, _time, _since)}"));
        if (key == _shown)
            return;
        _shown = key;
        Build(shown);
    }

    /// <summary>The checks to show: all of them in the Status checks page's order, or the ones chosen in the order chosen.</summary>
    internal static IReadOnlyList<CheckStatus> Pick(IReadOnlyList<CheckStatus>? statuses, PickList chosen)
    {
        if (statuses is null)
            return [];
        if (chosen.All)
            return statuses;
        return chosen.Names.Select(name => statuses.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))).OfType<CheckStatus>().ToList();
    }

    /// <summary>
    /// What's on the right of a check: its response time and how long it's been up (<c>4 ms · 3 h</c>), or <c>Down</c>
    /// and for how long; <c>…</c> until it's been checked.
    /// </summary>
    internal static string Value(CheckStatus status, bool time, bool since)
    {
        if (status.Up is null)
            return "…";
        var first = status.Up == false ? "Down" : time && status.Milliseconds is { } ms ? MetricFormatter.Format(ms, MetricUnit.Milliseconds) : null;
        return string.Join(" · ", new[] { first, since ? status.Since : null }.OfType<string>());
    }

    private void Build(IReadOnlyList<CheckStatus> statuses)
    {
        _grid.Children.Clear();
        _grid.Columns = statuses.Count == 0 ? 1 : _columns;
        var gap = _columns > 1 ? _fontSize : 0;
        if (statuses.Count == 0)
        {
            var empty = CreateText(_fontSize, Theme.Secondary);
            empty.Text = Empty;
            empty.TextTrimming = TextTrimming.CharacterEllipsis;
            empty.Margin = new Thickness(gap / 2, 0, gap / 2, 0);
            _grid.Children.Add(empty);
            return;
        }

        var rows = (statuses.Count + _columns - 1) / _columns;
        for (var i = 0; i < statuses.Count; i++)
        {
            var status = statuses[i];
            // At the top of its cell: the grid makes every cell as tall as the tallest, spacing and all, and the last
            // row has none under it.
            var row = new Grid { Margin = new Thickness(gap / 2, 0, gap / 2, i / _columns < rows - 1 ? _spacing : 0), VerticalAlignment = VerticalAlignment.Top };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Up is a dot in the up colour, down one in the down colour; a ring until the first check is back.
            var size = Math.Round(_fontSize * 0.5);
            var dot = new Ellipse { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, _fontSize * 0.45, 0) };
            if (status.Up is { } up)
                dot.Fill = up ? _up : _down;
            else
            {
                dot.Stroke = Theme.Secondary;
                dot.StrokeThickness = Math.Max(1, size / 6);
            }
            row.Children.Add(dot);

            var name = CreateText(_fontSize);
            name.Text = status.Name;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            // A name makes do with what the time leaves, down to a few letters.
            var squeezed = Squeezed(name, _fontSize * 3);
            Grid.SetColumn(squeezed, 1);
            row.Children.Add(squeezed);

            var value = CreateText(_fontSize * 0.8, status.Up == false ? _down : Theme.Secondary, HorizontalAlignment.Right);
            value.Text = Value(status, _time, _since);
            value.VerticalAlignment = VerticalAlignment.Center;
            if (value.Text.Length > 0)
                value.Margin = new Thickness(_fontSize * 0.4, 0, 0, 0);
            Grid.SetColumn(value, 2);
            row.Children.Add(value);
            _grid.Children.Add(row);
        }
    }
}
