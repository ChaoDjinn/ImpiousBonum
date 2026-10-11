using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// Something to tap on the dashboard: dims while pressed and raises <see cref="Tapped"/> when released over itself.
/// Lighter than a Button (no focus, no keyboard, no styles) and it marks the press handled, so a tap on it never
/// starts dragging an unlocked dashboard window. Windows' press-and-hold is off, so taps respond without delay.
/// </summary>
public sealed class TapTarget : Border
{
    private const double PressedOpacity = 0.55;

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
    internal void Tap() => Tapped?.Invoke(this, EventArgs.Empty);

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        Opacity = 1;
    }
}
