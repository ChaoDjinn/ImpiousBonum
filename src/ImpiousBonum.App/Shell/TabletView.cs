using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Remote;

namespace ImpiousBonum.App.Shell;

/// <summary>
/// The dashboard for a tablet's browser: a copy of the dashboard that is never on screen, drawn to a picture once a
/// second while a tablet is watching and served by <see cref="TabletServer"/>. It follows the same layout, theme and
/// game layouts as the real one, and keeps going while the real window is hidden over a fullscreen game.
/// With no tablet watching it draws nothing.
/// </summary>
public sealed class TabletView : IDisposable
{
    private const int JpegQuality = 85;

    private readonly DashboardView _view = new() { AnimateBackground = false };
    private readonly Border _host;
    private TabletServer? _server;
    private LayoutDocument? _layout;
    private ThemeSettings? _theme;
    private bool _built;
    private RenderTargetBitmap? _bitmap;
    private byte[] _pixels = [];
    private byte[] _previousPixels = [];
    private bool _hasPrevious;
    private long _sequence;

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private bool _allowTaps;

    public TabletView()
    {
        // Behind a transparent theme the tablet would otherwise show black JPEG corners.
        _host = new Border { Child = _view, SnapsToDevicePixels = true };
        // A button pressed from the tablet asks for its action like one on the real dashboard.
        _host.AddHandler(Widget.ActionEvent, new EventHandler<WidgetActionEventArgs>((_, e) =>
        {
            e.Handled = true;
            ActionRequested?.Invoke(this, e.Action);
        }));
    }

    /// <summary>A button on the tablet was pressed (only while <see cref="AllowTaps"/>).</summary>
    public event EventHandler<WidgetAction>? ActionRequested;

    /// <summary>
    /// Let tablets press the layout's buttons and media controls. Off by default: anyone with the link could then
    /// press them. Nothing else on the dashboard can be pressed from a tablet.
    /// </summary>
    public bool AllowTaps
    {
        get => _allowTaps;
        set
        {
            _allowTaps = value;
            if (_server is not null)
                _server.Tap = value ? TapFromServer : null;
        }
    }

    /// <summary>From the server's thread: press on the UI thread, waiting a moment for the answer.</summary>
    private bool TapFromServer(double x, double y)
    {
        try
        {
            return _dispatcher.Invoke(() => PressAt(x, y), TimeSpan.FromSeconds(2)) is true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Presses the button at a point of the picture (0–1 across and down), if there's one there: a button in a
    /// buttons widget, or a media control. Returns whether one was pressed.
    /// </summary>
    internal bool PressAt(double x, double y)
    {
        if (!_built || !_allowTaps || _view.Width <= 0 || _view.Height <= 0)
            return false;
        var point = new Point(x * _view.Width, y * _view.Height);
        if (VisualTreeHelper.HitTest(_view, point)?.VisualHit is not DependencyObject hit)
            return false;
        TapTarget? target = null;
        for (var node = hit; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (target is null && node is TapTarget tap)
                target = tap;
            if (node is Widget widget)
            {
                if (target is null || widget is not (ButtonsWidget or MediaWidget))
                    return false;
                target.Tap();
                return true;
            }
        }
        return false;
    }

    public bool IsRunning => _server is not null;

    /// <summary>Starts serving. Throws <see cref="System.Net.Sockets.SocketException"/> if the port is taken.</summary>
    public void Start(int port, string token)
    {
        Stop();
        var server = new TabletServer(port, token) { Tap = _allowTaps ? TapFromServer : null };
        server.Start();
        _server = server;
    }

    /// <summary>Old links stop working from their next request.</summary>
    public void SetToken(string token)
    {
        if (_server is not null)
            _server.Token = token;
    }

    public void Stop()
    {
        _server?.Dispose();
        _server = null;
        // Nothing to draw for; let the widgets and the last picture go.
        _built = false;
        _view.Children.Clear();
        _bitmap = null;
        _pixels = _previousPixels = [];
        _hasPrevious = false;
    }

    /// <summary>Shows this layout from the next frame on. Building waits until a tablet is watching.</summary>
    public void Build(LayoutDocument layout, ThemeSettings theme)
    {
        _layout = layout;
        _theme = theme;
        _built = false;
    }

    /// <summary>Follows a widget being dragged in the editor, like <see cref="DashboardView.SetGeometry"/>.</summary>
    public void SetGeometry(int index, double x, double y, double width, double height)
    {
        if (_built)
            _view.SetGeometry(index, x, y, width, height);
    }

    public void Refresh(MetricStore store, DateTime now)
    {
        if (_server is { IsWatched: true } server && Draw(store, now) is { } frame)
            server.Frame = frame;
    }

    /// <summary>Updates and draws the view; returns null when it looks exactly as it did last time.</summary>
    internal TabletFrame? Draw(MetricStore store, DateTime now)
    {
        if (_layout is null || _theme is null)
            return null;

        if (!_built)
        {
            _view.Build(_layout, _theme);
            _host.Background = Theme.From(_theme).Background;
            _built = true;
            _hasPrevious = false;
        }

        _view.Refresh(store, now);
        var width = (int)Math.Ceiling(_view.Width);
        var height = (int)Math.Ceiling(_view.Height);
        if (width <= 0 || height <= 0)
            return null;

        var size = new Size(width, height);
        _host.Measure(size);
        _host.Arrange(new Rect(size));
        _host.UpdateLayout();

        if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
        {
            _bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            _pixels = new byte[width * height * 4];
            _previousPixels = new byte[_pixels.Length];
            _hasPrevious = false;
        }
        else
        {
            _bitmap.Clear();
        }
        _bitmap.Render(_host);

        _bitmap.CopyPixels(_pixels, width * 4, 0);
        if (_hasPrevious && _pixels.AsSpan().SequenceEqual(_previousPixels))
            return null;
        (_pixels, _previousPixels) = (_previousPixels, _pixels);
        _hasPrevious = true;

        var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
        encoder.Frames.Add(BitmapFrame.Create(_bitmap));
        using var jpeg = new MemoryStream();
        encoder.Save(jpeg);
        return new TabletFrame(jpeg.ToArray(), "image/jpeg", ++_sequence);
    }

    public void Dispose() => Stop();
}

