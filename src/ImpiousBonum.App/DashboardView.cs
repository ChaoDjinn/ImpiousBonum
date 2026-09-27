using System.Windows;
using System.Windows.Controls;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App;

/// <summary>The layout's canvas at its design size. The window scales it to fit; snapshots render it directly.</summary>
public sealed class DashboardView : Canvas
{
    private readonly List<Widget> _widgets = [];

    public DashboardView()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    public void Build(LayoutDocument layout)
    {
        var theme = Theme.From(layout.Theme);
        Children.Clear();
        _widgets.Clear();
        Width = layout.Width;
        Height = layout.Height;
        Background = theme.Background;

        foreach (var definition in layout.Widgets)
        {
            var widget = WidgetFactory.Create(definition, theme);
            widget.Width = definition.GetDouble("width", 200);
            widget.Height = definition.GetDouble("height", 100);
            SetLeft(widget, definition.GetDouble("x", 0));
            SetTop(widget, definition.GetDouble("y", 0));
            Children.Add(widget);
            _widgets.Add(widget);
        }
    }

    public void Refresh(MetricStore store, DateTime now)
    {
        foreach (var widget in _widgets)
            widget.Refresh(store, now);
    }
}
