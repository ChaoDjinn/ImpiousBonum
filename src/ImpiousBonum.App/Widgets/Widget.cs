using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// Base for everything placed on the dashboard canvas. Widgets build their visuals once and update them on each tick.
/// Each widget type also publishes a static <see cref="WidgetDescriptor"/> listing its settings.
/// </summary>
public abstract class Widget : Grid
{
    protected Widget(WidgetSettings settings, Theme theme)
    {
        Settings = settings;
        Theme = theme;
        ClipToBounds = false;
    }

    protected WidgetSettings Settings { get; }

    protected Theme Theme { get; }

    /// <summary>Called roughly once a second on the UI thread.</summary>
    public abstract void Refresh(MetricStore store, DateTime now);

    protected TextBlock CreateText(double fontSize, Brush? foreground = null, HorizontalAlignment alignment = HorizontalAlignment.Left) => new()
    {
        FontFamily = Theme.FontFamily,
        FontWeight = Theme.FontWeight,
        FontSize = fontSize,
        Foreground = foreground ?? Theme.Foreground,
        HorizontalAlignment = alignment,
        TextAlignment = alignment switch
        {
            HorizontalAlignment.Right => TextAlignment.Right,
            HorizontalAlignment.Center => TextAlignment.Center,
            _ => TextAlignment.Left,
        },
        TextWrapping = TextWrapping.NoWrap,
    };

    protected static HorizontalAlignment ParseAlignment(string? value) => value?.ToLowerInvariant() switch
    {
        "right" => HorizontalAlignment.Right,
        "center" or "centre" => HorizontalAlignment.Center,
        _ => HorizontalAlignment.Left,
    };
}
