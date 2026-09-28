using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using ImpiousBonum.App.Editor;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App;

/// <summary>
/// Borderless, non-activating window pinned to a pixel rectangle on a chosen monitor.
/// Hidden from the taskbar and Alt+Tab, and tapping it never steals focus from what you're doing on the main screen.
/// When windowed and unlocked it can be dragged anywhere and resized from its edges, keeping the layout's shape.
/// </summary>
public partial class DashboardWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExTransparent = 0x00000020;
    private const int WsExLayered = 0x00080000;
    private const uint LwaAlpha = 0x00000002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    /// <summary>How close to the edge (in device-independent units) a press starts a resize rather than a move.</summary>
    private const double ResizeBorder = 10;

    private PixelRect? _target;
    private EditOverlay? _overlay;
    private bool _movable;
    private bool _clickThrough;
    private bool _dragging;
    private WindowEdges _resizeEdges;
    private (PixelRect Rect, NativePoint Cursor) _resizeStart;

    public DashboardWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => _overlay?.Update();
    }

    public DashboardView Dashboard => View;

    /// <summary>Right-click, or press and hold with a finger (Windows turns that into a right-click): open the layout editor.</summary>
    public event EventHandler? EditRequested;

    public bool IsEditing => _overlay is not null;

    /// <summary>The user dragged or resized the window; the new rectangle is in physical screen pixels.</summary>
    public event EventHandler<PixelRect>? PlacementChanged;

    /// <summary>Dragging and resizing are only possible when movable and the layout editor is closed.</summary>
    private bool CanMove => _movable && !IsEditing;

    /// <summary>
    /// <paramref name="movable"/>: can be dragged and resized (windowed and unlocked).
    /// <paramref name="clickThrough"/>: clicks pass through to the windows underneath.
    /// </summary>
    public void SetBehaviour(bool movable, bool clickThrough, bool topmost)
    {
        _movable = movable;
        _clickThrough = clickThrough && !movable;
        Topmost = topmost;
        ApplyClickThrough();
        UpdateOutline();
    }

    /// <summary>Raises the window above other normal windows without taking focus.</summary>
    public void BringForward()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != 0)
            SetWindowPos(handle, 0, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    /// <summary>
    /// Handle size for fingers: a tenth of the window height (48 px on a 480 px strip), within sensible limits.
    /// Small high-density screens need physically bigger targets than a desktop monitor.
    /// </summary>
    private double TouchHandleSize => Math.Clamp(ActualHeight / 10, 24, 64);

    /// <summary>Turns the dashboard into an editing surface synced with the layout editor: tap to select, drag to move or resize.</summary>
    public void BeginEdit(LayoutSession session)
    {
        if (_overlay is not null)
            return;
        _overlay = new EditOverlay(session, TouchHandleSize, () => EditOverlayScale.UnitsPerPixel(Scaler, session.Document));
        Stage.Children.Add(_overlay);
        ApplyClickThrough();
        UpdateOutline();
    }

    public void EndEdit()
    {
        if (_overlay is null)
            return;
        _overlay.Detach();
        Stage.Children.Remove(_overlay);
        _overlay = null;
        ApplyClickThrough();
        UpdateOutline();
    }

    private void UpdateOutline()
    {
        MoveOutline.Visibility = CanMove ? Visibility.Visible : Visibility.Collapsed;
        if (!CanMove)
            Cursor = null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!CanMove || e.Handled)
            return;

        var point = e.GetPosition(this);
        var edges = WindowGeometry.EdgesAt(point.X, point.Y, ActualWidth, ActualHeight, ResizeBorder);
        e.Handled = true;
        if (edges != WindowEdges.None)
        {
            if (CurrentRect() is { } rect && GetCursorPos(out var cursor) && CaptureMouse())
            {
                _resizeEdges = edges;
                _resizeStart = (rect, cursor);
            }
            return;
        }

        // DragMove runs Windows' own move loop (snapping across monitors included) and returns on release.
        if (CurrentRect() is not { } start)
            return;
        _dragging = true;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The button was already released (a very quick tap).
        }
        finally
        {
            _dragging = false;
        }

        // Crossing onto a monitor with different scaling makes Windows resize the window; keep the pixel size.
        if (CurrentRect() is { } moved && moved != start)
            FinishPlacement(start with { X = moved.X, Y = moved.Y });
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_resizeEdges != WindowEdges.None)
        {
            if (!GetCursorPos(out var cursor))
                return;
            var (rect, origin) = _resizeStart;
            _target = WindowGeometry.Resize(rect, _resizeEdges, cursor.X - origin.X, cursor.Y - origin.Y, AspectRatio(rect));
            ApplyPlacement();
            return;
        }

        if (!CanMove)
            return;
        var point = e.GetPosition(this);
        Cursor = WindowGeometry.EdgesAt(point.X, point.Y, ActualWidth, ActualHeight, ResizeBorder) switch
        {
            WindowEdges.Left or WindowEdges.Right => Cursors.SizeWE,
            WindowEdges.Top or WindowEdges.Bottom => Cursors.SizeNS,
            WindowEdges.Left | WindowEdges.Top or WindowEdges.Right | WindowEdges.Bottom => Cursors.SizeNWSE,
            WindowEdges.Right | WindowEdges.Top or WindowEdges.Left | WindowEdges.Bottom => Cursors.SizeNESW,
            _ => Cursors.SizeAll,
        };
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_resizeEdges != WindowEdges.None)
            ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_resizeEdges == WindowEdges.None)
            return;
        _resizeEdges = WindowEdges.None;
        if (_target is { } rect && rect != _resizeStart.Rect)
            FinishPlacement(rect);
    }

    private void FinishPlacement(PixelRect rect)
    {
        _target = rect;
        ApplyPlacement();
        PlacementChanged?.Invoke(this, rect);
    }

    /// <summary>The layout's shape (width / height), so resizing never adds bars; the window's own shape before a layout is shown.</summary>
    private double AspectRatio(PixelRect fallback) =>
        View.Width > 0 && View.Height > 0 && !double.IsNaN(View.Width) && !double.IsNaN(View.Height)
            ? View.Width / View.Height
            : (double)fallback.Width / Math.Max(1, fallback.Height);

    private PixelRect? CurrentRect()
    {
        var handle = new WindowInteropHelper(this).Handle;
        return handle != 0 && GetWindowRect(handle, out var r) ? new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) : null;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        EditRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    /// <summary>Moves the window to a rectangle in physical screen pixels.</summary>
    public void PlaceOn(PixelRect rect)
    {
        _target = rect;
        ApplyPlacement();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowLongPtrW(handle, GwlExStyle, GetWindowLongPtrW(handle, GwlExStyle) | WsExToolWindow | WsExNoActivate);
        ApplyClickThrough();
        ApplyPlacement();
    }

    /// <summary>
    /// Click-through needs a layered window as well as WS_EX_TRANSPARENT; fully opaque, so it looks the same.
    /// Off while the editor is open so widgets can still be tapped and dragged on the dashboard.
    /// </summary>
    private void ApplyClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0)
            return;

        var on = _clickThrough && !IsEditing;
        var style = GetWindowLongPtrW(handle, GwlExStyle);
        var wanted = on ? style | WsExTransparent | WsExLayered : style & ~(nint)(WsExTransparent | WsExLayered);
        if (wanted == style)
            return;
        SetWindowLongPtrW(handle, GwlExStyle, wanted);
        if (on)
            SetLayeredWindowAttributes(handle, 0, 255, LwaAlpha);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        // WPF resizes to the "suggested" rect when crossing into a monitor with different scaling; put it back.
        // Not mid-drag, which would yank the window back to where it started; the drag fixes the size when it ends.
        if (!_dragging)
            Dispatcher.BeginInvoke(ApplyPlacement);
    }

    private void ApplyPlacement()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0 || _target is not { } r)
            return;

        // Twice: the first move may cross monitors and trigger a DPI change that alters the size.
        SetWindowPos(handle, 0, r.X, r.Y, r.Width, r.Height, SwpNoZOrder | SwpNoActivate);
        SetWindowPos(handle, 0, r.X, r.Y, r.Width, r.Height, SwpNoZOrder | SwpNoActivate);
    }

    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtrW(nint hWnd, int index);

    [DllImport("user32.dll")]
    private static extern nint SetWindowLongPtrW(nint hWnd, int index, nint newLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(nint hWnd, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X, Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hWnd, nint insertAfter, int x, int y, int width, int height, uint flags);
}
