using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Widgets;

/// <summary>Base for everything placed on the dashboard canvas. Widgets build their visuals once and update them on each tick.</summary>
public abstract class Widget : Grid
{
    protected Widget(JsonObject definition, Theme theme)
    {
        Definition = definition;
        Theme = theme;
        ClipToBounds = false;
    }

    protected JsonObject Definition { get; }

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
