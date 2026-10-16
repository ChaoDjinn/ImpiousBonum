using System.Windows;
using System.Windows.Controls;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App;

/// <summary>The layout's canvas at its design size. The window scales it to fit; snapshots render it directly.</summary>
public sealed class DashboardView : Canvas
{
    private readonly List<Widget> _widgets = [];
    private bool _editing;

    private BackgroundPlayer? _background;
    private ParticleLayer? _particles;
    private Window? _window;

    public DashboardView()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        // A moving background only moves while it can be seen.
        IsVisibleChanged += (_, _) => PlayBackground();
        // Closed (the editor's preview, say): let a video player go now rather than whenever it's collected.
        Unloaded += (_, _) =>
        {
            _background?.Dispose();
            _background = null;
            Watch(null);
        };
        Loaded += (_, _) =>
        {
            if (_background is null && _backgroundTheme is not null)
                Background = _backgroundTheme.CanvasBackground(Width, Height, AnimateBackground, out _background);
            Watch(Window.GetWindow(this));
            PlayBackground();
        };
    }

    /// <summary>The theme the background was made from, to make it again if the view is shown again after closing.</summary>
    private Theme? _backgroundTheme;

    /// <summary>
    /// Whether an animated GIF or video background, and the theme's particles, move. Off for copies that are never on
    /// screen (the tablet view, a USB screen, the gallery's preview), which show a still frame instead and don't hold a
    /// GIF's frames, a video player or a timer.
    /// </summary>
    public bool AnimateBackground { get; init; } = true;

    private void PlayBackground()
    {
        // Hidden or minimised: nothing is spent on what can't be seen.
        if (IsVisible && _window?.WindowState != WindowState.Minimized)
        {
            _background?.Play();
            _particles?.Play();
        }
        else
        {
            _background?.Pause();
            _particles?.Pause();
        }
    }

    /// <summary>Follows the window the view is in, to stop moving while it's minimised.</summary>
    private void Watch(Window? window)
    {
        if (_window is not null)
            _window.StateChanged -= OnWindowStateChanged;
        _window = window;
        if (_window is not null)
            _window.StateChanged += OnWindowStateChanged;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e) => PlayBackground();

    /// <param name="themeSettings">The layout's theme once resolved (see <see cref="ThemeLibrary.Resolve"/>).</param>
    public void Build(LayoutDocument layout, ThemeSettings themeSettings)
    {
        var theme = Theme.From(themeSettings);
        _particles?.Pause();
        Children.Clear();
        _widgets.Clear();
        Width = layout.Width;
        Height = layout.Height;
        _background?.Dispose();
        _backgroundTheme = theme;
        Background = theme.CanvasBackground(layout.Width, layout.Height, AnimateBackground, out _background);
        // Over the background, behind the widgets.
        _particles = ParticleLayer.Create(themeSettings, layout.Width, layout.Height, AnimateBackground);
        if (_particles is not null)
            Children.Add(_particles);
        PlayBackground();

        foreach (var definition in layout.Widgets)
        {
            var widget = WidgetFactory.Create(definition, theme);
            widget.Editing = _editing;
            widget.Effect = theme.Glow;
            widget.Width = definition.GetDouble("width", 200);
            widget.Height = definition.GetDouble("height", 100);
            SetLeft(widget, definition.GetDouble("x", 0));
            SetTop(widget, definition.GetDouble("y", 0));
            Children.Add(widget);
            _widgets.Add(widget);
        }
    }

    /// <summary>True while the layout is being edited: widgets that hide when they have nothing to show stay visible, faded.</summary>
    public bool Editing
    {
        get => _editing;
        set
        {
            _editing = value;
            foreach (var widget in _widgets)
                widget.Editing = value;
        }
    }

    /// <summary>Moves or resizes one widget without rebuilding anything; used while dragging in the editor.</summary>
    public void SetGeometry(int index, double x, double y, double width, double height)
    {
        if (index < 0 || index >= _widgets.Count)
            return;
        var widget = _widgets[index];
        SetLeft(widget, x);
        SetTop(widget, y);
        widget.Width = width;
        widget.Height = height;
    }

    public void Refresh(MetricStore store, DateTime now)
    {
        foreach (var widget in _widgets)
            widget.Refresh(store, now);
    }
}
