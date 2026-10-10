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

    /// <summary>How hidden widgets and parts of widgets look while the layout is being edited.</summary>
    protected const double FadedOpacity = 0.4;

    private bool _editing;
    private bool _unavailable;

    /// <summary>
    /// True while the layout is being edited. Widgets that hide themselves (see <see cref="SetUnavailable"/>) then stay
    /// on screen, faded, so they can still be selected and placed.
    /// </summary>
    public bool Editing
    {
        get => _editing;
        set
        {
            _editing = value;
            ApplyAvailability();
        }
    }

    /// <summary>Called roughly once a second on the UI thread.</summary>
    public abstract void Refresh(MetricStore store, DateTime now);

    /// <summary>Hides the widget while it has nothing to show (faded instead while <see cref="Editing"/>).</summary>
    protected void SetUnavailable(bool unavailable)
    {
        if (_unavailable == unavailable)
            return;
        _unavailable = unavailable;
        ApplyAvailability();
    }

    private void ApplyAvailability()
    {
        Visibility = _unavailable && !_editing ? Visibility.Collapsed : Visibility.Visible;
        Opacity = _unavailable && _editing ? FadedOpacity : 1;
    }

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
