using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// Something to tap on the dashboard: dims while pressed and raises <see cref="Tapped"/> when released over itself.
/// Lighter than a Button (no focus, no keyboard, no styles) and it marks the press handled, so a tap on it never
/// starts dragging an unlocked dashboard window. Windows' press-and-hold is off, so taps respond without delay.
/// </summary>
public sealed class TapTarget : Border
{
    private const double PressedOpacity = 0.55;

    /// <summary>How strongly <see cref="Light"/> shows through, so an icon in the same colour stays visible on it.</summary>
    private const double LightOpacity = 0.5;

    private bool _waiting;
    private Brush? _light;

    public TapTarget()
    {
        Focusable = false;
        Cursor = Cursors.Hand;
        Stylus.SetIsPressAndHoldEnabled(this, false);
        Stylus.SetIsTapFeedbackEnabled(this, false);
        Stylus.SetIsFlicksEnabled(this, false);
    }

    public event EventHandler? Tapped;

    /// <summary>A press that moves further than this (in its own units) is a swipe, not a tap.</summary>
    private const double SwipeSlop = 12;

    private Point _pressedAt;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        e.Handled = true;
        _pressedAt = e.GetPosition(this);
        if (CaptureMouse())
            Opacity = PressedOpacity;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsMouseCaptured)
            return;
        e.Handled = true;
        var at = e.GetPosition(this);
        var inside = new Rect(RenderSize).Contains(at);
        var moved = (at - _pressedAt).Length > SwipeSlop;
        ReleaseMouseCapture();
        if (inside && !moved)
            Tap();
    }

    /// <summary>Does what a tap does.</summary>
    internal void Tap()
    {
        if (!_waiting)
            Tapped?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stays dimmed as if still pressed, and ignores taps: for a button waiting to hear how its action went.</summary>
    internal bool Waiting
    {
        get => _waiting;
        set
        {
            _waiting = value;
            Opacity = value ? PressedOpacity : 1;
        }
    }

    /// <summary>A colour to light the button up in, behind its icon and label; null for none.</summary>
    internal Brush? Light
    {
        get => _light;
        set
        {
            _light = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_light is null)
            return;
        dc.PushOpacity(LightOpacity);
        dc.DrawRoundedRectangle(_light, null, new Rect(RenderSize), CornerRadius.TopLeft, CornerRadius.TopLeft);
        dc.Pop();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        Opacity = _waiting ? PressedOpacity : 1;
    }
}
