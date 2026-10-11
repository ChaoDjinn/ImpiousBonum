using System.Windows;
using System.Windows.Controls;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// What's using the PC, like a small Task Manager: one row per app (all its processes together) with its CPU and
/// memory, busiest first.
/// </summary>
public sealed class ProcessesWidget : Widget
{
    public static WidgetDescriptor Descriptor { get; } = new(
        "processes", "Top apps",
        "The apps using the most CPU or memory, like a small Task Manager. An app's processes count together, so all of a browser's tabs are one row.",
        400, 220,
        [
            Setting.Choice("sort", "Sort by", "cpu", ["cpu", "memory"], group: Setting.Content),
            Setting.Number("count", "Rows", 5, 1, 30, group: Setting.Content),
            Setting.Choice("columns", "Show", "both", ["both", "cpu", "memory"], group: Setting.Content,
                help: "Which numbers each row shows after the app's name."),
            Setting.Number("fontSize", "Font size", 20, 6, 200),
            Setting.Number("spacing", "Row spacing", 4, 0, 200, group: Setting.Appearance),
            Setting.Thresholds("Changes the colour of each row's CPU figure. An empty metric uses that app's CPU."),
        ],
        (settings, theme) => new ProcessesWidget(settings, theme));

    // Only for formatting: the numbers come from the attachment, not from metrics.
    private static readonly MetricDefinition CpuFormat = new("top.format.cpu", "CPU", "Processes", MetricUnit.Percent, 100, "0.0");
    private static readonly MetricDefinition MemoryFormat = new("top.format.memory", "Memory", "Processes", MetricUnit.Bytes, null, "0");

    private readonly bool _byMemory;
    private readonly bool _showCpu;
    private readonly bool _showMemory;
    private readonly ThresholdColors _thresholds;
    private readonly List<Row> _rows = [];

    public ProcessesWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _byMemory = settings.String("sort") == "memory";
        var columns = settings.String("columns");
        _showCpu = columns != "memory";
        _showMemory = columns != "cpu";
        _thresholds = new ThresholdColors(settings, theme);

        var fontSize = settings.Number("fontSize");
        var spacing = settings.Number("spacing");
        var panel = new StackPanel();
        for (var i = 0; i < (int)settings.Number("count"); i++)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, spacing) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = CreateText(fontSize);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            var cpu = CreateText(fontSize * 0.9, Theme.Secondary, HorizontalAlignment.Right);
            cpu.MinWidth = fontSize * 3.2;
            cpu.Margin = new Thickness(fontSize * 0.5, 0, 0, 0);
            cpu.VerticalAlignment = VerticalAlignment.Center;
            var memory = CreateText(fontSize * 0.9, Theme.Secondary, HorizontalAlignment.Right);
            memory.MinWidth = fontSize * 4.2;
            memory.Margin = new Thickness(fontSize * 0.5, 0, 0, 0);
            memory.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(cpu, 1);
            Grid.SetColumn(memory, 2);
            cpu.Visibility = _showCpu ? Visibility.Visible : Visibility.Collapsed;
            memory.Visibility = _showMemory ? Visibility.Visible : Visibility.Collapsed;

            grid.Children.Add(name);
            grid.Children.Add(cpu);
            grid.Children.Add(memory);
            panel.Children.Add(grid);
            _rows.Add(new Row(grid, name, cpu, memory));
        }
        Children.Add(panel);
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        var apps = Top(store.GetAttachment(ProcessProvider.Apps) as IReadOnlyList<AppUsage>, _byMemory, _rows.Count);
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            if (i >= apps.Count)
            {
                // Before the second reading there's nothing to rank: keep the rows' space, show nothing.
                row.Grid.Visibility = Visibility.Hidden;
                continue;
            }
            var app = apps[i];
            row.Grid.Visibility = Visibility.Visible;
            row.Name.Text = app.Processes > 1 ? $"{app.Name} ({app.Processes})" : app.Name;
            row.Cpu.Text = MetricFormatter.Format(new MetricSample(CpuFormat, app.Cpu, null));
            row.Cpu.Foreground = _thresholds.Pick(store, app.Cpu, Theme.Secondary);
            row.Memory.Text = MetricFormatter.Format(new MetricSample(MemoryFormat, app.Memory, null));
        }
    }

    /// <summary>The first <paramref name="count"/> apps by CPU or by memory.</summary>
    internal static IReadOnlyList<AppUsage> Top(IReadOnlyList<AppUsage>? apps, bool byMemory, int count) =>
        apps is null
            ? []
            : (byMemory
                ? apps.OrderByDescending(a => a.Memory).ThenByDescending(a => a.Cpu)
                : apps.OrderByDescending(a => a.Cpu).ThenByDescending(a => a.Memory))
                .Take(count)
                .ToList();

    private sealed record Row(Grid Grid, TextBlock Name, TextBlock Cpu, TextBlock Memory);
}
