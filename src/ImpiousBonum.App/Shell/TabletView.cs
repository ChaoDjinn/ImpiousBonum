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
/// The dashboard for tablets' browsers: copies of the dashboard that are never on screen, drawn to a picture once a
/// second while a tablet is watching and served by <see cref="TabletServer"/>. The main copy follows the same layout,
/// theme and game layouts as the real one, and keeps going while the real window is hidden over a fullscreen game.
/// A link can name a saved layout instead: that gets a copy of its own when a tablet first asks for it, which goes
/// again once none has for <see cref="TabletServer.LayoutLifetime"/>. With no tablet watching it draws nothing.
/// </summary>
public sealed class TabletView : IDisposable
{
    private readonly Picture _main;
    // The saved layouts links name, while tablets are asking for them.
    private readonly Dictionary<string, Picture> _named = new(StringComparer.OrdinalIgnoreCase);
    private TabletServer? _server;
    private long _sequence;

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private bool _allowTaps;

    public TabletView() => _main = new Picture(OnPressed);

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

    /// <summary>
    /// Finds a saved layout by the name in a link, returning it as saved, or null if there's none. Called on the
    /// thread pool. Until it's set, links can only show the main dashboard.
    /// </summary>
    public Func<string, string?>? FindLayout { get; set; }

    /// <summary>Reads a saved layout a link names, with the theme to draw it in.</summary>
    public Func<string, (LayoutDocument Layout, ThemeSettings Theme)>? LoadLayout { get; set; }

    /// <summary>The saved layouts links name that are drawn for tablets now.</summary>
    internal IReadOnlyCollection<string> Layouts => _named.Keys;

    private void OnPressed(WidgetAction action) => ActionRequested?.Invoke(this, action);

