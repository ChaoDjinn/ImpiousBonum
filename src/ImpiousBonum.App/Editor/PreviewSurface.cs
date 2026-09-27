using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Editor;

/// <summary>
/// A scaled, live copy of the dashboard with an overlay for selecting, moving and resizing widgets.
/// The overlay sits inside the same Viewbox as the dashboard, so mouse positions arrive in canvas units.
/// </summary>
public sealed class PreviewSurface : Border
{
    private const double HandleScreenSize = 9;
    private const double MinimumSize = 8;

    private readonly LayoutSession _session;
    private readonly Grid _stage = new();
    private readonly DashboardView _view = new();
    private readonly Canvas _overlay = new() { Background = Brushes.Transparent, ClipToBounds = false };
    private readonly Viewbox _viewbox;
    private readonly Rectangle _selection;
    private readonly Rectangle[] _handles = new Rectangle[8];
    private readonly List<Rectangle> _outlines = [];
    private bool _rebuildQueued;
    private MetricStore? _store;

    private Gesture? _gesture;
    private int _gestureCount;

    public PreviewSurface(LayoutSession session)
    {
        _session = session;
        Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
        Padding = new Thickness(24);
        ClipToBounds = true;
        // Focusable so clicking the preview takes focus from text boxes, letting arrow keys nudge the selection.
        Focusable = true;
        FocusVisualStyle = null;

        _stage.Children.Add(_view);
        _stage.Children.Add(_overlay);
        _viewbox = new Viewbox { Stretch = Stretch.Uniform, Child = _stage };
        Child = _viewbox;

        var accent = new SolidColorBrush(Color.FromRgb(0x3D, 0xA5, 0xFF));
        _selection = new Rectangle { Stroke = accent, StrokeDashArray = [4, 3], IsHitTestVisible = false };
        for (var i = 0; i < _handles.Length; i++)
            _handles[i] = new Rectangle { Fill = Brushes.White, Stroke = accent, IsHitTestVisible = false };

        _overlay.MouseLeftButtonDown += OnMouseDown;
        _overlay.MouseMove += OnMouseMove;
        _overlay.MouseLeftButtonUp += OnMouseUp;
        _overlay.LostMouseCapture += (_, _) => _gesture = null;
        SizeChanged += (_, _) => UpdateAdorners();

        session.Changed += OnChanged;
        session.SelectionChanged += (_, _) => UpdateAdorners();
        Rebuild();
    }

    /// <summary>Grid size for snapping, in canvas units. 1 or less means no snapping. Holding Alt also bypasses it.</summary>
    public double Snap { get; set; } = 10;

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
            UpdateAdorners();
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
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void Rebuild()
    {
        var document = _session.Document;
        _view.Build(document);
        // Fill new widgets with the latest readings now rather than on the next tick, so edits don't flash blank.
        if (_store is not null)
            _view.Refresh(_store, DateTime.Now);
        _stage.Width = document.Width;
        _stage.Height = document.Height;
        _overlay.Width = document.Width;
        _overlay.Height = document.Height;
        UpdateAdorners();
    }

    /// <summary>Canvas units per screen pixel, so handles and outlines stay the same size on screen at any zoom.</summary>
    private double UnitsPerPixel
    {
        get
        {
            // The Viewbox fills its space and letterboxes the canvas, so the scale is the tighter of the two fits.
            var document = _session.Document;
            var scale = Math.Min(_viewbox.ActualWidth / document.Width, _viewbox.ActualHeight / document.Height);
            return scale > 0 && double.IsFinite(scale) ? 1 / scale : 1;
        }
    }

