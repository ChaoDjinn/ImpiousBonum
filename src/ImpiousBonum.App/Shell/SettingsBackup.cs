using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImpiousBonum.App.Layout;

namespace ImpiousBonum.App.Shell;

/// <summary>What a backup holds, as <see cref="SettingsBackup.Inspect"/> found it or <see cref="SettingsBackup.Write(string, string, DateTimeOffset)"/> wrote it.</summary>
public sealed class BackupContents
{
    /// <summary>When it was made, if the file says.</summary>
    public DateTimeOffset? Created { get; init; }

    /// <summary>The version of Impious Bonum that made it, such as 0.15.0, if the file says.</summary>
    public string? AppVersion { get; init; }

    public bool HasSettings { get; init; }

    /// <summary>The saved layouts' names, sorted.</summary>
    public IReadOnlyList<string> Layouts { get; init; } = [];

    /// <summary>The saved themes' names, sorted.</summary>
    public IReadOnlyList<string> Themes { get; init; } = [];

    /// <summary>Files left out, and why: too big or in use when backing up, or not something a backup holds when reading one.</summary>
    public IReadOnlyList<string> Skipped { get; init; } = [];

    /// <summary>The files written into a backup, so a restore only deletes what it has saved first.</summary>
    internal IReadOnlyList<string> Files { get; init; } = [];

    /// <summary>Made by a later version than <paramref name="current"/>, which may have settings this one doesn't know.</summary>
    public bool IsNewerThan(Version current) => SettingsBackup.ParseVersion(AppVersion) is { } made && made > current;

    /// <summary>"your settings, 4 layouts and 2 saved themes", for messages.</summary>
    public string Describe()
    {
        List<string> parts = HasSettings ? ["your settings"] : [];
        parts.Add(Count(Layouts.Count, "layout"));
        if (Themes.Count > 0)
            parts.Add(Count(Themes.Count, "saved theme"));
        return parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts.Take(parts.Count - 1))} and {parts[^1]}";
    }

    private static string Count(int count, string noun) => count switch
    {
        0 => $"no {noun}s",
        1 => $"1 {noun}",
        _ => $"{count} {noun}s",
    };
}

/// <summary>
/// Your whole setup in one <c>.ibbackup</c> file, to keep safe or to move to another PC: a zip of <c>settings.json</c>,
/// the saved layouts with the folders of fonts and pictures beside them in <c>layouts</c>, the saved themes, and a small
/// <c>manifest.json</c> saying when and by which version it was made. Left out: <c>dashboard.log</c>, the <c>logs</c>
/// folder and <c>sessions.csv</c>, which record what happened rather than set anything up (and restoring them would
/// lose what's been recorded since); <c>claude-usage.json</c>, which Claude Code keeps rewriting; and the copy kept of
/// a settings file that couldn't be read.
/// <para>
/// A backup may not be yours, so restoring one trusts nothing in it, as with layout packages: entry names with
/// <c>..</c> or absolute paths reject it, sizes are capped by counting the bytes actually unpacked, and only what a
/// backup can hold is written, into a folder of its own, before anything you have is touched. Then the setup from
/// before is saved beside the data folder, and only then swapped for the backup's.
/// </para>
/// </summary>
public static class SettingsBackup
{
    public const string Extension = ".ibbackup";

    public const string DialogFilter = "Impious Bonum backup (*.ibbackup)|*.ibbackup";

    /// <summary>The biggest font, picture or video a backup holds.</summary>
    public const long MaxFileBytes = 100L * 1024 * 1024;

    /// <summary>The biggest settings, layout or theme file: far bigger than any real one.</summary>
    public const long MaxJsonBytes = 4L * 1024 * 1024;

    public const long MaxTotalBytes = 1024L * 1024 * 1024;

    public const int MaxEntries = 5000;

