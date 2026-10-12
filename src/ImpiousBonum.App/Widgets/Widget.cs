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

    /// <summary>Raised (bubbling) when a tap on a widget asks the app to do something; see <see cref="WidgetAction"/>.</summary>
    public static readonly RoutedEvent ActionEvent = EventManager.RegisterRoutedEvent(
        "Action", RoutingStrategy.Bubble, typeof(EventHandler<WidgetActionEventArgs>), typeof(Widget));

    protected void RequestAction(WidgetAction action) => RaiseEvent(new WidgetActionEventArgs(action));

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

    /// <summary>
    /// With the widget's <c>shrinkToFit</c> setting on (see <see cref="Setting.ShrinkToFit"/>), puts
    /// <paramref name="content"/> in a <see cref="ShrinkToFit"/> that takes over its margin and grid cell, so it gets
    /// smaller to fit its space rather than being cut off. With it off, the content is returned as it is.
    /// </summary>
    protected FrameworkElement Fit(FrameworkElement content, FitDirection direction)
    {
        if (!Settings.Bool(Setting.ShrinkToFitKey))
            return content;
        var box = new ShrinkToFit { Direction = direction, Margin = content.Margin };
        SetColumn(box, GetColumn(content));
        SetColumnSpan(box, GetColumnSpan(content));
        SetRow(box, GetRow(content));
        SetRowSpan(box, GetRowSpan(content));
        content.Margin = default;
        box.Child = content;
        return box;
    }

    /// <summary>
    /// With shrink to fit on, moves everything in the widget, its rows and columns too, into a <see cref="ShrinkToFit"/>,
    /// so it all gets smaller together when it doesn't fit. Call it once the widget is built, with how the content sits
    /// in the widget's height.
    /// </summary>
    protected void FitContent(FitDirection direction, VerticalAlignment alignment = VerticalAlignment.Stretch)
    {
        if (!Settings.Bool(Setting.ShrinkToFitKey))
            return;
        var content = new Grid { VerticalAlignment = alignment };
        var rows = RowDefinitions.ToList();
        var columns = ColumnDefinitions.ToList();
        var children = Children.Cast<UIElement>().ToList();
        RowDefinitions.Clear();
        ColumnDefinitions.Clear();
        Children.Clear();
        rows.ForEach(content.RowDefinitions.Add);
        columns.ForEach(content.ColumnDefinitions.Add);
        children.ForEach(child => content.Children.Add(child));
        Children.Add(new ShrinkToFit { Direction = direction, Child = content });
    }

    /// <summary>
    /// A label on the left and a value on the right of one line. With shrink to fit they sit side by side, with a little
    /// gap, and get smaller together rather than run into each other or off the edge.
    /// </summary>
    protected FrameworkElement LabelAndValue(TextBlock label, TextBlock value)
    {
        var line = new Grid();
        line.Children.Add(label);
        line.Children.Add(value);
        if (!Settings.Bool(Setting.ShrinkToFitKey))
            return line;
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition());
        SetColumn(value, 1);
        value.Margin = new Thickness(value.FontSize * 0.25, 0, 0, 0);
        return Fit(line, FitDirection.Width);
    }

    protected static HorizontalAlignment ParseAlignment(string? value) => value?.ToLowerInvariant() switch
    {
        "right" => HorizontalAlignment.Right,
        "center" or "centre" => HorizontalAlignment.Center,
        _ => HorizontalAlignment.Left,
    };
}