    /// <summary>From the server's thread: press on the UI thread, waiting a moment for the answer.</summary>
    private bool TapFromServer(string? layout, double x, double y)
    {
        try
        {
            return _dispatcher.Invoke(() => PressAt(layout, x, y), TimeSpan.FromSeconds(2)) is true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Presses the button at a point of the main dashboard's picture; see <see cref="PressAt(string?, double, double)"/>.</summary>
    internal bool PressAt(double x, double y) => PressAt(null, x, y);

    /// <summary>
    /// Presses the button at a point of a layout's picture (0–1 across and down), if there's one there: a button in a
    /// buttons widget, or a media control. The layout is the saved one a link names, or null for the main dashboard.
    /// Returns whether one was pressed.
    /// </summary>
    internal bool PressAt(string? layout, double x, double y)
    {
        if (!_allowTaps)
            return false;
        var picture = layout is null ? _main : _named.GetValueOrDefault(layout);
        return picture?.PressAt(x, y) == true;
    }

    public bool IsRunning => _server is not null;

    /// <summary>Starts serving. Throws <see cref="System.Net.Sockets.SocketException"/> if the port is taken.</summary>
    public void Start(int port, string token)
    {
        Stop();
        var server = new TabletServer(port, token);
        server.Start();
        Serve(server);
    }

    /// <summary>Serves through <paramref name="server"/>, started or not (the tests ask it directly).</summary>
    internal void Serve(TabletServer server)
    {
        server.Tap = _allowTaps ? TapFromServer : null;
        server.FindLayout = name => FindLayout?.Invoke(name);
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
        // Nothing to draw for; let the widgets and the last pictures go.
        _main.Release();
        foreach (var picture in _named.Values)
            picture.Release();
        _named.Clear();
    }

    /// <summary>Shows this layout on the main dashboard's tablets from the next frame on. Building waits until a tablet is watching.</summary>
    public void Build(LayoutDocument layout, ThemeSettings theme) => _main.Build(layout, theme);

    /// <summary>The editor's working copy of the saved layout <paramref name="name"/>, for tablets showing it by name (if any are).</summary>
    public void Build(string name, LayoutDocument layout, ThemeSettings theme) => _named.GetValueOrDefault(name)?.Build(layout, theme);

    /// <summary>Reads the layouts links name again before their next frames, for a save, or a theme they use changing.</summary>
    public void ReloadLayouts()
    {
        foreach (var picture in _named.Values)
            picture.Unload();
    }

    /// <summary>Follows a widget being dragged in the editor, like <see cref="DashboardView.SetGeometry"/>.</summary>
    public void SetGeometry(int index, double x, double y, double width, double height) =>
        _main.SetGeometry(index, x, y, width, height);

    /// <summary>The same, for tablets showing the saved layout <paramref name="name"/> by name.</summary>
    public void SetGeometry(string name, int index, double x, double y, double width, double height) =>
        _named.GetValueOrDefault(name)?.SetGeometry(index, x, y, width, height);

    public void Refresh(MetricStore store, DateTime now)
    {
        if (_server is not { } server)
            return;
        if (server.IsWatched && Draw(store, now) is { } frame)
            server.Frame = frame;

        // Layouts links name: made when first asked for, drawn while watched, and let go when no one has asked for a while.
        server.ForgetIdleLayouts();
        var asked = server.Layouts;
        foreach (var name in _named.Keys.Except(asked, StringComparer.OrdinalIgnoreCase).ToList())
        {
            _named[name].Release();
            _named.Remove(name);
        }
        foreach (var name in asked)
        {
            if (!server.IsLayoutWatched(name) || LoadLayout is not { } load)
                continue;
            if (!_named.TryGetValue(name, out var picture))
                _named[name] = picture = new Picture(OnPressed);
            if (!picture.HasLayout)
            {
                var (layout, theme) = load(name);
                picture.Build(layout, theme);
            }
            if (picture.Draw(store, now) is { } jpeg)
                server.SetFrame(name, new TabletFrame(jpeg, "image/jpeg", ++_sequence));
        }
    }

    /// <summary>Updates and draws the main dashboard's copy; returns null when it looks exactly as it did last time.</summary>
    internal TabletFrame? Draw(MetricStore store, DateTime now) =>
        _main.Draw(store, now) is { } jpeg ? new TabletFrame(jpeg, "image/jpeg", ++_sequence) : null;

    public void Dispose() => Stop();

    /// <summary>One copy of the dashboard, drawn off screen, and its last picture to tell whether the next one changed.</summary>
    private sealed class Picture
    {
        private const int JpegQuality = 85;

        private readonly DashboardView _view = new() { AnimateBackground = false };
        private readonly Border _host;
        private LayoutDocument? _layout;
        private ThemeSettings? _theme;
        private bool _built;
        private RenderTargetBitmap? _bitmap;
        private byte[] _pixels = [];
        private byte[] _previousPixels = [];
        private bool _hasPrevious;

        public Picture(Action<WidgetAction> pressed)
        {
            // Behind a transparent theme the tablet would otherwise show black JPEG corners.
            _host = new Border { Child = _view, SnapsToDevicePixels = true };
            // A button pressed from the tablet asks for its action like one on the real dashboard.
            _host.AddHandler(Widget.ActionEvent, new EventHandler<WidgetActionEventArgs>((_, e) =>
            {
                e.Handled = true;
                pressed(e.Action);
            }));
        }

        public bool HasLayout => _layout is not null;

        public void Build(LayoutDocument layout, ThemeSettings theme)
        {
            _layout = layout;
            _theme = theme;
            _built = false;
        }

        /// <summary>Forgets the layout, so it's read again before the next frame.</summary>
        public void Unload()
        {
            _layout = null;
            _theme = null;
            _built = false;
        }

        public void SetGeometry(int index, double x, double y, double width, double height)
        {
            if (_built)
                _view.SetGeometry(index, x, y, width, height);
        }

        public bool PressAt(double x, double y)
        {
            if (!_built || _view.Width <= 0 || _view.Height <= 0)
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

        /// <summary>Updates and draws the view as a JPEG; returns null when it looks exactly as it did last time.</summary>
        public byte[]? Draw(MetricStore store, DateTime now)
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
            return jpeg.ToArray();
        }

        /// <summary>Lets the widgets and the last picture go, keeping the layout to build again from.</summary>
        public void Release()
        {
            _built = false;
            _view.Children.Clear();
            _bitmap = null;
            _pixels = _previousPixels = [];
            _hasPrevious = false;
        }
    }
}