    private const long MaxManifestBytes = 64 * 1024;
    private const int MaxNameLength = 120;
    private const string ManifestEntry = "manifest.json";
    private const string SettingsEntry = "settings.json";
    private const string LayoutsFolder = "layouts";
    private const string ThemesFolder = "themes";
    private const string FormatName = "impious-bonum-backup";
    private const int FormatVersion = 1;
    private const string NotABackup = "This isn't an Impious Bonum backup.";

    /// <summary>Where a backup is unpacked before anything is replaced: in the data folder, so its files only move from there.</summary>
    private const string StagingFolder = "restoring.tmp";

    private static readonly IReadOnlyList<string> LayoutFileExtensions = [.. LayoutPackage.FontExtensions, .. BackgroundImage.BackgroundExtensions];

    /// <summary>This version, as backups record it.</summary>
    public static Version CurrentVersion { get; } = Normalise(typeof(SettingsBackup).Assembly.GetName().Version) ?? new Version(0, 0, 0);

    private enum Kind { Other, Manifest, Settings, Layout, Theme, LayoutFile }

    private sealed record Checked(IReadOnlyList<(string Name, Kind Kind, ZipArchiveEntry Entry)> Entries, string? DataFolder, BackupContents Contents);

    /// <summary>A name for a new backup, such as "Impious Bonum 2026-10-16.ibbackup".</summary>
    public static string SuggestFileName(DateTimeOffset now) => $"Impious Bonum {now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}{Extension}";

    /// <summary>Writes a backup to a file, replacing any existing one only once the new one is complete.</summary>
    public static BackupContents Write(string dataDirectory, string path, DateTimeOffset now)
    {
        var temp = path + ".tmp";
        try
        {
            BackupContents contents;
            using (var file = File.Create(temp))
                contents = Write(dataDirectory, file, now);
            File.Move(temp, path, overwrite: true);
            return contents;
        }
        finally
        {
            File.Delete(temp);
        }
    }

    /// <summary>
    /// Writes a backup of the setup in <paramref name="dataDirectory"/>. A file too big for a backup, or that can't be
    /// read, is left out and named in <see cref="BackupContents.Skipped"/>.
    /// </summary>
    public static BackupContents Write(string dataDirectory, Stream destination, DateTimeOffset now)
    {
        var files = new List<string>();
        var layouts = new List<string>();
        var themes = new List<string>();
        var skipped = new List<string>();
        var hasSettings = false;
        long total = 0;

        using var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        WriteText(zip, ManifestEntry, new JsonObject
        {
            ["format"] = FormatName,
            ["version"] = FormatVersion,
            ["app"] = CurrentVersion.ToString(3),
            ["created"] = now,
            // So paths into this folder (an imported layout's font, say) can be pointed at the one it's restored to.
            ["dataFolder"] = Path.GetFullPath(dataDirectory),
        }.ToJsonString(JsonDefaults.Options));

        foreach (var (entry, kind, path) in SetupFiles(dataDirectory))
        {
            var limit = LimitFor(kind);
            try
            {
                var length = new FileInfo(path).Length;
                if (length > limit)
                {
                    skipped.Add($"{entry}: bigger than {Size(limit)}");
                    continue;
                }
                if (total + length > MaxTotalBytes)
                {
                    skipped.Add($"{entry}: the backup would be bigger than {Size(MaxTotalBytes)}");
                    continue;
                }
                // Pictures and videos are compressed already; squeezing them harder only takes longer.
                zip.CreateEntryFromFile(path, entry, kind == Kind.LayoutFile ? CompressionLevel.Fastest : CompressionLevel.Optimal);
                total += length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add($"{entry}: {ex.Message}");
                continue;
            }

            files.Add(path);
            if (kind == Kind.Settings)
                hasSettings = true;
            else if (kind == Kind.Layout)
                layouts.Add(NameOf(entry));
            else if (kind == Kind.Theme)
                themes.Add(NameOf(entry));
        }

        return new BackupContents
        {
            Created = now,
            AppVersion = CurrentVersion.ToString(3),
            HasSettings = hasSettings,
            Layouts = layouts,
            Themes = themes,
            Skipped = skipped,
            Files = files,
        };
    }

