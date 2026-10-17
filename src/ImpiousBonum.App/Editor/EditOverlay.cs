using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ImpiousBonum.App.Editor;

/// <summary>
/// Sits on top of a <see cref="DashboardView"/> at the same canvas size and turns it into an editing surface:
/// outlines for every widget, handles on the selected one, and select / move / resize with mouse or touch. Ctrl or
/// Shift and a click add a widget to the selection or take it out, a box dragged on empty canvas selects the widgets
/// inside it, and the selection moves together. Edges and centres snap to other widgets' and the canvas's, with guides
/// showing what lined up. Used by the editor's preview and, with bigger handles, on the dashboard itself.
/// </summary>
public sealed class EditOverlay : Canvas
{
    private static readonly Cursor[] HandleCursors =
    [
        Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE,
        Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE,
    ];

    /// <summary>How near, in screen pixels, a dragged edge or centre has to come to another to snap to it.</summary>
    private const double SnapReach = 6;

    private readonly LayoutSession _session;
    private readonly double _handleSize;
    private readonly double _snapReach;
    private readonly Func<double> _unitsPerPixel;
    private readonly Rectangle[] _handles = new Rectangle[8];
    private readonly List<Rectangle> _outlines = [];
    private readonly List<Rectangle> _selected = [];
    private readonly List<Line> _guideLines = [];
    private readonly Rectangle _box;
    private readonly Brush _outlineBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
    private readonly Brush _accent = new SolidColorBrush(Color.FromRgb(0x3D, 0xA5, 0xFF));
    // A colour of its own, so a guide can't be mistaken for a selected widget's outline.
    private readonly Brush _guideBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x3D, 0x7F));
    private Gesture? _gesture;
    private int _gestureCount;
    private bool _placing;
    private IReadOnlyList<SnapGuide> _guides = [];
    private Rect? _boxRect;

    /// <param name="handleSize">Handle size in screen pixels (DIPs). Larger for fingers.</param>
    /// <param name="unitsPerPixel">Canvas units per screen pixel at the current zoom, so handles keep their on-screen size.</param>
    public EditOverlay(LayoutSession session, double handleSize, Func<double> unitsPerPixel)
    {
        _session = session;
        _handleSize = handleSize;
        // Fingers are less exact than a mouse, so snapping reaches further with the bigger touch handles.
        _snapReach = Math.Max(SnapReach, handleSize / 4);
        _unitsPerPixel = unitsPerPixel;
        Background = Brushes.Transparent;
        ClipToBounds = false;

        // Drags should start the moment a finger moves, not wait for Windows' press-and-hold or flick detection.
        Stylus.SetIsPressAndHoldEnabled(this, false);
        Stylus.SetIsFlicksEnabled(this, false);
        Stylus.SetIsTapFeedbackEnabled(this, false);

        for (var i = 0; i < _handles.Length; i++)
            _handles[i] = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF)), Stroke = _accent, IsHitTestVisible = false };
        _box = new Rectangle { Stroke = _accent, Fill = new SolidColorBrush(Color.FromArgb(0x28, 0x3D, 0xA5, 0xFF)), IsHitTestVisible = false };

        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        LostMouseCapture += (_, _) =>
        {
            if (_gesture is not null)
                End();
        };

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

    /// <summary>The guides showing what the widgets being dragged line up with; none between drags. For tests.</summary>
    internal IReadOnlyList<SnapGuide> Guides => _guides;

    private void OnSessionChanged(object? sender, LayoutChange change)
    {
        // A drag here redraws once it has moved everything, not once for each widget it moves.
        if (!_placing)
            Update();
    }

    private void OnSelectionChanged(object? sender, EventArgs e) => Update();

    /// <summary>Redraws outlines, handles, guides and the selection box. Cheap enough to call on every change and resize.</summary>
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

        // A dashed line around each selected widget, and handles on it when it's the only one.
        var selection = _session.Selection.Where(i => i < rects.Count).ToList();
        for (var n = 0; n < selection.Count; n++)
        {
            if (n >= _selected.Count)
                _selected.Add(new Rectangle { Stroke = _accent, StrokeDashArray = [4, 3], IsHitTestVisible = false });
            var outline = _selected[n];
            outline.StrokeThickness = 2 * unit;
            Place(outline, rects[selection[n]]);
            Children.Add(outline);
        }
        if (selection.Count == 1)
            AddHandles(rects[selection[0]], unit);

        for (var n = 0; n < _guides.Count; n++)
        {
            if (n >= _guideLines.Count)
                _guideLines.Add(new Line { Stroke = _guideBrush, IsHitTestVisible = false });
            var (line, guide) = (_guideLines[n], _guides[n]);
            (line.X1, line.Y1, line.X2, line.Y2) = guide.Vertical
                ? (guide.At, guide.From, guide.At, guide.To)
                : (guide.From, guide.At, guide.To, guide.At);
            line.StrokeThickness = unit;
            Children.Add(line);
        }

        if (_boxRect is { } box)
        {
            _box.StrokeThickness = unit;
            Place(_box, box);
            Children.Add(_box);
        }
    }

    private void AddHandles(Rect rect, double unit)
    {
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

    /// <summary>The handle under the point, when one widget is selected (several have none), or -1.</summary>
    private int HandleAt(Point point) =>
        _session.Selection.Count == 1 && _session.SelectedWidget is { } selected
            // A little extra reach beyond the drawn handle makes them easier to hit, especially by touch.
            ? EditGeometry.HandleAt(LayoutSession.GeometryOf(selected), point, _handleSize * 0.8 * _unitsPerPixel(), _handleSize * _unitsPerPixel())
            : -1;

    /// <summary>The topmost selected widget under the point, or -1.</summary>
    private int SelectedAt(Point point)
    {
        var rects = Rects();
        for (var i = rects.Count - 1; i >= 0; i--)
        {
            if (_session.IsSelected(i) && rects[i].Contains(point))
                return i;
        }
        return -1;
    }

    // ---- Gestures ---------------------------------------------------------------------------------

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Press(e.GetPosition(this), Keyboard.Modifiers);
        if (_gesture is not null)
            CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(this);
        if (_gesture is not null)
        {
            Drag(point, Keyboard.Modifiers);
            return;
        }

        var handle = HandleAt(point);
        Cursor = handle >= 0 ? HandleCursors[handle]
            : EditGeometry.TopmostAt(Rects(), point) >= 0 ? Cursors.SizeAll
            : Cursors.Arrow;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e) => Release();

    /// <summary>
    /// The mouse or a finger went down at a point on the canvas: selects what's there, and starts moving the selection,
    /// resizing it by a handle, or drawing a box on empty canvas.
    /// </summary>
    internal void Press(Point point, ModifierKeys modifiers)
    {
        // Ctrl or Shift add to the selection (or take out of it) rather than start it again.
        var adding = (modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
        var handle = adding ? -1 : HandleAt(point);
        var clickSelects = -1;

        if (handle < 0)
        {
            var hit = EditGeometry.TopmostAt(Rects(), point);
            var selected = SelectedAt(point);
            if (adding && hit >= 0)
            {
                _session.ToggleSelected(hit);
                // Taken out of the selection, so there's nothing here to drag.
                if (!_session.IsSelected(hit))
                    return;
            }
            else if (!adding && selected >= 0)
            {
                // Pressing inside a selected widget keeps the selection even if another sits on top, so it stays
                // draggable. Let go without dragging, though, and that one is all that's selected, as a click on it would do.
                if (_session.Selection.Count > 1)
                    clickSelects = selected;
            }
            else if (!adding)
            {
                // A widget in a group brings the rest of the group with it, so the group moves as one; clicked again, it's
                // the one on its own (the press above, let go without dragging).
                _session.SelectWithGroup(hit);
            }

            if (hit < 0)
            {
                // Empty canvas: a box, selecting the widgets inside it (as well as those already selected, with Ctrl or Shift).
                _gesture = new Gesture(-1, point, [], [], adding ? [.. _session.Selection] : [], -1, ++_gestureCount);
                return;
            }
        }

        var widgets = _session.Selection.ToList();
        var rects = widgets.Select(i => LayoutSession.GeometryOf(_session.Document.Widgets[i])).ToList();
        _gesture = new Gesture(handle, point, widgets, rects, [], clickSelects, ++_gestureCount);
    }

    /// <summary>The pointer moved to a point while pressed: moves, resizes or draws the box. Alt places freely, without snapping.</summary>
    internal void Drag(Point point, ModifierKeys modifiers)
    {
        if (_gesture is not { } g)
            return;

        // A press only becomes a drag once the pointer has moved a little, so a click can't nudge what it selects onto
        // the grid or a nearby edge.
        if (!g.Dragging)
        {
            var moved = (point - g.Start) / _unitsPerPixel();
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            g.Dragging = true;
        }

        if (g.IsBox)
        {
            DragBox(g, point);
            return;
        }
        if (g.Widgets.Any(i => i >= _session.Document.Widgets.Count))
            return;

        // Several widgets move as the box around them all, so they snap as one and keep their places among themselves.
        var start = g.Handle < 0 ? EditGeometry.Bounds(g.StartRects) : g.StartRects[0];
        var dx = point.X - g.Start.X;
        var dy = point.Y - g.Start.Y;
        var result = modifiers.HasFlag(ModifierKeys.Alt)
            ? new Snapped(g.Handle < 0 ? EditGeometry.Move(start, dx, dy, 1) : EditGeometry.Resize(start, g.Handle, dx, dy, 1), [])
            : Snap(g, start, dx, dy);
        _guides = result.Guides;

        IReadOnlyList<(int, Rect)> changes = g.Handle < 0
            ? [.. g.Widgets.Select((index, n) => (index, Rect.Offset(g.StartRects[n], result.Rect.X - start.X, result.Rect.Y - start.Y)))]
            : [(g.Widgets[0], result.Rect)];
        _placing = true;
        try
        {
            _session.SetGeometry(changes, $"drag:{GetHashCode()}:{g.Id}", this);
        }
        finally
        {
            _placing = false;
        }
        // Also when nothing moved: the guides may have.
        Update();
    }

    /// <summary>Where the drag puts the widgets, snapped to the grid and to the edges and centres of the rest.</summary>
    private Snapped Snap(Gesture g, Rect start, double dx, double dy)
    {
        var others = Rects().Where((_, i) => !g.Widgets.Contains(i)).ToList();
        var canvas = new Size(_session.Document.Width, _session.Document.Height);
        var reach = _snapReach * _unitsPerPixel();
        return g.Handle < 0
            ? EditGeometry.SnapMove(start, dx, dy, _session.Snap, others, canvas, reach)
            : EditGeometry.SnapResize(start, g.Handle, dx, dy, _session.Snap, others, canvas, reach);
    }

    private void DragBox(Gesture g, Point point)
    {
        var box = new Rect(g.Start, point);
        _boxRect = box;
        var inside = Rects().Select((rect, i) => (rect, i)).Where(w => box.Contains(w.rect)).Select(w => w.i);
        _session.Select(g.Before.Concat(inside));
        Update();
    }

    /// <summary>The press ended. Let go without dragging on one of several selected widgets, that one is all that's selected.</summary>
    internal void Release()
    {
        var gesture = _gesture;
        End();
        if (gesture is { Dragging: false, ClickSelects: >= 0 })
            _session.Select(gesture.ClickSelects);
    }

    private void End()
    {
        _gesture = null;
        _guides = [];
        _boxRect = null;
        ReleaseMouseCapture();
        Update();
    }

    /// <summary>A press on the canvas, until it's let go.</summary>
    /// <param name="Handle">The handle dragged to resize the one widget selected, or -1 to move.</param>
    /// <param name="Widgets">The widgets it moves or resizes, or none for a box drawn on empty canvas.</param>
    /// <param name="StartRects">Where those widgets were when it began.</param>
    /// <param name="Before">For a box, what it adds to: what was selected before, with Ctrl or Shift.</param>
    /// <param name="ClickSelects">The widget that's all that's selected if it's let go without dragging, or -1.</param>
    private sealed record Gesture(int Handle, Point Start, IReadOnlyList<int> Widgets, IReadOnlyList<Rect> StartRects, IReadOnlyList<int> Before, int ClickSelects, int Id)
    {
        /// <summary>Whether the pointer has moved far enough to count as a drag rather than a click.</summary>
        public bool Dragging { get; set; }

        public bool IsBox => Widgets.Count == 0;
    }
}
