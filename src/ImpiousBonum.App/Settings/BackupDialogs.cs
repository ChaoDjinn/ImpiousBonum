using System.IO;
using System.Windows;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Settings;

/// <summary>The General page's Back up… and Restore…: the file dialogs and questions around <see cref="SettingsBackup"/>.</summary>
internal sealed class BackupDialogs(ISettingsHost host)
{
    private const string Caption = "Impious Bonum";

    /// <summary>What a backup has in it that's worth keeping to yourself, said on the page and after saving one.</summary>
    public const string PrivateNote =
        "A backup has private things in it: your calendar links, the tablet link, and the web addresses and commands you've set up, for alerts and custom data among others. Keep it somewhere only you can open.";

    /// <summary>Asks where to save a backup (true) or which one to restore (false); null if cancelled. Replaced in tests.</summary>
    internal Func<Window?, bool, string?> AskFile { get; set; } = AskFileWithDialog;

    /// <summary>Shows a message or asks a question. Replaced in tests.</summary>
    internal Func<Window?, string, MessageBoxButton, MessageBoxImage, MessageBoxResult> Show { get; set; } = ShowMessageBox;

    /// <summary>Asks where to save, then writes the settings, layouts and saved themes there as one file.</summary>
    public void BackUp(Window? owner)
    {
        if (AskFile(owner, true) is not { } path)
            return;

        try
        {
            var contents = SettingsBackup.Write(AppPaths.DataDirectory, path, DateTimeOffset.Now);
            var message = $"Saved {contents.Describe()} to {Path.GetFileName(path)}.\n\n{PrivateNote}";
            if (contents.Skipped.Count > 0)
                message += "\n\nLeft out:\n" + string.Join("\n", contents.Skipped.Take(10));
            Show(owner, message, MessageBoxButton.OK, contents.Skipped.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Show(owner, $"Couldn't save the backup: {ex.Message}", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Asks for a backup, says what's in it (warning first if a newer version made it) and asks before replacing
    /// anything; then the app saves the setup it has, restores the backup and starts again.
    /// </summary>
    public async Task RestoreAsync(Window? owner)
    {
        if (AskFile(owner, false) is not { } path)
            return;

        BackupContents contents;
        try
        {
            contents = SettingsBackup.Inspect(path);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Show(owner, $"Couldn't restore {Path.GetFileName(path)}: {ex.Message}", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var current = SettingsBackup.CurrentVersion.ToString(3);
        if (contents.IsNewerThan(SettingsBackup.CurrentVersion)
            && Show(owner,
                $"This backup was made by Impious Bonum {contents.AppVersion}, which is newer than this one ({current}). "
                + "Settings this version doesn't know about would be lost, and some layouts may not look the same.\n\n"
                + "To keep everything, update Impious Bonum first (About and updates), then restore it.\n\nRestore it anyway?",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (Show(owner, Question(contents), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        await host.RestoreBackupAsync(path);
    }

    private static string Question(BackupContents contents)
    {
        var made = contents.Created is { } created ? $" made on {created.LocalDateTime:d MMMM yyyy} at {created.LocalDateTime:t}" : "";
        var by = contents.AppVersion is { } version ? $" by Impious Bonum {version}" : "";
        var question = $"Restore the backup{made}{by}?\n\n"
            + $"It has {contents.Describe()}, which replace the ones you have now. Impious Bonum closes and starts again to use them.\n\n"
            + $"Your setup now is saved first, in {SettingsBackup.SafetyBackupFolder(AppPaths.DataDirectory)}, so you can go back to it.\n\n"
            + "Only restore your own backups: a backup sets the programs and web addresses that custom data, alerts and buttons use.";
        if (contents.Skipped.Count > 0)
            question += "\n\nLeft out, as they aren't part of a backup:\n" + string.Join("\n", contents.Skipped.Take(5).Select(s => s.Split(':')[0]));
        return question;
    }

    private static string? AskFileWithDialog(Window? owner, bool save)
    {
        Microsoft.Win32.FileDialog dialog = save
            ? new Microsoft.Win32.SaveFileDialog
            {
                Title = "Back up settings",
                Filter = SettingsBackup.DialogFilter,
                DefaultExt = SettingsBackup.Extension,
                FileName = SettingsBackup.SuggestFileName(DateTimeOffset.Now),
            }
            : new Microsoft.Win32.OpenFileDialog { Title = "Restore settings", Filter = SettingsBackup.DialogFilter };
        return (owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) == true ? dialog.FileName : null;
    }

    /// <summary>A message box; a question's default answer is No, so Enter never replaces anything.</summary>
    private static MessageBoxResult ShowMessageBox(Window? owner, string message, MessageBoxButton buttons, MessageBoxImage image)
    {
        var answer = buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.None;
        return owner is null
            ? MessageBox.Show(message, Caption, buttons, image, answer)
            : MessageBox.Show(owner, message, Caption, buttons, image, answer);
    }
}
