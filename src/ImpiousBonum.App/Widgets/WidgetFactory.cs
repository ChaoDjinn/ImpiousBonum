using System.Text.Json.Nodes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

public static class WidgetFactory
{
    public const string Readings = "Readings";
    public const string Graphs = "Graphs and bars";
    public const string Lists = "Lists";
    public const string Games = "Games";
    public const string Time = "Time and calendar";
    public const string Media = "Media and controls";
    public const string Online = "Weather, news and Claude";
    public const string Decoration = "Pictures and shapes";

    /// <summary>
    /// Every widget type, grouped by what it's for, in the order the editor offers them: the groups are the tabs of its
    /// <see cref="Editor.AddWidgetWindow"/> and the sections of the widget reference.
    /// </summary>
    public static readonly IReadOnlyList<WidgetDescriptor> Descriptors = WithThemeOverrides(
    [
        .. In(Readings, TextWidget.Descriptor, GaugeWidget.Descriptor, StatWidget.Descriptor, IconValueWidget.Descriptor, RowsWidget.Descriptor),
        .. In(Graphs, GraphWidget.Descriptor, LinesWidget.Descriptor, BarsWidget.Descriptor, CoresWidget.Descriptor),
        .. In(Lists, DrivesWidget.Descriptor, ProcessesWidget.Descriptor, StatusWidget.Descriptor, BatteriesWidget.Descriptor),
        .. In(Games, RecentGamesWidget.Descriptor, DownloadsWidget.Descriptor, FriendsWidget.Descriptor),
        .. In(Time, ClockWidget.Descriptor, TimerWidget.Descriptor, AgendaWidget.Descriptor, CountdownsWidget.Descriptor),
        .. In(Online, WeatherWidget.Descriptor, RainWidget.Descriptor, SunMoonWidget.Descriptor, NewsWidget.Descriptor, BarsWidget.ClaudeDescriptor),
        .. In(Media, MediaWidget.Descriptor, VisualizerWidget.Descriptor, ButtonsWidget.Descriptor),
        .. In(Decoration, ImageWidget.Descriptor, ShapeWidget.Descriptor, PhotoFrameWidget.Descriptor),
    ]);

    private static IEnumerable<WidgetDescriptor> In(string category, params WidgetDescriptor[] descriptors) =>
        descriptors.Select(d => d with { Category = category });

    /// <summary>Every widget can also have its own font and colours (<see cref="Setting.ThemeOverrides"/>).</summary>
    private static IReadOnlyList<WidgetDescriptor> WithThemeOverrides(IReadOnlyList<WidgetDescriptor> descriptors) =>
        [.. descriptors.Select(d => d with { Settings = [.. d.Settings, .. Setting.ThemeOverrides] })];

    public static WidgetDescriptor? Find(string? type) =>
        Descriptors.FirstOrDefault(d => d.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

    public static Widget Create(JsonObject definition, Theme theme)
    {
        var type = definition.GetString("type");
        return Find(type) is { } descriptor
            ? descriptor.Create(new WidgetSettings(definition, descriptor.Settings), theme.WithOverrides(definition))
            : new UnknownWidget(type, theme);
    }

    /// <summary>Placeholder that makes a typo in layout.json visible instead of silently dropping the widget.</summary>
    private sealed class UnknownWidget : Widget
    {
        public UnknownWidget(string? type, Theme theme) : base(new WidgetSettings([], []), theme)
        {
            var text = CreateText(20, theme.Critical);
            text.Text = $"Unknown widget type '{type}'";
            Children.Add(text);
        }

        public override void Refresh(MetricStore store, DateTime now)
        {
        }
    }
}
