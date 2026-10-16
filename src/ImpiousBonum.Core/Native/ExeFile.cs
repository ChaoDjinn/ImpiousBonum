using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ImpiousBonum.Core.Native;

/// <summary>What finding a game reads from a program: where its exe is, its description and its icon.</summary>
public static unsafe partial class ExeFile
{
    private const uint QueryLimitedInformation = 0x1000;
    private const uint LoadAsDataFile = 0x2, LoadAsImageResource = 0x20;
    private const int IconType = 3, IconGroupType = 14;

    /// <summary>A group's header (reserved, type, count), then an entry for each picture.</summary>
    private const int GroupHeaderSize = 6, GroupEntrySize = 14;

    /// <summary>An .ico file's header, then its entry for each picture.</summary>
    private const int FileHeaderSize = 6, FileEntrySize = 16;

    private static ReadOnlySpan<byte> PngSignature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The full path of a running process's exe, or null if it has gone or Windows won't say.</summary>
    public static string? PathOf(int processId)
    {
        // Limited information is enough for the path, and is allowed even for a game running as administrator.
        var process = OpenProcess(QueryLimitedInformation, 0, (uint)processId);
        if (process == 0)
            return null;
        try
        {
            var buffer = stackalloc char[1024];
            var length = 1024;
            return QueryFullProcessImageNameW(process, 0, buffer, &length) != 0 ? new string(buffer, 0, length) : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>The exe's description, as Explorer's Properties → Details shows it ("Hollow Knight"), or null if it hasn't one.</summary>
    public static string? Description(string exe)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(exe).FileDescription?.Trim() is { Length: > 0 } description ? description : null;
        }
        catch (Exception ex) when (ex is FileNotFoundException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The exe's own icon, the one Explorer shows, in its biggest size: PNG bytes for a modern 256-pixel icon, or a
    /// one-picture .ico for an older one. Null if the exe has no icon or can't be read. Reads the file's resources
    /// without running anything in it.
    /// </summary>
    public static byte[]? LargestIcon(string exe)
    {
        var module = LoadLibraryExW(exe, 0, LoadAsDataFile | LoadAsImageResource);
        if (module == 0)
            return null;
        try
        {
            if (FirstIconGroup(module) is not { } group || Largest(group) is not { } entry)
                return null;
            var id = BinaryPrimitives.ReadUInt16LittleEndian(entry.AsSpan(12));
            return Resource(module, id, IconType) is { } image ? IconFile(entry, image) : null;
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    /// <summary>
    /// Of an icon group's pictures, the entry of the biggest, the most colourful first; null if it has none. Each entry
    /// is a width and height (0 meaning 256), colours, planes, bits per pixel, length and the picture's resource id.
    /// </summary>
    internal static byte[]? Largest(ReadOnlySpan<byte> group)
    {
        if (group.Length < GroupHeaderSize)
            return null;
        var count = BinaryPrimitives.ReadUInt16LittleEndian(group[4..]);
        var best = -1;
        (int Size, int Bits) bestSize = default;
        for (var i = 0; i < count && GroupHeaderSize + (i + 1) * GroupEntrySize <= group.Length; i++)
        {
            var entry = group.Slice(GroupHeaderSize + i * GroupEntrySize, GroupEntrySize);
            (int Size, int Bits) size = (entry[0] == 0 ? 256 : entry[0], BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]));
            if (best < 0 || size.CompareTo(bestSize) > 0)
                (best, bestSize) = (i, size);
        }
        return best < 0 ? null : group.Slice(GroupHeaderSize + best * GroupEntrySize, GroupEntrySize).ToArray();
    }

    /// <summary>
    /// One icon picture as a file a picture decoder can read: a PNG as it is, or an older bitmap picture behind an .ico
    /// header naming just it.
    /// </summary>
    internal static byte[] IconFile(ReadOnlySpan<byte> entry, ReadOnlySpan<byte> image)
    {
        if (image.StartsWith(PngSignature))
            return image.ToArray();

        var file = new byte[FileHeaderSize + FileEntrySize + image.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(4), 1);
        // The size, colours, planes and bits as the group has them, then the picture's length and where it starts.
        entry[..8].CopyTo(file.AsSpan(FileHeaderSize));
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(FileHeaderSize + 8), image.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(FileHeaderSize + 12), FileHeaderSize + FileEntrySize);
        image.CopyTo(file.AsSpan(FileHeaderSize + FileEntrySize));
        return file;
    }

    // The first icon group is the exe's own icon: the one Explorer shows. Its name is a number, or a string that only
    // lasts while the names are listed, so the listing copies it out.
    [ThreadStatic]
    private static string? _firstName;

    [ThreadStatic]
    private static nint _firstId;

    private static byte[]? FirstIconGroup(nint module)
    {
        _firstName = null;
        _firstId = 0;
        EnumResourceNamesW(module, IconGroupType, &TakeFirst, 0);
        if (_firstName is { } name)
        {
            fixed (char* text = name)
                return Resource(module, (nint)text, IconGroupType);
        }
        return _firstId != 0 ? Resource(module, _firstId, IconGroupType) : null;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int TakeFirst(nint module, nint type, nint name, nint parameter)
    {
        // Numbers are below 65536; anything else points to the name.
        if (name >>> 16 == 0)
            _firstId = name;
        else
            _firstName = Marshal.PtrToStringUni(name);
        return 0;
    }

    private static byte[]? Resource(nint module, nint name, nint type)
    {
        var found = FindResourceW(module, name, type);
        if (found == 0)
            return null;
        var size = SizeofResource(module, found);
        var loaded = LoadResource(module, found);
        var data = loaded == 0 ? null : LockResource(loaded);
        return data is null || size == 0 ? null : new ReadOnlySpan<byte>(data, (int)size).ToArray();
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint OpenProcess(uint access, int inherit, uint processId);

    [LibraryImport("kernel32.dll")]
    private static partial int QueryFullProcessImageNameW(nint process, uint flags, char* name, int* size);

    [LibraryImport("kernel32.dll")]
    private static partial int CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadLibraryExW(string path, nint reserved, uint flags);

    [LibraryImport("kernel32.dll")]
    private static partial int FreeLibrary(nint module);

    [LibraryImport("kernel32.dll")]
    private static partial int EnumResourceNamesW(nint module, nint type, delegate* unmanaged[Stdcall]<nint, nint, nint, nint, int> callback, nint parameter);

    [LibraryImport("kernel32.dll")]
    private static partial nint FindResourceW(nint module, nint name, nint type);

    [LibraryImport("kernel32.dll")]
    private static partial uint SizeofResource(nint module, nint resource);

    [LibraryImport("kernel32.dll")]
    private static partial nint LoadResource(nint module, nint resource);

    [LibraryImport("kernel32.dll")]
    private static partial void* LockResource(nint loaded);
}
