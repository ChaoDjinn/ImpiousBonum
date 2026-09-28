using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImpiousBonum.App.Layout;
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

    private readonly DashboardView _view = new();
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

    public TabletView()
    {
        // Behind a transparent theme the tablet would otherwise show black JPEG corners.
        _host = new Border { Child = _view, SnapsToDevicePixels = true };
    }

    public bool IsRunning => _server is not null;

    /// <summary>Starts serving. Throws <see cref="System.Net.Sockets.SocketException"/> if the port is taken.</summary>
    public void Start(int port, string token)
    {
        Stop();
        var server = new TabletServer(port, token);
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