    /// <summary>
    /// Reads what a backup holds, checking it as <see cref="Restore"/> will. Throws <see cref="InvalidDataException"/>
    /// with a message for the user if it isn't a backup, breaks the safety rules, or comes from a version that writes
    /// backups differently.
    /// </summary>
    public static BackupContents Inspect(string path)
    {
        using var file = File.OpenRead(path);
        using var zip = Open(file);
        return Check(zip).Contents;
    }

    /// <summary>
    /// Replaces the setup in <paramref name="dataDirectory"/> with a backup's: checks and unpacks the backup, saves the
    /// setup from before in <see cref="SafetyBackupFolder"/>, then swaps one for the other. Layouts and themes that
    /// aren't in the backup go; logs and anything else a backup doesn't hold stay. Nothing may be using the data folder
    /// meanwhile: the app restores only once it has closed everything. Returns the backup of the setup from before.
    /// Throws <see cref="InvalidDataException"/> or <see cref="IOException"/>; until the swap, nothing has changed.
    /// </summary>
    public static string Restore(string path, string dataDirectory, DateTimeOffset now)
    {
        var staging = Path.Combine(dataDirectory, StagingFolder);
        DeleteFolder(staging);
        try
        {
            using (var file = File.OpenRead(path))
            using (var zip = Open(file))
                Unpack(Check(zip), staging, dataDirectory);

            var before = NewSafetyBackupPath(dataDirectory, now);
            Directory.CreateDirectory(Path.GetDirectoryName(before)!);
            var saved = Write(dataDirectory, before, now);
            Swap(staging, dataDirectory, saved.Files, before);
            return before;
        }
        finally
        {
            DeleteFolder(staging);
        }
    }

