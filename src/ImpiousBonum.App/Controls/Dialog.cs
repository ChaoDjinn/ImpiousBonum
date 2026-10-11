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

    /// <summary>
    /// Closes the window when the button is clicked. WPF's IsCancel only closes windows shown with ShowDialog, so a
    /// Close button on a window shown with Show needs this (or a Click handler).
    /// </summary>
    public static readonly DependencyProperty ClosesWindowProperty = DependencyProperty.RegisterAttached(
        "ClosesWindow", typeof(bool), typeof(Dialog), new PropertyMetadata(false, OnClosesWindowChanged));

    public static bool GetClosesWindow(DependencyObject element) => (bool)element.GetValue(ClosesWindowProperty);

    public static void SetClosesWindow(DependencyObject element, bool value) => element.SetValue(ClosesWindowProperty, value);

    private static void OnClosesWindowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is System.Windows.Controls.Primitives.ButtonBase button && e.NewValue is true)
            button.Click += (_, _) => Window.GetWindow(button)?.Close();
    }
}
