using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// The <c>span</c> setting of the widgets that draw a history: the readings a second apart every metric keeps (as many
/// as their <c>points</c> say), or averages going back 15 minutes to 24 hours.
/// </summary>
public static class GraphSpan
{
    public const string Key = "span";
    public const string Seconds = "seconds";

    private static readonly (string Name, LongSpan Span)[] Longer =
    [
        ("15 minutes", LongSpan.FifteenMinutes),
        ("1 hour", LongSpan.Hour),
        ("6 hours", LongSpan.SixHours),
        ("24 hours", LongSpan.Day),
    ];

    public static SettingDescriptor Setting() =>
        Widgets.Setting.Choice(Key, "Time span", Seconds, [Seconds, .. Longer.Select(l => l.Name)], group: Widgets.Setting.Appearance,
            help: "seconds shows the last History (seconds) of readings, one a second. The longer spans show averages, a point every 10 seconds for 15 minutes or an hour, a minute for 6 hours and 5 minutes for 24 hours, and fill from when the graph is first on screen.");

    /// <summary>The <c>points</c> setting, which counts with the span on <see cref="Seconds"/>.</summary>
    public static SettingDescriptor Points(double @default) =>
        Widgets.Setting.Number("points", "History (seconds)", @default, 2, MetricStore.DefaultHistoryLength, group: Widgets.Setting.Appearance,
            help: "How many seconds of readings, with Time span on seconds.");

    /// <summary>The longer span chosen, or null for the readings a second apart.</summary>
    public static LongSpan? Of(WidgetSettings settings)
    {
        var name = settings.OptionalString(Key);
        foreach (var (longer, span) in Longer)
        {
            if (string.Equals(name, longer, StringComparison.OrdinalIgnoreCase))
                return span;
        }
        return null;
    }
}
