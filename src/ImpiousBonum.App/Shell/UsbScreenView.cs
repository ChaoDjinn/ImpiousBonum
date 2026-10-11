using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImpiousBonum.App.Layout;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Screens;

namespace ImpiousBonum.App.Shell;

/// <summary>What tray → Display → USB screen is set to.</summary>
/// <param name="Port">"auto" to find the screen, or a serial port such as COM5.</param>
/// <param name="Layout">The layout to show on it, or null for the one the dashboard shows.</param>
/// <param name="Brightness">0–100.</param>
/// <param name="Orientation">landscape, portrait, or either with "-reversed" for a screen mounted upside down.</param>
public sealed record UsbScreenSettings(
    bool Enabled,
    string Port,
    // Left out rather than written as null, like game links' layouts: settings.json stays tidy to edit by hand.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Layout,
    int Brightness,
    string Orientation)
{
    public static readonly UsbScreenSettings Default = new(false, Auto, null, 60, "landscape");

    public const string Auto = "auto";

    public static readonly IReadOnlyList<(string Value, string Label)> Orientations =
    [
        ("landscape", "Landscape"),
        ("landscape-reversed", "Landscape, upside down"),
        ("portrait", "Portrait"),
        ("portrait-reversed", "Portrait, upside down"),
    ];

    public ScreenOrientation ScreenOrientation => Orientation switch
    {
        "landscape-reversed" => ScreenOrientation.ReverseLandscape,
        "portrait" => ScreenOrientation.Portrait,
        "portrait-reversed" => ScreenOrientation.ReversePortrait,
        _ => ScreenOrientation.Landscape,
    };
}

/// <summary>
/// The dashboard on a small USB screen (see <see cref="UsbScreen"/>): a copy of a layout that's never on the PC's
/// own screens, scaled to fit the little screen and drawn once a second, much like the tablet view.
/// </summary>
public sealed class UsbScreenView : IDisposable
{
    private readonly DashboardView _view = new();
    private readonly Viewbox _fit;
    private readonly Border _host;
    private UsbScreen? _screen;
    private string? _port;
    private LayoutDocument? _layout;
    private ThemeSettings? _theme;
    private bool _built;
    private RenderTargetBitmap? _bitmap;
    private byte[] _pixels = [];
    private UsbScreenSettings _settings = UsbScreenSettings.Default;
    private DateTime _nextLook;
    private ScreenOrientation _orientation = ScreenOrientation.Landscape;

    public UsbScreenView()
    {
        _fit = new Viewbox { Child = _view, Stretch = Stretch.Uniform };
        _host = new Border { Child = _fit, SnapsToDevicePixels = true };
    }

    /// <summary>"Off", "Connected", or what went wrong, for the tray.</summary>
    public string Status => _screen?.Status ?? (_settings.Enabled ? "No screen found: plug it in, or pick its port below" : "Off");

    /// <summary>Connects, reconnects or disconnects to match the settings.</summary>
    public void Apply(UsbScreenSettings settings)
    {
        _settings = settings;
        _nextLook = DateTime.UtcNow + LookEvery;
        var port = !settings.Enabled ? null
            : settings.Port.Equals(UsbScreenSettings.Auto, StringComparison.OrdinalIgnoreCase) ? UsbScreen.FindPort()
            : settings.Port;
        if (!string.Equals(port, _port, StringComparison.OrdinalIgnoreCase))
        {
            _screen?.Dispose();
            _screen = port is null ? null : UsbScreen.OnPort(port);
            _port = port;
        }
        _screen?.Configure(settings.ScreenOrientation, settings.Brightness);
        _orientation = settings.ScreenOrientation;
        if (_screen is null)
            Release();
    }

    /// <summary>Shows this layout from the next frame on.</summary>
    public void Build(LayoutDocument layout, ThemeSettings theme)
    {
        _layout = layout;
        _theme = theme;
        _built = false;
    }

    /// <summary>With "find it automatically" and no screen yet, how often to look again (it may be plugged in later).</summary>
    private static readonly TimeSpan LookEvery = TimeSpan.FromSeconds(10);

    public void Refresh(MetricStore store, DateTime now)
    {
        if (_screen is null && _settings.Enabled && DateTime.UtcNow >= _nextLook)
            Apply(_settings);
        if (_screen is null || Draw(store, now) is not { } frame)
            return;
        _screen.TrySend(frame.Pixels, frame.Width, frame.Height);
    }

    /// <summary>The layout drawn at the screen's size, as 32-bit BGRA.</summary>
    internal (byte[] Pixels, int Width, int Height)? Draw(MetricStore store, DateTime now)
    {
        if (_layout is null || _theme is null)
            return null;
        if (!_built)
        {
            _view.Build(_layout, _theme);
            _host.Background = Theme.From(_theme).Background;
            _built = true;
        }
        _view.Refresh(store, now);

        var (width, height) = TuringProtocol.Size(_orientation);
        var size = new Size(width, height);
        _host.Measure(size);
        _host.Arrange(new Rect(size));
        _host.UpdateLayout();

        if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
        {
            _bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            _pixels = new byte[width * height * 4];
        }
        else
            _bitmap.Clear();
        _bitmap.Render(_host);
        _bitmap.CopyPixels(_pixels, width * 4, 0);
        return (_pixels, width, height);
    }

    private void Release()
    {
        _built = false;
        _view.Children.Clear();
        _bitmap = null;
        _pixels = [];
    }

    public void Dispose()
    {
        _screen?.Dispose();
        _screen = null;
        _port = null;
    }
}