    /// <summary>
    /// Where a restore saves the setup it replaces: a folder beside the data folder, such as
    /// <c>%AppData%\ImpiousBonum backups</c>, out of the way of the restore.
    /// </summary>
    public static string SafetyBackupFolder(string dataDirectory)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory));
        var name = Path.GetFileName(full);
        return Path.Combine(Path.GetDirectoryName(full) ?? full, (name.Length > 0 ? name : "Impious Bonum") + " backups");
    }

    private static string NewSafetyBackupPath(string dataDirectory, DateTimeOffset now)
    {
        var folder = SafetyBackupFolder(dataDirectory);
        var name = $"Before restoring {now.ToString("yyyy-MM-dd HH.mm", CultureInfo.InvariantCulture)}";
        var path = Path.Combine(folder, name + Extension);
        for (var i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"{name} ({i}){Extension}");
        return path;
    }

    /// <summary>
    /// The files in the data folder a backup holds, with their names in it. Links to folders elsewhere are passed over,
    /// so neither backing up nor restoring reaches outside the data folder.
    /// </summary>
    private static IEnumerable<(string Entry, Kind Kind, string Path)> SetupFiles(string dataDirectory)
    {
        var candidates = new List<(string Entry, string Path)>();
        var settings = Path.Combine(Path.GetFullPath(dataDirectory), SettingsEntry);
        if (File.Exists(settings))
            candidates.Add((SettingsEntry, settings));
        foreach (var folder in (string[])[LayoutsFolder, ThemesFolder])
        {
            foreach (var file in FilesIn(Path.Combine(dataDirectory, folder)))
                candidates.Add(($"{folder}/{file.Name}", file.FullName));
        }
        foreach (var directory in FoldersIn(Path.Combine(dataDirectory, LayoutsFolder)))
        {
            foreach (var file in FilesIn(directory.FullName))
                candidates.Add(($"{LayoutsFolder}/{directory.Name}/{file.Name}", file.FullName));
        }

        foreach (var (entry, path) in candidates)
        {
            if (Classify(entry) is var kind and not (Kind.Other or Kind.Manifest))
                yield return (entry, kind, path);
        }
    }

    private static IEnumerable<FileInfo> FilesIn(string directory) => Directory.Exists(directory)
        ? new DirectoryInfo(directory).EnumerateFiles().Where(f => !f.Attributes.HasFlag(FileAttributes.ReparsePoint)).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
        : [];

    private static IEnumerable<DirectoryInfo> FoldersIn(string directory) => Directory.Exists(directory)
        ? new DirectoryInfo(directory).EnumerateDirectories().Where(d => !d.Attributes.HasFlag(FileAttributes.ReparsePoint)).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
        : [];

    /// <summary>
    /// What a name in a backup is, by the same rules for writing one and reading one: anything else isn't part of a
    /// backup. Folders in <c>layouts</c> hold only fonts, pictures and videos (and the note marking an imported
    /// layout's folder), one level deep.
    /// </summary>
    private static Kind Classify(string entry)
    {
        var parts = entry.Split('/');
        if (parts.Length == 1)
            return Is(parts[0], ManifestEntry) ? Kind.Manifest : Is(parts[0], SettingsEntry) ? Kind.Settings : Kind.Other;
        if (parts.Length == 2 && IsNamedFile(parts[1]))
            return Is(parts[0], LayoutsFolder) ? Kind.Layout : Is(parts[0], ThemesFolder) ? Kind.Theme : Kind.Other;
        if (parts.Length == 3 && Is(parts[0], LayoutsFolder) && IsSafeName(parts[1]) && IsSafeName(parts[2])
            && (Is(parts[2], LayoutLibrary.ImportMarker) || LayoutFileExtensions.Contains(Path.GetExtension(parts[2]).ToLowerInvariant())))
            return Kind.LayoutFile;
        return Kind.Other;
    }

    private static bool Is(string name, string expected) => string.Equals(name, expected, StringComparison.OrdinalIgnoreCase);

    /// <summary>A layout or theme file: a valid name and <c>.json</c>, as the lists of layouts and themes take.</summary>
    private static bool IsNamedFile(string fileName) =>
        fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && LayoutLibrary.ValidateName(fileName[..^".json".Length]) is null;

    /// <summary>A file or folder name that's safe to create: no path in it, and nothing Windows refuses or keeps for a device.</summary>
    private static bool IsSafeName(string name) =>
        name.Length is > 0 and <= MaxNameLength
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && name == name.Trim()
        && !name.EndsWith('.')
        && !LayoutLibrary.IsReservedName(name);

    private static string NameOf(string entry) => Path.GetFileNameWithoutExtension(entry);

    private static long LimitFor(Kind kind) => kind switch
    {
        Kind.LayoutFile => MaxFileBytes,
        Kind.Manifest => MaxManifestBytes,
        _ => MaxJsonBytes,
    };

    private static ZipArchive Open(Stream source)
    {
        try
        {
            return new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException(NotABackup);
        }
    }

    /// <summary>Checks every entry's name and the sizes its header claims, and reads the manifest.</summary>
    private static Checked Check(ZipArchive zip)
    {
        if (zip.Entries.Count > MaxEntries)
            throw new InvalidDataException($"The file has too many entries ({zip.Entries.Count}).");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<(string Name, Kind Kind, ZipArchiveEntry Entry)>();
        var skipped = new List<string>();
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            if (!LayoutPackage.IsSafeEntryName(entry.FullName))
                throw new InvalidDataException($"The file contains an unsafe path: {entry.FullName}");
            var name = entry.FullName.Replace('\\', '/');
            if (name.EndsWith('/'))
                continue;
            if (!names.Add(name))
                throw new InvalidDataException($"The file contains {entry.FullName} twice.");

            var kind = Classify(name);
            if (kind == Kind.Other)
            {
                skipped.Add($"{name}: not something a backup holds");
                continue;
            }
            var limit = LimitFor(kind);
            if (entry.Length > limit)
                throw TooBig(name, limit);
            total += entry.Length;
            if (total > MaxTotalBytes)
                throw TooBigInAll();
            entries.Add((name, kind, entry));
        }

        var manifest = entries.FirstOrDefault(e => e.Kind == Kind.Manifest).Entry ?? throw new InvalidDataException(NotABackup);
        var (created, app, dataFolder) = ReadManifest(manifest);
        List<string> Names(Kind kind) => [.. entries.Where(e => e.Kind == kind).Select(e => NameOf(e.Name)).Order(StringComparer.OrdinalIgnoreCase)];
        return new Checked(entries, dataFolder, new BackupContents
        {
            Created = created,
            AppVersion = app,
            HasSettings = entries.Any(e => e.Kind == Kind.Settings),
            Layouts = Names(Kind.Layout),
            Themes = Names(Kind.Theme),
            Skipped = skipped,
        });
    }

    private static (DateTimeOffset? Created, string? App, string? DataFolder) ReadManifest(ZipArchiveEntry entry)
    {
        long total = 0;
        using var json = new MemoryStream();
        Copy(entry, ManifestEntry, json, MaxManifestBytes, ref total);
        JsonObject? manifest;
        try
        {
            manifest = JsonNode.Parse(json.ToArray()) as JsonObject;
        }
        catch (JsonException)
        {
            manifest = null;
        }
        if (manifest is null || manifest.GetString("format") != FormatName)
            throw new InvalidDataException(NotABackup);
        if (JsonDefaults.TryGetNumber(manifest["version"], out var version) && version > FormatVersion)
            throw new InvalidDataException("This backup is from a newer version of Impious Bonum, which saves backups differently. Update Impious Bonum (About and updates), then restore it.");

        DateTimeOffset? created = manifest["created"] is JsonValue value && value.TryGetValue<DateTimeOffset>(out var when) ? when : null;
        return (created, manifest.GetString("app"), manifest.GetString("dataFolder"));
    }

    /// <summary>Writes what a backup holds into <paramref name="staging"/>, counting what it unpacks against the limits.</summary>
    private static void Unpack(Checked backup, string staging, string dataDirectory)
    {
        var root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
        long total = 0;
        foreach (var (name, kind, entry) in backup.Entries)
        {
            if (kind == Kind.Manifest)
                continue;
            var target = Path.GetFullPath(Path.Combine(root, PathOf(name, kind)));
            // Checked already, but nothing from the file decides where a write lands without this.
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The file contains an unsafe path: {name}");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            if (kind == Kind.LayoutFile)
            {
                using var file = File.Create(target);
                Copy(entry, name, file, MaxFileBytes, ref total);
                continue;
            }

            using var memory = new MemoryStream();
            Copy(entry, name, memory, MaxJsonBytes, ref total);
            var json = memory.ToArray();
            if (kind == Kind.Settings)
                CheckSettings(json);
            File.WriteAllBytes(target, MovePaths(json, backup.DataFolder, dataDirectory));
        }
    }

    /// <summary>A name in the backup as a path under the data folder, with the app's own spelling of its folders.</summary>
    private static string PathOf(string name, Kind kind)
    {
        var parts = name.Split('/');
        parts[0] = kind switch
        {
            Kind.Settings => SettingsEntry,
            Kind.Theme => ThemesFolder,
            _ => LayoutsFolder,
        };
        return Path.Combine(parts);
    }

    /// <summary>
    /// Settings that can't be read would start the app from its defaults, so rather than put those in place, the
    /// restore stops.
    /// </summary>
    private static void CheckSettings(byte[] json)
    {
        try
        {
            using var reader = new StreamReader(new MemoryStream(json));
            JsonSerializer.Deserialize<AppSettings>(reader.ReadToEnd(), JsonDefaults.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The settings in the backup can't be read: {ex.Message}");
        }
    }

    /// <summary>
    /// Points paths into the data folder the backup was made from (an imported layout's font, say) at the one it's
    /// restored to, for a backup from another Windows account or PC. Done on the text, so comments written by hand stay.
    /// </summary>
    private static byte[] MovePaths(byte[] json, string? from, string to)
    {
        if (from is null || !Path.IsPathFullyQualified(from))
            return json;
        var oldFolder = Path.TrimEndingDirectorySeparator(from) + Path.DirectorySeparatorChar;
        var newFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(to)) + Path.DirectorySeparatorChar;
        if (Is(oldFolder, newFolder))
            return json;

        var text = Encoding.UTF8.GetString(json);
        var replacement = JsonEncodedText.Encode(newFolder).ToString();
        var moved = text;
        // As the app writes it (some characters escaped), and as typed by hand (only the backslashes).
        foreach (var written in new[] { JsonEncodedText.Encode(oldFolder).ToString(), oldFolder.Replace("\\", "\\\\") }.Distinct())
            moved = moved.Replace(written, replacement, StringComparison.OrdinalIgnoreCase);
        return moved == text ? json : Encoding.UTF8.GetBytes(moved);
    }

    /// <summary>
    /// Deletes the setup that's been saved to <paramref name="before"/> and moves the unpacked one into its place.
    /// Anything that couldn't be saved is left where it is.
    /// </summary>
    private static void Swap(string staging, string dataDirectory, IReadOnlyList<string> saved, string before)
    {
        var layouts = Path.GetFullPath(Path.Combine(dataDirectory, LayoutsFolder));
        try
        {
            foreach (var file in saved)
                File.Delete(file);
            // Folders of fonts and pictures emptied by that, so a layout that's gone leaves nothing behind.
            foreach (var folder in saved.Select(Path.GetDirectoryName).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (Is(Path.GetDirectoryName(folder) ?? "", layouts) && !Directory.EnumerateFileSystemEntries(folder).Any())
                    Directory.Delete(folder);
            }

            foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).ToList())
            {
                var target = Path.Combine(dataDirectory, Path.GetRelativePath(staging, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(file, target, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"{ex.Message} Your setup from before is saved in {before}: restore that to go back to it.", ex);
        }
    }

    private static void DeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left from an earlier restore and in use; the next restore clears it.
        }
    }

    /// <summary>Copies an entry out, stopping as soon as it passes a limit: the sizes in the zip's headers can't be trusted.</summary>
    private static void Copy(ZipArchiveEntry entry, string name, Stream output, long limit, ref long total)
    {
        using var input = entry.Open();
        var buffer = new byte[81920];
        long length = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            length += read;
            total += read;
            if (length > limit)
                throw TooBig(name, limit);
            if (total > MaxTotalBytes)
                throw TooBigInAll();
            output.Write(buffer, 0, read);
        }
    }

    private static InvalidDataException TooBig(string name, long limit) => new($"{name} is bigger than {Size(limit)}.");

    private static InvalidDataException TooBigInAll() => new($"Unpacked, the backup would be bigger than {Size(MaxTotalBytes)}.");

    private static string Size(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024L * 1024 * 1024)} GB",
        >= 1024 * 1024 => $"{bytes / (1024 * 1024)} MB",
        _ => $"{bytes / 1024} KB",
    };

    private static void WriteText(ZipArchive zip, string entryName, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(entryName, CompressionLevel.Optimal).Open());
        writer.Write(text);
    }

    /// <summary>"0.16.0", or "0.16.0-beta", as a version to compare; null if it isn't one.</summary>
    internal static Version? ParseVersion(string? text) =>
        text is not null && Version.TryParse(text.Split('-', '+')[0], out var version) ? Normalise(version) : null;

    private static Version? Normalise(Version? version) =>
        version is null ? null : new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
}
