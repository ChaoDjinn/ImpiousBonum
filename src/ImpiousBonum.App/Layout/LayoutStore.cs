using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace ImpiousBonum.App.Layout;

/// <summary>Loads layout.json (seeding it from the built-in default on first run) and watches it so edits apply on save.</summary>
public sealed class LayoutStore : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly DispatcherTimer _debounce;

    public LayoutStore()
    {
        AppPaths.EnsureCreated();
        if (!File.Exists(AppPaths.LayoutFile))
            File.WriteAllText(AppPaths.LayoutFile, ReadDefaultJson());

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Changed?.Invoke(this, EventArgs.Empty);
        };

        _watcher = new FileSystemWatcher(AppPaths.DataDirectory, Path.GetFileName(AppPaths.LayoutFile))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        // Editors often save via several writes or a rename; restart the debounce on each.
        FileSystemEventHandler restart = (_, _) => _debounce.Dispatcher.BeginInvoke(() =>
        {
            _debounce.Stop();
            _debounce.Start();
        });
        _watcher.Changed += restart;
        _watcher.Created += restart;
        _watcher.Renamed += (s, e) => restart(s, e);
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Raised on the UI thread after layout.json changes on disk.</summary>
    public event EventHandler? Changed;

    /// <summary>Reads layout.json. Throws <see cref="JsonException"/> or <see cref="IOException"/> if it can't be read.</summary>
    public LayoutDocument Load()
    {
        var json = ReadWithRetry(AppPaths.LayoutFile);
        return JsonSerializer.Deserialize<LayoutDocument>(json, JsonDefaults.Options) ?? LoadDefault();
    }

    /// <summary>
    /// Writes layout.json. JSON can't carry comments through a round trip, so a short header pointing at the
    /// widget reference replaces any hand-written ones.
    /// </summary>
    public void Save(LayoutDocument layout)
    {
        var json = JsonSerializer.Serialize(layout, JsonDefaults.Options);
        File.WriteAllText(AppPaths.LayoutFile, SavedHeader + json + Environment.NewLine);
    }

    private const string SavedHeader =
        "// Impious Bonum layout, saved by the layout editor. You can still edit it by hand; changes apply on save.\n" +
        "// Widget reference: https://github.com/ChaoDjinn/ImpiousBonum/blob/main/docs/widgets.md\n";

    public static LayoutDocument LoadDefault() =>
        JsonSerializer.Deserialize<LayoutDocument>(ReadDefaultJson(), JsonDefaults.Options)!;

    private static string ReadDefaultJson()
    {
        using var stream = typeof(LayoutStore).Assembly.GetManifestResourceStream("ImpiousBonum.default-layout.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string ReadWithRetry(string path)
    {
        // The file may still be locked by the editor that just saved it.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(50);
            }
        }
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _debounce.Stop();
    }
}