    private void UpdateAdorners()
    {
        _overlay.Children.Clear();
        var unit = UnitsPerPixel;

        // Faint outlines show where every widget is, including ones that are currently blank.
        var faint = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF));
        for (var i = 0; i < _session.Document.Widgets.Count; i++)
        {
            if (i >= _outlines.Count)
                _outlines.Add(new Rectangle { IsHitTestVisible = false });
            var outline = _outlines[i];
            outline.Stroke = faint;
            outline.StrokeThickness = unit;
            outline.StrokeDashArray = [2, 4];
            Place(outline, LayoutSession.GeometryOf(_session.Document.Widgets[i]));
            _overlay.Children.Add(outline);
        }

        if (_session.SelectedWidget is not { } selected)
            return;

        var rect = LayoutSession.GeometryOf(selected);
        _selection.StrokeThickness = 1.5 * unit;
        Place(_selection, rect);
        _overlay.Children.Add(_selection);

        var size = HandleScreenSize * unit;
        foreach (var (handle, point) in _handles.Zip(HandlePoints(rect)))
        {
            handle.StrokeThickness = unit;
            Place(handle, new Rect(point.X - size / 2, point.Y - size / 2, size, size));
            _overlay.Children.Add(handle);
        }
    }

    private static void Place(FrameworkElement element, Rect rect)
    {
        Canvas.SetLeft(element, rect.X);
        Canvas.SetTop(element, rect.Y);
        element.Width = rect.Width;
        element.Height = rect.Height;
    }

    // Handle order: top-left, top, top-right, right, bottom-right, bottom, bottom-left, left.
    private static Point[] HandlePoints(Rect r) =>
    [
        new(r.Left, r.Top), new(r.Left + r.Width / 2, r.Top), new(r.Right, r.Top), new(r.Right, r.Top + r.Height / 2),
        new(r.Right, r.Bottom), new(r.Left + r.Width / 2, r.Bottom), new(r.Left, r.Bottom), new(r.Left, r.Top + r.Height / 2),
    ];

    private static readonly Cursor[] HandleCursors =
    [
        Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE,
        Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE,
    ];

    private int HandleAt(Point point)
    {
        if (_session.SelectedWidget is not { } selected)
            return -1;
        var reach = HandleScreenSize * UnitsPerPixel;
        var points = HandlePoints(LayoutSession.GeometryOf(selected));
        for (var i = 0; i < points.Length; i++)
        {
            if (Math.Abs(points[i].X - point.X) <= reach && Math.Abs(points[i].Y - point.Y) <= reach)
                return i;
        }
        return -1;
    }

    /// <summary>Topmost widget under the point. Later widgets draw on top, so search from the end.</summary>
    private int WidgetAt(Point point)
    {
        for (var i = _session.Document.Widgets.Count - 1; i >= 0; i--)
        {
            if (LayoutSession.GeometryOf(_session.Document.Widgets[i]).Contains(point))
                return i;
        }
        return -1;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(_overlay);
        var handle = HandleAt(point);

        if (handle < 0)
        {
            var hit = WidgetAt(point);
            // Clicking inside the selected widget keeps it selected even if another sits on top, so it stays draggable.
            if (!(_session.SelectedWidget is { } selected && LayoutSession.GeometryOf(selected).Contains(point)))
                _session.Select(hit);
            if (_session.SelectedIndex < 0)
                return;
        }

        _gesture = new Gesture(_session.SelectedIndex, handle, point, LayoutSession.GeometryOf(_session.SelectedWidget!), ++_gestureCount);
        _overlay.CaptureMouse();
        Focus();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(_overlay);

        if (_gesture is not { } g)
        {
            var handle = HandleAt(point);
            _overlay.Cursor = handle >= 0 ? HandleCursors[handle]
                : WidgetAt(point) >= 0 ? Cursors.SizeAll
                : Cursors.Arrow;
            return;
        }

        var dx = point.X - g.Start.X;
        var dy = point.Y - g.Start.Y;
        var r = g.StartRect;
        Rect result;

        if (g.Handle < 0)
        {
            result = new Rect(SnapValue(r.X + dx), SnapValue(r.Y + dy), r.Width, r.Height);
        }
        else
        {
            double left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom;
            if (g.Handle is 0 or 6 or 7)
                left = Math.Min(SnapValue(r.Left + dx), right - MinimumSize);
            if (g.Handle is 2 or 3 or 4)
                right = Math.Max(SnapValue(r.Right + dx), left + MinimumSize);
            if (g.Handle is 0 or 1 or 2)
                top = Math.Min(SnapValue(r.Top + dy), bottom - MinimumSize);
            if (g.Handle is 4 or 5 or 6)
                bottom = Math.Max(SnapValue(r.Bottom + dy), top + MinimumSize);
            result = new Rect(left, top, right - left, bottom - top);
        }

        if (result != LayoutSession.GeometryOf(_session.Document.Widgets[g.Index]))
            _session.SetGeometry(g.Index, result, $"drag:{g.Id}", this);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _gesture = null;
        _overlay.ReleaseMouseCapture();
    }

    private double SnapValue(double value)
    {
        var snap = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? 1 : Math.Max(1, Snap);
        return Math.Round(value / snap) * snap;
    }

    private sealed record Gesture(int Index, int Handle, Point Start, Rect StartRect, int Id);
}
