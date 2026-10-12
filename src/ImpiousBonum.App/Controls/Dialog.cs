using System.Windows;
using System.Windows.Input;

namespace ImpiousBonum.App.Controls;

/// <summary>Behaviour shared by the app's dialogs.</summary>
public static class Dialog
{
    /// <summary>
    /// Keeps an element (a dialog's "what's missing" message) hidden until the user first presses a key or clicks in
    /// the window, so a dialog doesn't open with a complaint about fields nobody has had a chance to fill in yet.
    /// Hidden, not collapsed, so the dialog doesn't change size when it appears.
    /// </summary>
    public static readonly DependencyProperty ShowAfterInputProperty = DependencyProperty.RegisterAttached(
        "ShowAfterInput", typeof(bool), typeof(Dialog), new PropertyMetadata(false, OnShowAfterInputChanged));

    public static bool GetShowAfterInput(DependencyObject element) => (bool)element.GetValue(ShowAfterInputProperty);

    public static void SetShowAfterInput(DependencyObject element, bool value) => element.SetValue(ShowAfterInputProperty, value);

    private static void OnShowAfterInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true)
            return;
        element.Visibility = Visibility.Hidden;
        element.Loaded += (_, _) =>
        {
            if (Window.GetWindow(element) is not { } window)
            {
                element.Visibility = Visibility.Visible;
                return;
            }
            void Reveal(object sender, InputEventArgs args)
            {
                element.Visibility = Visibility.Visible;
                window.PreviewKeyDown -= Reveal;
                window.PreviewMouseDown -= Reveal;
            }
            window.PreviewKeyDown += Reveal;
            window.PreviewMouseDown += Reveal;
        };
    }
}
