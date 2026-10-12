using System.Windows;
using System.Windows.Controls;

namespace ImpiousBonum.App.Controls;

/// <summary>
/// A page's content: a column as wide as the page up to its <see cref="FrameworkElement.MaxWidth"/>, kept to the left
/// beside the sidebar as in Windows' Settings, rather than as wide as its widest card or centred.
/// </summary>
public sealed class PageColumn : StackPanel
{
    protected override Size MeasureOverride(Size constraint)
    {
        var size = base.MeasureOverride(constraint);
        return new Size(double.IsInfinity(constraint.Width) ? size.Width : constraint.Width, size.Height);
    }
}
