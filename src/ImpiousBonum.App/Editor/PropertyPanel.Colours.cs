using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Editor;

// Theme colours made from a picture or from Windows' colours.
public sealed partial class PropertyPanel
{
    /// <summary>
    /// <i>Colours from ▾</i>: the theme's colours made from the Windows wallpaper, the layout's background image, a
    /// picture of your choosing or Windows' accent colour (see <see cref="PictureTheme"/>). They become the layout's own
    /// theme values in one undo step, to keep as a theme with Save theme as.
    /// </summary>
    private void AddColoursFrom(Panel group)
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        label.Children.Add(new TextBlock { Text = "Colours from" });
        var chevron = Glyph("");
        chevron.FontSize = 10;
        chevron.Margin = new Thickness(8, 1, 0, 0);
        label.Children.Add(chevron);
        var button = new Button
        {
            Content = label,
            Margin = new Thickness(LabelWidth, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Make this layout's colours from a picture or from Windows' colours. Undo puts them back; Save theme as… keeps them.",
        };
        AutomationProperties.SetName(button, "Colours from");

        var background = new MenuItem { Header = "Background image" };
        ToolTipService.SetShowOnDisabled(background, true);
        background.Click += (_, _) =>
        {
            if (_session.ResolvedTheme.BackgroundImage is { } image)
                ColoursFromPicture(image, "the background image");
        };
        var menu = new ContextMenu();
        menu.Items.Add(ColoursItem("Windows wallpaper", "The picture on your desktop, or its colour", ColoursFromWallpaper));
        menu.Items.Add(background);
        menu.Items.Add(ColoursItem("A picture…", "Choose a picture to take the colours from", ColoursFromChosenPicture));
        menu.Items.Add(ColoursItem("Windows accent colour", "Windows' accent colour, dark or light as your apps are in Windows", ColoursFromAccent));
        menu.Opened += (_, _) =>
        {
            var image = _session.ResolvedTheme.BackgroundImage;
            background.IsEnabled = !string.IsNullOrWhiteSpace(image) && !AnimatedBackground.IsVideo(image);
            background.ToolTip = string.IsNullOrWhiteSpace(image) ? "Choose a background image below first"
                : background.IsEnabled ? "This layout's background picture"
                : "A video's colours can't be read. Choose a picture or GIF.";
        };
        button.ContextMenu = menu;
        button.Click += (_, _) =>
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        };
        group.Children.Add(button);
    }

    private static MenuItem ColoursItem(string header, string toolTip, Action action)
    {
        var item = new MenuItem { Header = header, ToolTip = toolTip };
        item.Click += (_, _) => action();
        return item;
    }

    private void ColoursFromWallpaper()
    {
        var wallpaper = WindowsWallpaper.Current();
        if (wallpaper.Picture is { } picture)
            ColoursFromPicture(picture, "your wallpaper");
        else
            _session.SetThemeValues(PictureTheme.FromPicture([new Swatch(wallpaper.Colour, 1)]));
    }

    private void ColoursFromChosenPicture()
    {
        var extensions = string.Join(";", BackgroundImage.Extensions.Select(e => "*" + e));
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = $"Pictures ({extensions})|{extensions}|All files|*.*", Title = "Choose a picture to take the colours from" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            ColoursFromPicture(dialog.FileName, "that picture");
    }

    private void ColoursFromAccent() =>
        _session.SetThemeValues(PictureTheme.FromColour(SystemColors.AccentColor, light: !WindowsColourMode.IsDark()));

    /// <param name="what">The picture, as the message saying it can't be read calls it.</param>
    private void ColoursFromPicture(string path, string what)
    {
        if (Palette.FromFile(path) is { Count: > 0 } swatches)
        {
            _session.SetThemeValues(PictureTheme.FromPicture(swatches));
            return;
        }
        var problem = File.Exists(path) ? $"Couldn't read {what}. Try a PNG, JPG, BMP, GIF or TIFF picture." : $"Couldn't find {what}: {path}";
        MessageBox.Show(Window.GetWindow(this)!, problem, "Colours from a picture", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
