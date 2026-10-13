using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ImpiousBonum.App.Widgets;

/// <summary>Which ways a <see cref="ShrinkToFit"/> makes its child fit.</summary>
public enum FitDirection
{
    /// <summary>Shrinks it when it's wider than its space: a line of text, or a label and its value.</summary>
    Width,

    /// <summary>
    /// Shrinks it when its text would be cut off at the top or bottom. It's laid out at the width it gets, so text that
    /// ends with … still does: for a stack of rows, each fitting its own width. Anything in it that can't get narrower,
    /// such as a line with no …, needs a Width fit of its own.
    /// </summary>
    Height,

    /// <summary>Either: a line of text, or a few stacked lines, with a widget to itself.</summary>
    Both,
}

/// <summary>
/// Shrinks its child evenly when it doesn't fit, so a long reading or a big font fits instead of being cut off. Never
/// enlarges it, so text that fits looks exactly as it would without this. The child keeps its own alignment in the
/// space, and stretches across it if it's set to.
/// </summary>
/// <remarks>
/// For the height only the ink counts, not the room a font leaves above and below its letters: big text's line is
/// often a little taller than its widget without anything visibly cut off, and that stays as it was.
/// </remarks>
public sealed class ShrinkToFit : Decorator
{
    /// <summary>How many times in a row a Height fit lays its child out again, wider, to find its scale.</summary>
    private const int MostRelayouts = 8;

    private double _widthScale = 1;
    private ScaleTransform? _transform;

    // A Height fit lays a child that stretches out at the width it'll get once shrunk: 1 / _layoutScale times its
    // space. _relayout says the next measure is its own, for a smaller scale, rather than for new text or a new size.
    private double _layoutScale = 1;
    private bool _relayout;
    private int _relayouts;

    // The child's ink when it decided the scale, watched so text that changes without changing size is fitted again.
    private Rect? _ink;

    public FitDirection Direction { get; init; } = FitDirection.Width;

