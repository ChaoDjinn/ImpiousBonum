using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Settings;
using ImpiousBonum.App.Shell;
using ImpiousBonum.Core.Providers;

namespace ImpiousBonum.App.Tests;

/// <summary>Backing up the whole setup to one file and restoring it, and the General page's buttons for both.</summary>
[Collection("AppPaths")]
public sealed class SettingsBackupTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 14, 5, 0, TimeSpan.FromHours(1));
    private static readonly byte[] FontBytes = Encoding.ASCII.GetBytes("not really a font");
    private static readonly byte[] ImageBytes = Encoding.ASCII.GetBytes("not really a picture");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ImpiousBonum.Tests", Guid.NewGuid().ToString("N"));

    public SettingsBackupTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    /// <summary>
    /// A data folder, like %AppData%\ImpiousBonum on one PC, with a bit of everything: settings with private things in
    /// them, layouts (one imported, with its font and picture), a saved theme, and what a backup leaves out.
    /// </summary>
    private string MyPc()
    {
        var data = Path.Combine(_root, "my pc", "ImpiousBonum");
        AppPaths.UseDataDirectory(data);
        new AppSettings
        {
            PingHost = "8.8.8.8",
            TabletToken = "secret",
            ActiveLayout = "Desk",
            Calendars = [new CalendarLink("Work", "https://example.com/private/work.ics")],
        }.Save();

        var library = new LayoutLibrary(data);
        library.EnsureSeeded();
        library.Save("Desk", new LayoutDocument { Width = 1920, Height = 480 });
        library.Import(LayoutPackage.Read(Package(), "Shared"), "Shared");
        new ThemeLibrary(data).Save("Neon", new JsonObject { ["accent"] = "#123456" });

        File.WriteAllText(Path.Combine(data, "dashboard.log"), "log");
        File.WriteAllText(Path.Combine(data, "sessions.csv"), "my sessions");
        File.WriteAllText(Path.Combine(data, "claude-usage.json"), "{}");
        File.WriteAllText(Path.Combine(data, AppSettings.UnreadableCopyName), "{ oops");
        Directory.CreateDirectory(Path.Combine(data, "logs"));
        File.WriteAllText(Path.Combine(data, "logs", "2026-10-16.csv"), "time,cpu");
        File.WriteAllText(Path.Combine(library.Directory, "notes.txt"), "mine");
        return data;
    }

    /// <summary>Another PC's data folder, set up differently.</summary>
    private string OtherPc()
    {
        var data = Path.Combine(_root, "other pc", "ImpiousBonum");
        AppPaths.UseDataDirectory(data);
        new AppSettings { PingHost = "9.9.9.9" }.Save();
        var library = new LayoutLibrary(data);
        library.EnsureSeeded();
        library.Save("Kitchen", new LayoutDocument { Width = 800 });
        new ThemeLibrary(data).Save("Old", new JsonObject { ["accent"] = "#654321" });
        File.WriteAllText(Path.Combine(data, "sessions.csv"), "their sessions");
        return data;
    }

    /// <summary>A layout package with a font and a background picture, as from the gallery.</summary>
    private MemoryStream Package()
    {
        var files = Directory.CreateDirectory(Path.Combine(_root, "files")).FullName;
        var font = Path.Combine(files, "Fancy.ttf");
        var image = Path.Combine(files, "wallpaper.jpg");
        File.WriteAllBytes(font, FontBytes);
        File.WriteAllBytes(image, ImageBytes);
        var stream = new MemoryStream();
        LayoutPackage.Write(new LayoutDocument { Theme = new JsonObject { ["fontFile"] = font, ["backgroundImage"] = image } }, "Shared", stream);
        stream.Position = 0;
        return stream;
    }

    private string BackUp(string data, string name = "Backup")
    {
        var path = Path.Combine(_root, name + SettingsBackup.Extension);
        SettingsBackup.Write(data, path, Now);
        return path;
    }

    private static List<string> EntryNames(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return [.. zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal)];
    }

    /// <summary>A hand-made backup, for the ones the app would never write.</summary>
    private string Zip(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + SettingsBackup.Extension);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write(content);
        }
        return path;
    }

    private static string Manifest(string app = "0.15.0", int version = 1) =>
        $$"""{ "format": "impious-bonum-backup", "version": {{version}}, "app": "{{app}}", "created": "2026-10-16T14:05:00+01:00" }""";

    private const string PlainSettings = """{ "pingHost": "1.2.3.4" }""";

    /// <summary>Every file in a folder, relative to it, so a test can see nothing changed.</summary>
    private static Dictionary<string, string> Snapshot(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(folder, f), File.ReadAllText, StringComparer.OrdinalIgnoreCase);

    private static ThemeSettings Resolved(LayoutDocument layout) => new ThemeLibrary(null).Resolve(layout.Theme);

    // ---- Backing up ---------------------------------------------------------------------------------

    [Fact]
    public void A_backup_holds_the_settings_layouts_and_themes_and_leaves_out_logs()
    {
        var data = MyPc();
        var path = Path.Combine(_root, "Backup.ibbackup");

        var contents = SettingsBackup.Write(data, path, Now);

        Assert.Equal(
            [
                "layouts/Default.json", "layouts/Desk.json", "layouts/Shared.json",
                "layouts/Shared/background.jpg", "layouts/Shared/font.ttf", "layouts/Shared/imported.txt",
                "manifest.json", "settings.json", "themes/Neon.json",
            ],
            EntryNames(path));
        Assert.Equal("your settings, 3 layouts and 1 saved theme", contents.Describe());
        Assert.Empty(contents.Skipped);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Inspecting_a_backup_says_when_and_by_which_version_and_what_it_holds()
    {
        var path = BackUp(MyPc());

        var contents = SettingsBackup.Inspect(path);

        Assert.Equal(Now, contents.Created);
        Assert.Equal(SettingsBackup.CurrentVersion.ToString(3), contents.AppVersion);
        Assert.True(contents.HasSettings);
        Assert.Equal(["Default", "Desk", "Shared"], contents.Layouts);
        Assert.Equal(["Neon"], contents.Themes);
        Assert.Empty(contents.Skipped);
        Assert.False(contents.IsNewerThan(SettingsBackup.CurrentVersion));
    }

    [Fact]
    public void Files_too_big_for_a_backup_are_left_out_and_named()
    {
        var data = MyPc();
        var video = Path.Combine(data, "layouts", "Shared", "huge.mp4");
        using (var file = File.Create(video))
            file.SetLength(SettingsBackup.MaxFileBytes + 1);

        var contents = SettingsBackup.Write(data, Path.Combine(_root, "Backup.ibbackup"), Now);

        Assert.Contains(contents.Skipped, s => s.StartsWith("layouts/Shared/huge.mp4: bigger than 100 MB", StringComparison.Ordinal));
        Assert.DoesNotContain("layouts/Shared/huge.mp4", EntryNames(Path.Combine(_root, "Backup.ibbackup")));
    }

    // ---- Restoring ----------------------------------------------------------------------------------

    [Fact]
    public void Restoring_on_another_pc_brings_back_the_whole_setup()
    {
        var backup = BackUp(MyPc());
        var other = OtherPc();

        SettingsBackup.Restore(backup, other, Now);

        AppPaths.UseDataDirectory(other);
        var settings = AppSettings.Load();
        Assert.Null(settings.LoadProblem);
        Assert.Equal("8.8.8.8", settings.PingHost);
        Assert.Equal("secret", settings.TabletToken);
        Assert.Equal("Desk", settings.ActiveLayout);
        Assert.Equal("https://example.com/private/work.ics", Assert.Single(settings.Calendars).Url);

        // Exactly the backup's layouts and themes: the other PC's own are replaced, not added to.
        var library = new LayoutLibrary(other);
        Assert.Equal(["Default", "Desk", "Shared"], library.List());
        Assert.Equal(1920, library.Load("Desk").Width);
        Assert.Equal(["Neon"], new ThemeLibrary(other).ListSaved());

        // The imported layout's font and picture came too, and the layout points at them in this data folder, not the old one.
        var shared = Resolved(library.Load("Shared"));
        Assert.Equal(Path.Combine(other, "layouts", "Shared", "font.ttf"), shared.FontFile);
        Assert.Equal(Path.Combine(other, "layouts", "Shared", "background.jpg"), shared.BackgroundImage);
        Assert.Equal(FontBytes, File.ReadAllBytes(shared.FontFile!));
        Assert.Equal(ImageBytes, File.ReadAllBytes(shared.BackgroundImage!));
        // So the folder isn't taken for one nothing uses and tidied away.
        library.DeleteUnusedImportFolders();
        Assert.True(File.Exists(shared.FontFile));

        // What a backup doesn't hold is left as it was, and nothing is left over from the restore.
        Assert.Equal("their sessions", File.ReadAllText(Path.Combine(other, "sessions.csv")));
        Assert.False(File.Exists(Path.Combine(other, "dashboard.log")));
        Assert.False(Directory.Exists(Path.Combine(other, "logs")));
        Assert.False(Directory.Exists(Path.Combine(other, "restoring.tmp")));
    }

    [Fact]
    public void Restoring_on_the_same_pc_leaves_its_own_files_alone()
    {
        var data = MyPc();
        var backup = BackUp(data);
        new LayoutLibrary(data).Save("Made since", new LayoutDocument());

        SettingsBackup.Restore(backup, data, Now);

        Assert.Equal(["Default", "Desk", "Shared"], new LayoutLibrary(data).List());
        Assert.Equal("mine", File.ReadAllText(Path.Combine(data, "layouts", "notes.txt")));
        Assert.Equal("my sessions", File.ReadAllText(Path.Combine(data, "sessions.csv")));
        Assert.Equal("time,cpu", File.ReadAllText(Path.Combine(data, "logs", "2026-10-16.csv")));
        Assert.Equal(FontBytes, File.ReadAllBytes(Path.Combine(data, "layouts", "Shared", "font.ttf")));
    }

    [Fact]
    public void Restoring_first_saves_the_setup_it_replaces_beside_the_data_folder()
    {
        var backup = BackUp(MyPc());
        var other = OtherPc();

        var before = SettingsBackup.Restore(backup, other, Now);

        Assert.Equal(Path.Combine(_root, "other pc", "ImpiousBonum backups", "Before restoring 2026-10-16 14.05.ibbackup"), before);
        Assert.Equal(SettingsBackup.SafetyBackupFolder(other), Path.GetDirectoryName(before));
        var saved = SettingsBackup.Inspect(before);
        Assert.Equal(["Default", "Kitchen"], saved.Layouts);
        Assert.Equal(["Old"], saved.Themes);

        // Restoring that goes back to how it was; the setup it replaces is saved again, beside the first.
        var again = SettingsBackup.Restore(before, other, Now);
        Assert.EndsWith("Before restoring 2026-10-16 14.05 (2).ibbackup", again);
        Assert.Equal(["Default", "Kitchen"], new LayoutLibrary(other).List());
        Assert.Equal(["Old"], new ThemeLibrary(other).ListSaved());
        AppPaths.UseDataDirectory(other);
        Assert.Equal("9.9.9.9", AppSettings.Load().PingHost);
        Assert.Equal(["Default", "Desk", "Shared"], SettingsBackup.Inspect(again).Layouts);
    }

    [Fact]
    public void Unreadable_settings_in_a_backup_stop_the_restore_before_anything_changes()
    {
        var other = OtherPc();
        var before = Snapshot(other);
        var backup = Zip(("manifest.json", Manifest()), ("settings.json", "{ \"pingHost\": oops }"), ("layouts/New.json", "{}"));

        var ex = Assert.Throws<InvalidDataException>(() => SettingsBackup.Restore(backup, other, Now));

        Assert.Contains("settings in the backup can't be read", ex.Message);
        Assert.Equal(before, Snapshot(other));
        Assert.False(Directory.Exists(SettingsBackup.SafetyBackupFolder(other)));
    }

    // ---- Files that aren't what they seem -----------------------------------------------------------

    [Theory]
    [InlineData("../settings.json")]
    [InlineData("layouts/../../evil.json")]
    [InlineData("layouts\\..\\..\\evil.json")]
    [InlineData("/settings.json")]
    [InlineData("\\Windows\\evil.json")]
    [InlineData("C:/Windows/evil.json")]
    [InlineData("C:settings.json")]
    public void Unsafe_entry_names_reject_the_backup_and_change_nothing(string entryName)
    {
        var other = OtherPc();
        var before = Snapshot(other);
        var backup = Zip(("manifest.json", Manifest()), ("settings.json", PlainSettings), (entryName, "{}"));

        Assert.Contains("unsafe path", Assert.Throws<InvalidDataException>(() => SettingsBackup.Inspect(backup)).Message);
        Assert.Contains("unsafe path", Assert.Throws<InvalidDataException>(() => SettingsBackup.Restore(backup, other, Now)).Message);

        Assert.Equal(before, Snapshot(other));
        Assert.False(Directory.Exists(SettingsBackup.SafetyBackupFolder(other)));
    }

    [Fact]
    public void Only_what_a_backup_can_hold_is_restored_and_the_rest_is_named()
    {
        var other = OtherPc();
        var backup = Zip(
            ("manifest.json", Manifest()),
            ("settings.json", PlainSettings),
            ("layouts/Desk.json", "{ \"width\": 640 }"),
            ("layouts/Desk/background.png", "png"),
            ("layouts/Desk/evil.exe", "MZ"),
            ("layouts/Desk/deeper/font.ttf", "ttf"),
            ("layouts/CON/font.ttf", "ttf"),
            ("themes/notes.txt", "hi"),
            ("dashboard.log", "someone else's log"),
            ("startup.bat", "echo"));

        var contents = SettingsBackup.Inspect(backup);
        SettingsBackup.Restore(backup, other, Now);

        Assert.Equal(["Desk"], contents.Layouts);
        Assert.Equal(
            ["layouts/Desk/evil.exe", "layouts/Desk/deeper/font.ttf", "layouts/CON/font.ttf", "themes/notes.txt", "dashboard.log", "startup.bat"],
            contents.Skipped.Select(s => s.Split(':')[0]));
        Assert.Equal(640, new LayoutLibrary(other).Load("Desk").Width);
        Assert.Equal(["background.png"], Directory.GetFiles(Path.Combine(other, "layouts", "Desk")).Select(Path.GetFileName));
        Assert.False(Directory.Exists(Path.Combine(other, "layouts", "CON")));
        Assert.False(File.Exists(Path.Combine(other, "startup.bat")));
        Assert.False(File.Exists(Path.Combine(other, "dashboard.log")));
        Assert.False(File.Exists(Path.Combine(other, "themes", "notes.txt")));
    }

    [Fact]
    public void Oversized_entries_reject_the_backup_and_change_nothing()
    {
        var other = OtherPc();
        var before = Snapshot(other);
        // Each compresses to almost nothing, as a zip bomb would.
        var hugeSettings = Zip(("manifest.json", Manifest()), ("settings.json", "{}" + new string(' ', (int)SettingsBackup.MaxJsonBytes)));
        var hugeVideo = Path.Combine(_root, "video" + SettingsBackup.Extension);
        using (var zip = ZipFile.Open(hugeVideo, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
                writer.Write(Manifest());
            using var video = zip.CreateEntry("layouts/Desk/movie.mp4").Open();
            var zeros = new byte[1024 * 1024];
            for (var written = 0L; written <= SettingsBackup.MaxFileBytes; written += zeros.Length)
                video.Write(zeros);
        }

        Assert.Contains("settings.json is bigger than 4 MB", Assert.Throws<InvalidDataException>(() => SettingsBackup.Restore(hugeSettings, other, Now)).Message);
        Assert.Contains("movie.mp4 is bigger than 100 MB", Assert.Throws<InvalidDataException>(() => SettingsBackup.Restore(hugeVideo, other, Now)).Message);

        Assert.Equal(before, Snapshot(other));
        Assert.False(Directory.Exists(Path.Combine(other, "restoring.tmp")));
    }

    [Fact]
    public void A_file_with_too_many_entries_is_rejected()
    {
        var entries = Enumerable.Range(0, SettingsBackup.MaxEntries).Select(i => ($"themes/Theme {i}.json", "{}")).Prepend(("manifest.json", Manifest()));

        var ex = Assert.Throws<InvalidDataException>(() => SettingsBackup.Inspect(Zip([.. entries])));

        Assert.Contains("too many entries", ex.Message);
    }

    [Fact]
    public void Files_that_are_not_backups_are_rejected()
    {
        var notZip = Path.Combine(_root, "settings" + SettingsBackup.Extension);
        File.WriteAllText(notZip, PlainSettings);
        var layoutPackage = Path.Combine(_root, "Shared.ibl");
        File.WriteAllBytes(layoutPackage, Package().ToArray());
        var otherFormat = Zip(("manifest.json", "{ \"format\": \"impious-bonum-layout\", \"version\": 1 }"), ("settings.json", PlainSettings));
        var brokenManifest = Zip(("manifest.json", "{ oops"), ("settings.json", PlainSettings));

        foreach (var path in new[] { notZip, layoutPackage, otherFormat, brokenManifest })
            Assert.Equal("This isn't an Impious Bonum backup.", Assert.Throws<InvalidDataException>(() => SettingsBackup.Inspect(path)).Message);
    }

    [Fact]
    public void A_backup_from_a_newer_version_says_so()
    {
        Assert.True(SettingsBackup.Inspect(Zip(("manifest.json", Manifest(app: "99.0.0")))).IsNewerThan(SettingsBackup.CurrentVersion));
        Assert.True(SettingsBackup.Inspect(Zip(("manifest.json", Manifest(app: "99.1.0-beta")))).IsNewerThan(SettingsBackup.CurrentVersion));
        Assert.False(SettingsBackup.Inspect(Zip(("manifest.json", Manifest(app: "0.1.0")))).IsNewerThan(SettingsBackup.CurrentVersion));
        Assert.False(SettingsBackup.Inspect(Zip(("manifest.json", Manifest(app: "not a version")))).IsNewerThan(SettingsBackup.CurrentVersion));

        // One that saves backups differently can't be read at all.
        var ex = Assert.Throws<InvalidDataException>(() => SettingsBackup.Inspect(Zip(("manifest.json", Manifest(version: 2)), ("settings.json", PlainSettings))));
        Assert.Contains("newer version", ex.Message);
    }

    // ---- The General page ---------------------------------------------------------------------------

    [Fact]
    public void The_general_page_backs_up_and_only_restores_after_asking()
    {
        MyPc();
        var newer = Zip(("manifest.json", Manifest(app: "99.0.0")), ("settings.json", PlainSettings));
        Sta.Run(() =>
        {
            var host = DispatchProxy.Create<ISettingsHost, BackupHost>();
            var restored = ((BackupHost)(object)host).Restored;
            var page = new GeneralPage(host);
            page.Load();
            // The page says a backup has private things in it before one is made.
            Assert.Contains(BackupDialogs.PrivateNote, Logical<TextBlock>(page).Select(t => t.Text));

            var file = Path.Combine(_root, "Backup.ibbackup");
            var messages = new List<string>();
            var answers = new Queue<MessageBoxResult>();
            page.Backups.AskFile = (_, _) => file;
            page.Backups.Show = (_, text, _, _) =>
            {
                messages.Add(text);
                return answers.TryDequeue(out var answer) ? answer : MessageBoxResult.OK;
            };

            Click(Button(page, "Back up…"));
            Assert.True(File.Exists(file));
            Assert.StartsWith("Saved your settings, 3 layouts and 1 saved theme to Backup.ibbackup.", messages[^1]);
            Assert.Contains("calendar links", messages[^1]);

            // Says what's in it and asks; No leaves everything as it is.
            answers.Enqueue(MessageBoxResult.No);
            Click(Button(page, "Restore…"));
            Assert.Contains("your settings, 3 layouts and 1 saved theme", messages[^1]);
            Assert.Contains(SettingsBackup.SafetyBackupFolder(AppPaths.DataDirectory), messages[^1]);
            Assert.Empty(restored);

            answers.Enqueue(MessageBoxResult.Yes);
            Click(Button(page, "Restore…"));
            Assert.Equal([file], restored);

            // A newer version's backup warns before anything else, and No there goes no further.
            page.Backups.AskFile = (_, _) => newer;
            var asked = messages.Count;
            answers.Enqueue(MessageBoxResult.No);
            Click(Button(page, "Restore…"));
            Assert.Contains("newer than this one", Assert.Single(messages.Skip(asked)));
            Assert.Equal([file], restored);

            answers.Enqueue(MessageBoxResult.Yes);
            answers.Enqueue(MessageBoxResult.Yes);
            Click(Button(page, "Restore…"));
            Assert.Equal([file, newer], restored);

            // A file that isn't a backup is turned away with why.
            page.Backups.AskFile = (_, _) => Path.Combine(_root, "missing.ibbackup");
            Click(Button(page, "Restore…"));
            Assert.StartsWith("Couldn't restore missing.ibbackup", messages[^1]);
            Assert.Equal(2, restored.Count);
        });
    }

    /// <summary>A settings host that notes the backups it's asked to restore.</summary>
    public class BackupHost : DispatchProxy
    {
        public AppSettings Settings { get; } = new();

        public List<string> Restored { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "get_Settings":
                    return Settings;
                case nameof(ISettingsHost.RestoreBackupAsync):
                    Restored.Add((string)args![0]!);
                    return Task.CompletedTask;
                default:
                    return targetMethod?.ReturnType is { IsValueType: true } type && type != typeof(void) ? Activator.CreateInstance(type) : null;
            }
        }
    }

    private static Button Button(DependencyObject page, string name) => Logical<Button>(page).First(b => AutomationProperties.GetName(b) == name);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static IEnumerable<T> Logical<T>(DependencyObject? root) where T : DependencyObject =>
        root is null ? [] : LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().SelectMany(child => child is T match ? [match, .. Logical<T>(child)] : Logical<T>(child));
}
