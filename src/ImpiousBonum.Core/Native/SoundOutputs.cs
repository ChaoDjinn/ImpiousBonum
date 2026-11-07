using System.Runtime.InteropServices;
using static ImpiousBonum.Core.Native.AudioSessionReader;

namespace ImpiousBonum.Core.Native;

/// <summary>What a playback device is, from what its driver says (its form factor), for its icon.</summary>
public enum OutputKind
{
    Speakers,
    Headphones,
    Headset,

    /// <summary>A TV or monitor, through HDMI or DisplayPort.</summary>
    Display,
    Other,
}

/// <summary>
/// A playback device that's on: its id, its name and its hardware's (<c>Headphones</c>, <c>Arctis Nova 7X</c>), what
/// kind it is, and whether Windows plays to it now.
/// </summary>
/// <param name="Name">Its own name, as the sound settings show it and you can rename it: <c>Headphones</c>, <c>Speakers</c>.</param>
/// <param name="Hardware">What it's part of: <c>Arctis Nova 7X</c>, <c>Realtek(R) Audio</c>; empty when Windows doesn't say.</param>
public sealed record SoundOutput(string Id, string Name, string Hardware, OutputKind Kind, bool Default)
{
    /// <summary>Both names, as the sound settings list it: <c>Headphones (Arctis Nova 7X)</c>.</summary>
    public string FullName => Hardware.Length == 0 ? Name : $"{Name} ({Hardware})";
}

/// <summary>
/// The playback devices, and switching which one Windows plays to: the default device for everything (games, music
/// and calls), as the sound settings' output picker or a click in the volume flyout does. Windows has no published way
/// for an app to do that; this uses the one the sound control panel itself uses (IPolicyConfig), as other sound
/// switchers do. Call from a thread-pool thread, not the UI's.
/// </summary>
public static class SoundOutputs
{
    private const int Render = 0;
    private const int ActiveDevices = 1;
    private const int StgmRead = 0;
    private const ushort VtLpwstr = 31, VtUi4 = 19;

    // ERole: eConsole, eMultimedia, eCommunications. The sound settings set all three.
    private static readonly int[] Roles = [0, 1, 2];