    /// <summary>How much the child is shrunk, from 1 (not at all) down; for tests.</summary>
    public double Scale { get; private set; } = 1;

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is not { } child)
            return default;
        double scale;
        if (Direction == FitDirection.Height)
        {
            // At the width it has, so text that ends with … still does; shrunk, that's wider, and lines in a Width fit of
            // their own use all of it. New text or a new size starts again from full size.
            if (!_relayout)
            {
                _layoutScale = 1;
                _relayouts = 0;
            }
            _relayout = false;
            _widthScale = 1;
            scale = Stretches(child) ? _layoutScale : 1;
            child.Measure(new Size(constraint.Width / scale, double.PositiveInfinity));
        }
        else
        {
            // At its natural size, so a line of text says how wide it really is.
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            scale = _widthScale = ScaleFor(child.DesiredSize.Width, constraint.Width);
        }
        var natural = child.DesiredSize;
        return new Size(Math.Min(natural.Width * scale, constraint.Width), Math.Min(natural.Height * scale, constraint.Height));
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child is not { } child)
            return arrangeSize;
        var relayout = Direction == FitDirection.Height && Stretches(child);
        var scale = relayout ? _layoutScale : _widthScale;
        Place(child, arrangeSize, scale);
        Rect? ink = null;
        // Arranging drew it, so its ink is known; it's only worth a look when the child is taller than its space. Shrunk,
        // it's laid out wider, which can show more of text that ends with …, so it takes another look.
        for (var looks = 1; Direction != FitDirection.Width && child.DesiredSize.Height * scale > arrangeSize.Height; looks++)
        {
            ink = Ink(child);
            var fit = ink.Value.IsEmpty ? 1 : ScaleForInk(Anchor(child), child.DesiredSize.Height, ink.Value.Top, ink.Value.Bottom, arrangeSize.Height);
            if (fit >= scale || looks > 3)
                break;
            scale = fit;
            Place(child, arrangeSize, scale);
            ink = null;
            // Lines that were shrunk to fit its width grow with it, and get taller: lay it out again at that width.
            if (relayout && fit < _layoutScale * 0.99 && _relayouts++ < MostRelayouts)
            {
                _layoutScale = fit;
                _relayout = true;
                InvalidateMeasure();
                ink = Ink(child);
                break;
            }
        }
        Watch(ink);
        Scale = scale;
        return arrangeSize;
    }

    /// <summary>
    /// Lays the child out where its alignment puts it, as if the space were 1 / <paramref name="scale"/> times bigger,
    /// then draws it that much smaller from its top-left.
    /// </summary>
    private void Place(UIElement child, Size space, double scale)
    {
        var natural = child.DesiredSize;
        var horizontal = (child as FrameworkElement)?.HorizontalAlignment ?? HorizontalAlignment.Stretch;
        var anchor = Anchor(child);
        var width = horizontal == HorizontalAlignment.Stretch ? Math.Max(natural.Width, space.Width / scale) : natural.Width;
        var height = anchor < 0 ? Math.Max(natural.Height, space.Height / scale) : natural.Height;
        var x = horizontal switch
        {
            HorizontalAlignment.Center => (space.Width - width * scale) / 2,
            HorizontalAlignment.Right => space.Width - width * scale,
            _ => 0,
        };
        var y = Math.Max(anchor, 0) * (space.Height - height * scale);

        // One transform, changed only when the scale does.
        if (scale < 1)
        {
            _transform ??= new ScaleTransform();
            _transform.ScaleX = _transform.ScaleY = scale;
            child.RenderTransform = _transform;
        }
        else if (_transform is not null)
            child.RenderTransform = Transform.Identity;
        child.Arrange(new Rect(x, y, width, height));
    }

    /// <summary>
    /// Keeps an eye on the child's ink while it decides the scale: text whose size stays the same as it changes (digits
    /// in a monospaced font, say) can still draw taller or shorter, and nothing else would fit it again.
    /// </summary>
    private void Watch(Rect? ink)
    {
        if (ink is not null && _ink is null)
            LayoutUpdated += CheckInk;
        else if (ink is null && _ink is not null)
            LayoutUpdated -= CheckInk;
        _ink = ink;
    }

    private void CheckInk(object? sender, EventArgs e)
    {
        if (_ink is { } ink && Child is { } child && Ink(child) != ink)
        {
            InvalidateMeasure();
            InvalidateArrange();
        }
    }

    private static bool Stretches(UIElement child) =>
        ((child as FrameworkElement)?.HorizontalAlignment ?? HorizontalAlignment.Stretch) == HorizontalAlignment.Stretch;

    /// <summary>Where the child sits in the height: 0 at the top, 0.5 in the middle, 1 at the bottom, or -1 stretched (filling it from the top).</summary>
    private static double Anchor(UIElement child) => (child as FrameworkElement)?.VerticalAlignment switch
    {
        VerticalAlignment.Top => 0,
        VerticalAlignment.Center => 0.5,
        VerticalAlignment.Bottom => 1,
        _ => -1,
    };

    /// <summary>
    /// What <paramref name="visual"/> and everything in it draws, in its own space. Only what's shown counts: a hidden
    /// or collapsed element keeps its last drawing, and a clip hides what's outside it.
    /// </summary>
    internal static Rect Ink(Visual visual)
    {
        var ink = Rect.Empty;
        AddInk(visual, Matrix.Identity, null, ref ink);
        return ink;
    }

    /// <param name="toRoot">From <paramref name="visual"/>'s space to the space the ink is measured in.</param>
    /// <param name="clip">What the clips above it let through, in that space.</param>
    private static void AddInk(Visual visual, Matrix toRoot, Rect? clip, ref Rect ink)
    {
        var content = VisualTreeHelper.GetContentBounds(visual);
        if (!content.IsEmpty)
        {
            content.Transform(toRoot);
            if (clip is { } shown)
                content.Intersect(shown);
            // Ink that only touches a clip's edge isn't drawn.
            if (!content.IsEmpty && content.Width * content.Height > 0)
                ink.Union(content);
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
        {
            if (VisualTreeHelper.GetChild(visual, i) is not Visual child || child is UIElement { Visibility: not Visibility.Visible })
                continue;
            var toParent = VisualTreeHelper.GetTransform(child)?.Value ?? Matrix.Identity;
            var offset = VisualTreeHelper.GetOffset(child);
            toParent.Translate(offset.X, offset.Y);
            var childToRoot = toParent * toRoot;
            var childClip = clip;
            if (VisualTreeHelper.GetClip(child) is { } own)
            {
                var bounds = own.Bounds;
                bounds.Transform(childToRoot);
                if (childClip is { } outer)
                    bounds.Intersect(outer);
                if (bounds.IsEmpty)
                    continue;
                childClip = bounds;
            }
            AddInk(child, childToRoot, childClip, ref ink);
        }
    }

    internal static double ScaleFor(double natural, double available) =>
        natural > 0 && available > 0 && natural > available ? available / natural : 1;

    /// <summary>
    /// The largest scale, up to 1, that keeps ink from <paramref name="inkTop"/> to <paramref name="inkBottom"/> of a box
    /// <paramref name="height"/> tall inside <paramref name="available"/>, with the box at <paramref name="anchor"/>
    /// (0 the top, 0.5 the middle, 1 the bottom, below 0 stretched, which is the top).
    /// </summary>
    internal static double ScaleForInk(double anchor, double height, double inkTop, double inkBottom, double available)
    {
        if (available <= 0)
            return 1;
        anchor = Math.Max(anchor, 0);
        // The box's top lands at anchor × (available − height × scale), and its ink has to stay between 0 and available.
        var scale = 1.0;
        if (anchor > 0 && anchor * height - inkTop > 0)
            scale = Math.Min(scale, anchor * available / (anchor * height - inkTop));
        if (anchor < 1 && inkBottom - anchor * height > 0)
            scale = Math.Min(scale, (1 - anchor) * available / (inkBottom - anchor * height));
        return scale;
    }
}

/// <summary>
/// Asks for no more width than its <see cref="FrameworkElement.MinWidth"/> and gives its child all it gets: text that
/// ends with … inside a Width or Both <see cref="ShrinkToFit"/> is shortened, rather than everything shrinking for it.
/// </summary>
public sealed class Squeeze : Decorator
{
    private bool _arranging;

    protected override Size MeasureOverride(Size constraint) => new(0, base.MeasureOverride(constraint).Height);

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        // Measured again at the width it gets, so its text ends with … there rather than being clipped.
        if (Child is { } child)
        {
            _arranging = true;
            child.Measure(arrangeSize);
            _arranging = false;
        }
        return base.ArrangeOverride(arrangeSize);
    }

    protected override void OnChildDesiredSizeChanged(UIElement child)
    {
        // That's this measuring it for its width, which doesn't change what this asks for.
        if (!_arranging)
            base.OnChildDesiredSizeChanged(child);
    }
}
