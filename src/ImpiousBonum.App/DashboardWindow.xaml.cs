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
/// </summary>
public partial class DashboardWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    private PixelRect? _target;
    private EditOverlay? _overlay;

    public DashboardWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => _overlay?.Update();
    }

    public DashboardView Dashboard => View;

    /// <summary>Right-click, or press and hold with a finger (Windows turns that into a right-click): open the layout editor.</summary>
    public event EventHandler? EditRequested;

    public bool IsEditing => _overlay is not null;

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
    }

    public void EndEdit()
    {
        if (_overlay is null)
            return;
        _overlay.Detach();
        Stage.Children.Remove(_overlay);
        _overlay = null;
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
        ApplyPlacement();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        // WPF resizes to the "suggested" rect when crossing into a monitor with different scaling; put it back.
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hWnd, nint insertAfter, int x, int y, int width, int height, uint flags);
}