    private static readonly Microphone.PropertyKey DeviceDescription = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);
    private static readonly Microphone.PropertyKey InterfaceName = new(new Guid("026e516e-b814-414b-83cd-856d6fef4822"), 2);
    private static readonly Microphone.PropertyKey FormFactor = new(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 0);

    /// <summary>The playback devices that are on, in the sound settings' order, the one Windows plays to marked; none without an audio service.</summary>
    public static IReadOnlyList<SoundOutput> List()
    {
        IDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = (IDeviceEnumerator)new Microphone.MMDeviceEnumerator();
            string? defaultId = null;
            if (enumerator.GetDefaultAudioEndpoint(Render, 1, out var current) >= 0 && current is not null)
            {
                current.GetId(out defaultId);
                Marshal.ReleaseComObject(current);
            }
            if (enumerator.EnumAudioEndpoints(Render, ActiveDevices, out var devices) < 0 || devices is null)
                return [];
            var outputs = new List<SoundOutput>();
            try
            {
                if (devices.GetCount(out var count) < 0)
                    return [];
                for (var i = 0; i < count; i++)
                {
                    if (devices.Item(i, out var device) < 0 || device is null)
                        continue;
                    try
                    {
                        if (device.GetId(out var id) < 0 || id is null)
                            continue;
                        var (name, hardware, kind) = Describe(device);
                        outputs.Add(new SoundOutput(id, name, hardware, kind, id == defaultId));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(devices);
            }
            return outputs;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            return [];
        }
        finally
        {
            if (enumerator is not null)
                Marshal.ReleaseComObject(enumerator);
        }
    }

    /// <summary>
    /// Plays to the output <paramref name="target"/> names (see <see cref="Pick"/>). Returns why it couldn't, or null
    /// when it did.
    /// </summary>
    public static string? Switch(string target)
    {
        var outputs = List();
        if (outputs.Count == 0)
            return "There are no sound outputs to switch to.";
        if (Pick(outputs, target) is not { } output)
            return $"No sound output is called \"{target}\". Its name is as the sound settings show it, such as Headphones.";
        if (output.Default)
            return null;
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigClient();
            try
            {
                foreach (var role in Roles)
                {
                    if (policy.SetDefaultEndpoint(output.Id, role) < 0)
                        return $"Windows wouldn't play to {output.FullName}.";
                }
                return null;
            }
            finally
            {
                Marshal.ReleaseComObject(policy);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return $"Windows wouldn't play to {output.FullName}: {ex.Message}";
        }
    }

    /// <summary>
    /// The output <paramref name="target"/> names, among <paramref name="outputs"/>:
    /// <list type="bullet">
    /// <item>one name (its id, either of its names or both, or part of them, ignoring case) is that output;</item>
    /// <item>names with commas between them take turns: the one after the output playing now, or the first;</item>
    /// <item>empty takes turns among them all.</item>
    /// </list>
    /// Null when none is named.
    /// </summary>
    public static SoundOutput? Pick(IReadOnlyList<SoundOutput> outputs, string target)
    {
        var names = target.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (names.Length == 1)
            return Find(outputs, names[0]);
        var turns = names.Length == 0 ? outputs : names.Select(name => Find(outputs, name)).OfType<SoundOutput>().Distinct().ToList();
        if (turns.Count == 0)
            return null;
        var playing = turns.ToList().FindIndex(o => o.Default);
        return turns[(playing + 1) % turns.Count];
    }

    /// <summary>The output one name names: its id or a name exactly, or failing that the first whose names contain it.</summary>
    public static SoundOutput? Find(IReadOnlyList<SoundOutput> outputs, string name) =>
        outputs.FirstOrDefault(o => o.Id == name
            || name.Equals(o.FullName, StringComparison.OrdinalIgnoreCase)
            || name.Equals(o.Name, StringComparison.OrdinalIgnoreCase)
            || name.Equals(o.Hardware, StringComparison.OrdinalIgnoreCase))
        ?? outputs.FirstOrDefault(o => o.FullName.Contains(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A device's own name, its hardware's and its kind, without the numbers Windows puts in front (see <see cref="Unnumbered"/>).</summary>
    private static (string Name, string Hardware, OutputKind Kind) Describe(Microphone.IMMDevice device)
    {
        if (device.OpenPropertyStore(StgmRead, out var store) < 0 || store is null)
            return ("Sound output", "", OutputKind.Other);
        try
        {
            var name = Unnumbered(Text(store, DeviceDescription)) is { Length: > 0 } own ? own : "Sound output";
            var hardware = Unnumbered(Text(store, InterfaceName));
            var kind = Number(store, FormFactor) switch
            {
                1 => OutputKind.Speakers,
                3 => OutputKind.Headphones,
                5 or 6 => OutputKind.Headset,
                9 => OutputKind.Display,
                _ => OutputKind.Other,
            };
            return (name, hardware, kind);
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    /// <summary>
    /// A name without the number Windows puts in front: <c>4- Arctis Nova 7X</c>, a second device of the same make, is
    /// Arctis Nova 7X, and <c>4 - Beyond TV</c>, a TV on the graphics card's fourth output, is Beyond TV.
    /// </summary>
    internal static string Unnumbered(string? name)
    {
        var text = name?.Trim() ?? "";
        var digits = 0;
        while (digits < text.Length && char.IsAsciiDigit(text[digits]))
            digits++;
        var rest = text[digits..].TrimStart();
        return digits > 0 && rest.StartsWith("- ", StringComparison.Ordinal) && rest.Length > 2 ? rest[2..].Trim() : text;
    }

    private static string? Text(Microphone.IPropertyStore store, Microphone.PropertyKey key)
    {
        var copy = key;
        if (store.GetValue(ref copy, out var value) < 0)
            return null;
        try
        {
            return value.Type == VtLpwstr && value.Value != 0 ? Marshal.PtrToStringUni(value.Value) : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    private static int? Number(Microphone.IPropertyStore store, Microphone.PropertyKey key)
    {
        var copy = key;
        if (store.GetValue(ref copy, out var value) < 0)
            return null;
        try
        {
            return value.Type == VtUi4 ? (int)(value.Value & 0xFFFFFFFF) : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref Microphone.PropVariant value);

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient
    {
    }

    // The sound control panel's own interface. Vtable order matters: methods never called reserve their slots.
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        void GetMixFormat();
        void GetDeviceFormat();
        void ResetDeviceFormat();
        void SetDeviceFormat();
        void GetProcessingPeriod();
        void SetProcessingPeriod();
        void GetShareMode();
        void SetShareMode();
        void GetPropertyValue();
        void SetPropertyValue();

        [PreserveSig]
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);

        void SetEndpointVisibility();
    }
}
