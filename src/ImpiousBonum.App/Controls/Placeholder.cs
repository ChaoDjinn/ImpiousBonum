using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ImpiousBonum.App.Controls;

/// <summary>
/// Faint text in an empty box ("Search metrics…") or an empty list ("No alerts yet"), which WPF doesn't have:
/// <c>controls:Placeholder.Text="…"</c> on a TextBox, or on a ListBox or other ItemsControl.
/// </summary>
public static class Placeholder
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Placeholder), new PropertyMetadata(null, OnTextChanged));

    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

    private static readonly DependencyProperty AdornerProperty = DependencyProperty.RegisterAttached(
        "Adorner", typeof(PlaceholderAdorner), typeof(Placeholder));

    /// <summary>True when the placeholder shows: the box has no text, or the list no items.</summary>
    public static bool IsShowing(FrameworkElement element) => !string.IsNullOrEmpty(GetText(element)) && element switch
    {
        TextBox box => box.Text.Length == 0,
        ItemsControl list => list.Items.Count == 0,
        _ => false,
    };

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;
        if (element.GetValue(AdornerProperty) is PlaceholderAdorner existing)
        {
            existing.InvalidateVisual();
            return;
        }
        if (element.IsLoaded)
            Attach(element);
        else
            element.Loaded += OnLoaded;
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        element.Loaded -= OnLoaded;
        Attach(element);
    }

    private static void Attach(FrameworkElement element)
    {
        if (element.GetValue(AdornerProperty) is not null || AdornerLayer.GetAdornerLayer(element) is not { } layer)
            return;
        var adorner = new PlaceholderAdorner(element);
        element.SetValue(AdornerProperty, adorner);
        layer.Add(adorner);
        switch (element)
        {
            case TextBox box:
                box.TextChanged += (_, _) => adorner.InvalidateVisual();
                break;
            case ItemsControl list:
                ((INotifyCollectionChanged)list.Items).CollectionChanged += (_, _) => adorner.InvalidateVisual();
                break;
        }
        element.SizeChanged += (_, _) => adorner.InvalidateVisual();
        element.IsVisibleChanged += (_, _) => adorner.InvalidateVisual();
    }

    private sealed class PlaceholderAdorner(FrameworkElement element) : Adorner(element)
    {
        protected override void OnRender(DrawingContext drawing)
        {
            if (!IsShowing(element) || !element.IsVisible)
                return;
            var control = element as Control;
            // The theme's faintest text colour, so it reads as a hint in light and dark mode alike.
            var brush = element.TryFindResource("TextFillColorTertiaryBrush") as Brush ?? Brushes.Gray;
            var text = new FormattedText(GetText(element)!, CultureInfo.CurrentUICulture, element.FlowDirection,
                new Typeface(control?.FontFamily ?? SystemFonts.MessageFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                control?.FontSize ?? SystemFonts.MessageFontSize, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                Trimming = TextTrimming.CharacterEllipsis,
            };

            if (element is TextBox box)
            {
                // Where the box draws its own text: inside its content host, after the caret's couple of pixels.
                var host = box.Template?.FindName("PART_ContentHost", box) as FrameworkElement;
                var origin = host?.TranslatePoint(new Point(2, 0), box) ?? new Point(box.Padding.Left + 4, 0);
                text.MaxTextWidth = Math.Max(1, box.ActualWidth - origin.X - box.Padding.Right - 4);
                text.MaxLineCount = 1;
                drawing.DrawText(text, new Point(origin.X, (box.ActualHeight - text.Height) / 2));
                return;
            }

            // An empty list: the message in the middle.
            const double margin = 16;
            text.MaxTextWidth = Math.Max(1, element.ActualWidth - 2 * margin);
            text.TextAlignment = TextAlignment.Center;
            drawing.DrawText(text, new Point(margin, Math.Max(0, (element.ActualHeight - text.Height) / 2)));
        }
    }
}
