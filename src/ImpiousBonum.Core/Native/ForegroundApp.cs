using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ImpiousBonum.Core.Native;

/// <summary>The process that owns the window the user is currently working in.</summary>
public static partial class ForegroundApp
{
    private static int _cachedProcessId;
    private static string? _cachedName;

    public static (int ProcessId, string? Name) Get()
    {
        var window = GetForegroundWindow();
        if (window == 0 || GetWindowThreadProcessId(window, out var processId) == 0 || processId == 0)
            return (0, null);

        var id = (int)processId;
        if (id != _cachedProcessId)
        {
            _cachedProcessId = id;
            _cachedName = TryGetName(id);
        }
        return (id, _cachedName);
    }

    private static string? TryGetName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);
}
