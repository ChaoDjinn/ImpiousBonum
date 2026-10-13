using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ImpiousBonum.App.Widgets;

/// <summary>
/// A list that shows only the rows that fit in it whole, so a bigger font leaves the last ones out rather than cutting
/// one in half. The first row always shows, and is measured to the list's height so a Height fit around it can shrink it.
/// </summary>
public sealed class WholeRows : Panel
{
    /// <summary>Where the first row left out starts; it and the rest are clipped off.</summary>
    private double _bottom;

    /// <summary>How many rows show; for tests.</summary>
    internal int Shown { get; private set; }

    protected override Size MeasureOverride(Size constraint)
    {
        var size = new Size();
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var row = InternalChildren[i];
            // The space under a row doesn't have to fit, only the row.
            var under = (row as FrameworkElement)?.Margin.Bottom ?? 0;
            row.Measure(new Size(constraint.Width, i == 0 ? constraint.Height + under : double.PositiveInfinity));
            size.Width = Math.Max(size.Width, row.DesiredSize.Width);
            size.Height += row.DesiredSize.Height;
        }
        // No taller than its space, so it's arranged in that and knows which rows fit.
        size.Height = Math.Min(size.Height, constraint.Height);
        return size;
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var top = 0.0;
        Shown = 0;
        _bottom = 0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var row = InternalChildren[i];
            row.Arrange(new Rect(0, top, arrangeSize.Width, row.DesiredSize.Height));
            // Arranging drew it, so its ink is known: only that has to fit, not the room a font leaves under its letters.
            if (Shown == i)
            {
                var ink = ShrinkToFit.Ink(row);
                if (i == 0 || ink.IsEmpty || VisualTreeHelper.GetOffset(row).Y + ink.Bottom <= arrangeSize.Height + 0.5)
                    Shown++;
                else
                    _bottom = top;
            }
            top += row.DesiredSize.Height;
        }
        return arrangeSize;
    }

    protected override Geometry? GetLayoutClip(Size layoutSlotSize) =>
        Shown == InternalChildren.Count
            ? base.GetLayoutClip(layoutSlotSize)
            // Generous at the sides and the top, so only the rows left out are cut.
            : new RectangleGeometry(new Rect(-layoutSlotSize.Width, -layoutSlotSize.Height, layoutSlotSize.Width * 3, layoutSlotSize.Height + _bottom));
}
