using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ImpiousBonum.App.Editor;

/// <summary>
/// Sits on top of a <see cref="DashboardView"/> at the same canvas size and turns it into an editing surface:
/// outlines for every widget, handles on the selected one, and select / move / resize with mouse or touch.
/// Used by the editor's preview and, with bigger handles, on the dashboard itself.
/// </summary>
public sealed class EditOverlay : Canvas
{
    private static readonly Cursor[] HandleCursors =
    [
        Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE,
        Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE,
    ];

    private readonly LayoutSession _session;
    private readonly double _handleSize;
    private readonly Func<double> _unitsPerPixel;
    private readonly Rectangle _selection;
    private readonly Rectangle[] _handles = new Rectangle[8];
    private readonly List<Rectangle> _outlines = [];
    private readonly Brush _outlineBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
    private Gesture? _gesture;
    private int _gestureCount;

    /// <param name="handleSize">Handle size in screen pixels (DIPs). Larger for fingers.</param>
    /// <param name="unitsPerPixel">Canvas units per screen pixel at the current zoom, so handles keep their on-screen size.</param>
    public EditOverlay(LayoutSession session, double handleSize, Func<double> unitsPerPixel)
    {
        _session = session;
        _handleSize = handleSize;
        _unitsPerPixel = unitsPerPixel;
        Background = Brushes.Transparent;
        ClipToBounds = false;

        // Drags should start the moment a finger moves, not wait for Windows' press-and-hold or flick detection.
        Stylus.SetIsPressAndHoldEnabled(this, false);
        Stylus.SetIsFlicksEnabled(this, false);
        Stylus.SetIsTapFeedbackEnabled(this, false);

        var accent = new SolidColorBrush(Color.FromRgb(0x3D, 0xA5, 0xFF));
        _selection = new Rectangle { Stroke = accent, StrokeDashArray = [4, 3], IsHitTestVisible = false };
        for (var i = 0; i < _handles.Length; i++)
            _handles[i] = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF)), Stroke = accent, IsHitTestVisible = false };

        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        LostMouseCapture += (_, _) => _gesture = null;

        session.Changed += OnSessionChanged;
        session.SelectionChanged += OnSelectionChanged;
        Update();
    }

    /// <summary>Stops listening to the session. Call before throwing the overlay away.</summary>
    public void Detach()
    {
        _session.Changed -= OnSessionChanged;
        _session.SelectionChanged -= OnSelectionChanged;
        ReleaseMouseCapture();
    }

    private void OnSessionChanged(object? sender, LayoutChange change) => Update();

    private void OnSelectionChanged(object? sender, EventArgs e) => Update();

    /// <summary>Redraws outlines and handles. Cheap enough to call on every change and resize.</summary>
    public void Update()
    {
        var document = _session.Document;
        Width = document.Width;
        Height = document.Height;
        Children.Clear();
        var unit = _unitsPerPixel();

        // Faint outlines show where every widget is, including ones that are currently blank.
        var rects = Rects();
        for (var i = 0; i < rects.Count; i++)
        {
            if (i >= _outlines.Count)
                _outlines.Add(new Rectangle { IsHitTestVisible = false, StrokeDashArray = [2, 4] });
            var outline = _outlines[i];
            outline.Stroke = _outlineBrush;
            outline.StrokeThickness = unit;
            Place(outline, rects[i]);
            Children.Add(outline);
        }

        if (_session.SelectedWidget is not { } selected)
            return;

        var rect = LayoutSession.GeometryOf(selected);
        _selection.StrokeThickness = 2 * unit;
        Place(_selection, rect);
        Children.Add(_selection);

        var size = _handleSize * unit;
        var visible = EditGeometry.VisibleHandles(rect, size);
        var points = EditGeometry.HandlePoints(rect);
        for (var i = 0; i < _handles.Length; i++)
        {
            if (!visible[i])
                continue;
            var handle = _handles[i];
            // Round, so they cover less of the widget being adjusted.
            handle.RadiusX = handle.RadiusY = size / 2;
            handle.StrokeThickness = unit;
            Place(handle, new Rect(points[i].X - size / 2, points[i].Y - size / 2, size, size));
            Children.Add(handle);
        }
    }

    private List<Rect> Rects() => _session.Document.Widgets.Select(LayoutSession.GeometryOf).ToList();

    private static void Place(FrameworkElement element, Rect rect)
    {
        SetLeft(element, rect.X);
        SetTop(element, rect.Y);
        element.Width = rect.Width;
        element.Height = rect.Height;
    }

    private int HandleAt(Point point) =>
        _session.SelectedWidget is { } selected
            // A little extra reach beyond the drawn handle makes them easier to hit, especially by touch.
            ? EditGeometry.HandleAt(LayoutSession.GeometryOf(selected), point, _handleSize * 0.8 * _unitsPerPixel(), _handleSize * _unitsPerPixel())
            : -1;

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(this);
        var handle = HandleAt(point);

        if (handle < 0)
        {
            // Pressing inside the selected widget keeps it selected even if another sits on top, so it stays draggable.
            if (!(_session.SelectedWidget is { } selected && LayoutSession.GeometryOf(selected).Contains(point)))
                _session.Select(EditGeometry.TopmostAt(Rects(), point));
            if (_session.SelectedIndex < 0)
                return;
        }

        _gesture = new Gesture(_session.SelectedIndex, handle, point, LayoutSession.GeometryOf(_session.SelectedWidget!), ++_gestureCount);
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(this);

        if (_gesture is not { } g)
        {
            var handle = HandleAt(point);
            Cursor = handle >= 0 ? HandleCursors[handle]
                : EditGeometry.TopmostAt(Rects(), point) >= 0 ? Cursors.SizeAll
                : Cursors.Arrow;
            return;
        }

        if (g.Index >= _session.Document.Widgets.Count)
            return;

        var snap = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? 1 : _session.Snap;
        var dx = point.X - g.Start.X;
        var dy = point.Y - g.Start.Y;
        var result = g.Handle < 0
            ? EditGeometry.Move(g.StartRect, dx, dy, snap)
            : EditGeometry.Resize(g.StartRect, g.Handle, dx, dy, snap);
        _session.SetGeometry(g.Index, result, $"drag:{GetHashCode()}:{g.Id}", this);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _gesture = null;
        ReleaseMouseCapture();
    }

    private sealed record Gesture(int Index, int Handle, Point Start, Rect StartRect, int Id);
}
