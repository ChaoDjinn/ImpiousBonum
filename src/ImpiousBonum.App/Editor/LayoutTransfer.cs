using System.IO;
using System.Windows;
using ImpiousBonum.App.Layout;

namespace ImpiousBonum.App.Editor;

/// <summary>The Export… and Import… dialogs, shared by the editor and the tray.</summary>
public static class LayoutTransfer
{
    private const string Caption = "Impious Bonum";

    /// <summary>Asks where to save, then writes the layout with its font and background image as one <c>.ibl</c> file.</summary>
    public static void Export(Window owner, LayoutDocument layout, string name)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export layout",
            Filter = LayoutPackage.DialogFilter,
            DefaultExt = LayoutPackage.Extension,
            FileName = name + LayoutPackage.Extension,
        };
        if (dialog.ShowDialog(owner) != true)
            return;

        try
        {
            var warnings = LayoutPackage.Write(layout, name, dialog.FileName);
            var message = $"Exported \"{name}\" to {Path.GetFileName(dialog.FileName)}.";
            if (warnings.Count > 0)
                message += Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, warnings);
            MessageBox.Show(owner, message, Caption, MessageBoxButton.OK, warnings.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(owner, $"Couldn't export the layout: {ex.Message}", Caption, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Asks for an <c>.ibl</c> file and adds its layout to the saved layouts, asking before replacing one with the
    /// same name. Returns the saved name, or null if cancelled or the file couldn't be used (the user has been told).
    /// </summary>
    public static string? Import(Window? owner, LayoutStore layouts)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Import layout", Filter = LayoutPackage.DialogFilter };
        if ((owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) != true)
            return null;

        LayoutPackageContents package;
        try
        {
            package = LayoutPackage.Read(dialog.FileName);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Show(owner, $"Couldn't import {Path.GetFileName(dialog.FileName)}: {ex.Message}", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }

        var name = package.Name;
        if (layouts.Library.Find(name) is { } existing)
        {
            var answer = Show(owner,
                $"There's already a layout called \"{existing}\". Replace it?{Environment.NewLine}{Environment.NewLine}" +
                "Yes replaces it. No keeps both and lets you name the imported one.",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel)
                return null;
            if (answer == MessageBoxResult.No)
            {
                var newName = NamePromptWindow.Ask(owner, "Import layout", "Name for the imported layout", SuggestName(layouts.Library, name),
                    n => layouts.Library.CheckNewName(n));
                if (newName is null)
                    return null;
                name = newName;
            }
        }

        try
        {
            name = layouts.Import(package, name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Show(owner, $"Couldn't save the imported layout: {ex.Message}", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }

        if (package.Skipped.Count > 0)
        {
            Show(owner, $"Imported \"{name}\". Some things in the file weren't used:{Environment.NewLine}{Environment.NewLine}" +
                string.Join(Environment.NewLine, package.Skipped.Take(10)), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        return name;
    }

    /// <summary>"Name (2)", "Name (3)"… whichever is free first.</summary>
    public static string SuggestName(LayoutLibrary library, string name)
    {
        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{name} ({i})";
            if (library.CheckNewName(candidate) is null)
                return candidate;
        }
        return name;
    }

    private static MessageBoxResult Show(Window? owner, string message, MessageBoxButton buttons, MessageBoxImage image) =>
        owner is null
            ? MessageBox.Show(message, Caption, buttons, image)
            : MessageBox.Show(owner, message, Caption, buttons, image);
}
