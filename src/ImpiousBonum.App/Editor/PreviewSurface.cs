using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Editor;

/// <summary>A scaled, live copy of the dashboard in the editor, with an <see cref="EditOverlay"/> for selecting, moving and resizing.</summary>
public sealed class PreviewSurface : Border
{
    private const double HandleSize = 9;

    private readonly LayoutSession _session;
    private readonly Grid _stage = new();
    private readonly DashboardView _view = new();
    private readonly EditOverlay _overlay;
    private readonly Viewbox _viewbox;
    private bool _rebuildQueued;
    private MetricStore? _store;

    public PreviewSurface(LayoutSession session)
    {
        _session = session;
        Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
        Padding = new Thickness(24);
        ClipToBounds = true;
        // Focusable so clicking the preview takes focus from text boxes, letting arrow keys nudge the selection.
        Focusable = true;
        FocusVisualStyle = null;

        // The Viewbox must exist before the overlay: the overlay draws immediately and asks for the zoom.
        _viewbox = new Viewbox { Stretch = Stretch.Uniform, Child = _stage };
        Child = _viewbox;
        _overlay = new EditOverlay(session, HandleSize, () => UnitsPerPixel);
        _overlay.PreviewMouseLeftButtonDown += (_, _) => Focus();
        _stage.Children.Add(_view);
        _stage.Children.Add(_overlay);

        SizeChanged += (_, _) => _overlay.Update();
        session.Changed += OnChanged;
        Rebuild();
    }

    public void Refresh(MetricStore store, DateTime now)
    {
        _store = store;
        _view.Refresh(store, now);
    }

    private void OnChanged(object? sender, LayoutChange change)
    {
        if (change.Kind == ChangeKind.Geometry && change.WidgetIndex is { } index)
        {
            var rect = LayoutSession.GeometryOf(_session.Document.Widgets[index]);
            _view.SetGeometry(index, rect.X, rect.Y, rect.Width, rect.Height);
            return;
        }

        // Typing fires a change per keystroke; rebuild once the burst has been handled.
        if (_rebuildQueued)
            return;
        _rebuildQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _rebuildQueued = false;
            Rebuild();
        }, DispatcherPriority.Background);
    }

    private void Rebuild()
    {
        _view.Build(_session.Document);
        // Fill new widgets with the latest readings now rather than on the next tick, so edits don't flash blank.
        if (_store is not null)
            _view.Refresh(_store, DateTime.Now);
        _overlay.Update();
    }

    /// <summary>Canvas units per screen pixel, so handles and outlines stay the same size on screen at any zoom.</summary>
    private double UnitsPerPixel => EditOverlayScale.UnitsPerPixel(_viewbox, _session.Document);
}

/// <summary>Zoom of a letterboxed Viewbox: it fills its space, so the scale is the tighter of the two fits.</summary>
public static class EditOverlayScale
{
    public static double UnitsPerPixel(FrameworkElement viewbox, Layout.LayoutDocument document)
    {
        var scale = Math.Min(viewbox.ActualWidth / document.Width, viewbox.ActualHeight / document.Height);
        return scale > 0 && double.IsFinite(scale) ? 1 / scale : 1;
    }
}
