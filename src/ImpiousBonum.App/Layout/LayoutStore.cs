using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using ImpiousBonum.App.Shell;

namespace ImpiousBonum.App.Layout;

/// <summary>
/// The saved layouts and which one is active (remembered in settings.json). Watches the active layout's file so
/// hand edits apply on save; edits to other layouts do nothing until they're selected.
/// </summary>
public sealed class LayoutStore : IDisposable
{
    private readonly AppSettings _settings;
    private readonly FileSystemWatcher _watcher;
    private readonly DispatcherTimer _debounce;

    public LayoutStore(AppSettings settings)
    {
        _settings = settings;
        Library = new LayoutLibrary(AppPaths.DataDirectory);
        Library.EnsureSeeded();
        Active = Library.Resolve(settings.ActiveLayout);
        RememberActive();

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Changed?.Invoke(this, EventArgs.Empty);
        };

        _watcher = new FileSystemWatcher(Library.Directory, "*.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        // Editors often save via several writes or a rename; restart the debounce on each.
        void Restart(string? fileName)
        {
            if (!string.Equals(Path.GetFileNameWithoutExtension(fileName), Active, StringComparison.OrdinalIgnoreCase))
                return;
            _debounce.Dispatcher.BeginInvoke(() =>
            {
                _debounce.Stop();
                _debounce.Start();
            });
        }
        _watcher.Changed += (_, e) => Restart(e.Name);
        _watcher.Created += (_, e) => Restart(e.Name);
        _watcher.Renamed += (_, e) => Restart(e.Name);
        _watcher.EnableRaisingEvents = true;
    }

    public LayoutLibrary Library { get; }

    /// <summary>Name of the layout the dashboard shows and the editor edits.</summary>
    public string Active { get; private set; }

    public string ActivePath => Library.PathOf(Active);

    /// <summary>Raised on the UI thread after the active layout's file changes on disk.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised after a different layout becomes active.</summary>
    public event EventHandler? ActiveChanged;

    public IReadOnlyList<string> List() => Library.List();

    /// <summary>Reads the active layout. Throws <see cref="JsonException"/> or <see cref="IOException"/> if it can't be read.</summary>
    public LayoutDocument Load() => Library.Load(Active);

    public LayoutDocument Load(string name) => Library.Load(name);

    /// <summary>The active layout, or the built-in default if its file is broken.</summary>
    public LayoutDocument LoadOrDefault()
    {
        try
        {
            return Load();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return LoadDefault();
        }
    }

    public void Save(LayoutDocument layout) => Library.Save(Active, layout);

    public void Save(string name, LayoutDocument layout) => Library.Save(name, layout);

    /// <summary>Makes a saved layout active and remembers the choice.</summary>
    public void SetActive(string name)
    {
        var found = Library.Find(name) ?? throw new FileNotFoundException($"There's no layout called \"{name}\".", Library.PathOf(name));
        if (found == Active)
            return;
        Active = found;
        RememberActive();
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Rename(string name, string newName)
    {
        Library.Rename(name, newName);
        if (string.Equals(name, Active, StringComparison.OrdinalIgnoreCase))
        {
            Active = Library.Resolve(newName);
            RememberActive();
        }
    }

    public void Duplicate(string name, string newName) => Library.Duplicate(name, newName);

    /// <summary>Deletes a layout. Deleting the active one switches to another.</summary>
    public void Delete(string name)
    {
        Library.Delete(name);
        if (string.Equals(name, Active, StringComparison.OrdinalIgnoreCase))
        {
            Active = Library.Resolve(null);
            RememberActive();
            ActiveChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public static LayoutDocument LoadDefault() => LayoutLibrary.LoadDefault();

    private void RememberActive()
    {
        if (_settings.ActiveLayout == Active)
            return;
        _settings.ActiveLayout = Active;
        _settings.Save();
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _debounce.Stop();
    }
}
