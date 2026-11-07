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
        "The apps using the most CPU, memory or graphics card, like a small Task Manager. An app's processes count together, so all of a browser's tabs are one row.",
        400, 220,
        [
            Setting.Choice("sort", "Sort by", "cpu", ["cpu", "memory", "gpu"], group: Setting.Content,
                help: "gpu ranks the apps by their share of the graphics card, like Task Manager's GPU column, and leaves out apps not using it."),
            Setting.Number("count", "Rows", 5, 1, 30, group: Setting.Content),
            Setting.Choice("columns", "Show", "both", ["both", "cpu", "memory"], group: Setting.Content,
                help: "Which numbers each row shows after the app's name."),
            Setting.Toggle("gpu", "GPU column", false, group: Setting.Content,
                help: "Each app's share of the graphics card too, between CPU and memory. Always shown when sorted by GPU."),
            Setting.Number("fontSize", "Font size", 20, 6, 200),
            Setting.ShrinkToFit(),
            Setting.Number("spacing", "Row spacing", 4, 0, 200, group: Setting.Appearance),
            Setting.Thresholds("Changes the colour of each row's CPU figure. An empty metric uses that app's CPU."),
        ],
        (settings, theme) => new ProcessesWidget(settings, theme));

    // Only for formatting: the numbers come from the attachment, not from metrics.
    private static readonly MetricDefinition CpuFormat = new("top.format.cpu", "CPU", "Processes", MetricUnit.Percent, 100, "0.0");
    private static readonly MetricDefinition MemoryFormat = new("top.format.memory", "Memory", "Processes", MetricUnit.Bytes, null, "0");

    private readonly string _sort;
    private readonly bool _showCpu;
    private readonly bool _showMemory;
    private readonly bool _showGpu;
    private readonly ThresholdColors _thresholds;
    private readonly List<Row> _rows = [];

    public ProcessesWidget(WidgetSettings settings, Theme theme) : base(settings, theme)
    {
        _sort = settings.String("sort");
        var columns = settings.String("columns");
        _showCpu = columns != "memory";
        _showMemory = columns != "cpu";
        _showGpu = settings.Bool("gpu") || _sort == Gpu;
        _thresholds = new ThresholdColors(settings, theme);

        var fontSize = settings.Number("fontSize");
        var spacing = settings.Number("spacing");
        var panel = new StackPanel();
        // With the GPU column, a row has up to three numbers: small headings over them say which is which.
        if (_showGpu)
            panel.Children.Add(Headings(fontSize, spacing));
        for (var i = 0; i < (int)settings.Number("count"); i++)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, spacing) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = CreateText(fontSize);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            var cpu = CreateText(fontSize * 0.9, Theme.Secondary, HorizontalAlignment.Right);
            cpu.MinWidth = fontSize * 3.2;
            cpu.Margin = new Thickness(fontSize * 0.5, 0, 0, 0);
            cpu.VerticalAlignment = VerticalAlignment.Center;
            var gpu = CreateText(fontSize * 0.9, Theme.Secondary, HorizontalAlignment.Right);
            gpu.MinWidth = fontSize * 3.2;
            gpu.Margin = new Thickness(fontSize * 0.5, 0, 0, 0);
            gpu.VerticalAlignment = VerticalAlignment.Center;
            gpu.Visibility = _showGpu ? Visibility.Visible : Visibility.Collapsed;
            var memory = CreateText(fontSize * 0.9, Theme.Secondary, HorizontalAlignment.Right);
            memory.MinWidth = fontSize * 4.2;
            memory.Margin = new Thickness(fontSize * 0.5, 0, 0, 0);
            memory.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(cpu, 1);
            Grid.SetColumn(gpu, 2);
            Grid.SetColumn(memory, 3);
            var symbol = CreateSymbol(cpu);
            var cpuCell = Beside(cpu, symbol);
            cpuCell.Visibility = _showCpu ? Visibility.Visible : Visibility.Collapsed;
            memory.Visibility = _showMemory ? Visibility.Visible : Visibility.Collapsed;

            // A name makes do with what the numbers leave, down to a few letters.
            grid.Children.Add(Squeezed(name, fontSize * 3));
            grid.Children.Add(cpuCell);
            grid.Children.Add(gpu);
            grid.Children.Add(memory);
            panel.Children.Add(grid);
            _rows.Add(new Row(grid, name, cpu, symbol, gpu, memory));
        }
        // Names end with … when they're too long; the rows get smaller when they don't all fit, or the numbers don't.
        Children.Add(Fit(panel, FitDirection.Both));
    }

    public override void Refresh(MetricStore store, DateTime now)
    {
        var apps = Top(store.GetAttachment(ProcessProvider.Apps) as IReadOnlyList<AppUsage>, _sort, _rows.Count);
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
            row.Cpu.Foreground = _thresholds.Pick(store, app.Cpu, Theme.Secondary, out var level);
            row.Symbol?.Show(level, row.Cpu.Foreground);
            row.Gpu.Text = MetricFormatter.Format(new MetricSample(CpuFormat, app.Gpu, null));
            row.Memory.Text = MetricFormatter.Format(new MetricSample(MemoryFormat, app.Memory, null));
        }
    }

    private const string Memory = "memory", Gpu = "gpu";

    /// <summary>CPU, GPU and Memory over the columns shown, as wide as the numbers under them.</summary>
    private Grid Headings(double fontSize, double spacing)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, spacing) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var shown = new[] { ("CPU", _showCpu, 3.2), ("GPU", true, 3.2), ("Memory", _showMemory, 4.2) };
        foreach (var (heading, show, width) in shown)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = CreateText(fontSize * 0.7, Theme.Secondary, HorizontalAlignment.Right);
            text.Text = heading;
            text.MinWidth = fontSize * width;
            text.Margin = new Thickness(fontSize * 0.5, 0, 0, 0);
            text.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumn(text, grid.ColumnDefinitions.Count - 1);
            grid.Children.Add(text);
        }
        return grid;
    }

    /// <summary>
    /// The first <paramref name="count"/> apps by CPU, by memory, or by GPU (<paramref name="sort"/>); by GPU, only the
    /// apps using it.
    /// </summary>
    internal static IReadOnlyList<AppUsage> Top(IReadOnlyList<AppUsage>? apps, string sort, int count) =>
        apps is null
            ? []
            : (sort switch
            {
                Memory => apps.OrderByDescending(a => a.Memory).ThenByDescending(a => a.Cpu),
                Gpu => apps.Where(a => a.Gpu > 0).OrderByDescending(a => a.Gpu).ThenByDescending(a => a.Cpu),
                _ => apps.OrderByDescending(a => a.Cpu).ThenByDescending(a => a.Memory),
            })
                .Take(count)
                .ToList();

    private sealed record Row(Grid Grid, TextBlock Name, TextBlock Cpu, WarningSymbol? Symbol, TextBlock Gpu, TextBlock Memory);
}
