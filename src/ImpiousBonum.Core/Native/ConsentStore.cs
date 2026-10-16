using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace ImpiousBonum.Core.Native;

/// <summary>One app's record of using the microphone or camera, as Windows keeps it for its privacy settings.</summary>
/// <param name="Key">
/// The app's key: a Store app's package family name (<c>Microsoft.WindowsCamera_8wekyb3d8bbwe</c>), or a desktop app's
/// path with # for \ (<c>C:#Program Files#Discord#Discord.exe</c>).
/// </param>
/// <param name="Packaged">True for a Store app, false for a desktop app (one under <c>NonPackaged</c>).</param>
/// <param name="Start">When it last started using it (<c>LastUsedTimeStart</c>, a FILETIME), or 0 if it never has.</param>
/// <param name="Stop">When it last stopped (<c>LastUsedTimeStop</c>): 0 while it's using it.</param>
public readonly record struct ConsentEntry(string Key, bool Packaged, long Start, long Stop)
{
    /// <summary>Whether the app is using it now: it has started, and not stopped since.</summary>
    public bool InUse => Start > 0 && (Stop == 0 || Stop < Start);
}

/// <summary>
/// Which apps have used the microphone or camera, and which are using it now, from the record behind Windows' privacy
/// settings and its microphone and camera icons (HKCU\…\CapabilityAccessManager\ConsentStore). Reading it needs no
/// permission and isn't using either, so it never lights those icons. The record keeps every app that ever used one
/// (well over a hundred keys on a PC that's had Discord a while: a new key for each version), so it's read again only
/// after Windows changes it, which a change notification on the key says. Not thread-safe: use one reader from one loop.
/// </summary>
internal sealed partial class ConsentStoreReader : IDisposable
{
    /// <summary>The capabilities' keys, under HKEY_CURRENT_USER.</summary>
    public const string Root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    private const string Desktop = "NonPackaged";

    // Subkeys added or removed, and values changed, anywhere below the key; and kept after the thread that asked ends,
    // since the sampler's loop moves between thread-pool threads.
    private const uint NotifyName = 0x1, NotifyLastSet = 0x4, NotifyThreadAgnostic = 0x10000000;

    private readonly string _path;
    private readonly AutoResetEvent _changed = new(false);
    private RegistryKey? _key;
    private bool _watching;
    private IReadOnlyList<ConsentEntry>? _entries;

    /// <param name="path">The capability's key under HKEY_CURRENT_USER, e.g. <see cref="Root"/>\microphone.</param>
    public ConsentStoreReader(string path) => _path = path;

    /// <summary>Every app with a record, or null when there's no key (nothing has ever asked to use it).</summary>
    public IReadOnlyList<ConsentEntry>? Read()
    {
        if (_watching && !_changed.WaitOne(0))
            return _entries;
        try
        {
            _key ??= Registry.CurrentUser.OpenSubKey(_path);
            if (_key is null)
                return _entries = null;
            // Ask before reading, so a change made while reading isn't missed. Should asking fail, it reads every time.
            _watching = RegNotifyChangeKeyValue(_key.Handle, true, NotifyName | NotifyLastSet | NotifyThreadAgnostic, _changed.SafeWaitHandle, true) == 0;
            return _entries = ReadEntries(_key);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            // The key was deleted while open, or can't be read: open it afresh next time.
            Close();
            return _entries = null;
        }
    }

    /// <summary>The records under a capability's key: Store apps' keys, and desktop apps' under NonPackaged.</summary>
    internal static List<ConsentEntry> ReadEntries(RegistryKey key)
    {
        var entries = new List<ConsentEntry>();
        foreach (var name in key.GetSubKeyNames())
        {
            using var app = key.OpenSubKey(name);
            if (app is null)
                continue;
            if (!name.Equals(Desktop, StringComparison.OrdinalIgnoreCase))
            {
                entries.Add(Entry(name, true, app));
                continue;
            }
            foreach (var path in app.GetSubKeyNames())
            {
                using var desktop = app.OpenSubKey(path);
                if (desktop is not null)
                    entries.Add(Entry(path, false, desktop));
            }
        }
        return entries;
    }

    private static ConsentEntry Entry(string name, bool packaged, RegistryKey app) =>
        new(name, packaged, app.GetValue("LastUsedTimeStart") is long start ? start : 0, app.GetValue("LastUsedTimeStop") is long stop ? stop : 0);

    private void Close()
    {
        _key?.Dispose();
        _key = null;
        _watching = false;
    }

    public void Dispose()
    {
        Close();
        _changed.Dispose();
    }

    [LibraryImport("advapi32.dll")]
    private static partial int RegNotifyChangeKeyValue(SafeRegistryHandle key, [MarshalAs(UnmanagedType.Bool)] bool watchSubtree, uint filter,
        SafeWaitHandle changed, [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
}
