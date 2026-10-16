using System.Runtime.InteropServices;

namespace ImpiousBonum.Core.Native;

/// <summary>The microphone Windows uses for calls, and whether it's muted.</summary>
/// <param name="Name">Its name as the Sound settings show it, e.g. <c>Headset Microphone (Arctis 7)</c>; null if it has none.</param>
public sealed record MicrophoneState(string? Name, bool Muted);

/// <summary>
/// The default communications microphone, through Core Audio's IAudioEndpointVolume: the same mute as the Sound
/// settings. Looking at it or muting it doesn't open the microphone, so Windows doesn't count it as being used or show
/// its microphone icon.
/// </summary>
public static class Microphone
{
    /// <summary>Mutes the microphone, or unmutes it if it's muted. Returns whether it's muted now, or null when there's no microphone.</summary>
    public static bool? ToggleMute()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            try
            {
                return WithVolume(enumerator, volume =>
                    volume.GetMute(out var muted) >= 0 && volume.SetMute(!muted, 0) >= 0 ? !muted : (bool?)null);
            }
            finally
            {
                Marshal.ReleaseComObject(enumerator);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            // No audio service, or no Core Audio at all.
            return null;
        }
    }

    /// <summary>Runs <paramref name="use"/> on the default communications microphone's volume control; null when there's no microphone.</summary>
    internal static T? WithVolume<T>(IMMDeviceEnumerator enumerator, Func<IAudioEndpointVolume, T?> use, Action<IMMDevice>? device = null)
    {
        if (enumerator.GetDefaultAudioEndpoint(Capture, Communications, out var endpoint) < 0 || endpoint is null)
            return default;
        try
        {
            device?.Invoke(endpoint);
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (endpoint.Activate(ref iid, ClsctxAll, 0, out var activated) < 0 || activated is not IAudioEndpointVolume volume)
                return default;
            try
            {
                return use(volume);
            }
            finally
            {
                Marshal.ReleaseComObject(volume);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(endpoint);
        }
    }

    /// <summary>The device's name as the Sound settings show it (PKEY_Device_FriendlyName), or null.</summary>
    internal static string? FriendlyName(IMMDevice device)
    {
        if (device.OpenPropertyStore(StgmRead, out var store) < 0 || store is null)
            return null;
        try
        {
            var key = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
            if (store.GetValue(ref key, out var value) < 0)
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
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    // EDataFlow.eCapture and ERole.eCommunications: the recording device apps use for calls.
    private const int Capture = 1;
    private const int Communications = 2;
    private const int ClsctxAll = 0x17;
    private const int StgmRead = 0;
    private const ushort VtLpwstr = 31;

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumerator
    {
    }

    // Vtable order matters: IUnknown is implied. Methods never called are declared parameterless purely to reserve their slots.
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints();

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int clsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object? instance);

        [PreserveSig]
        int OpenPropertyStore(int access, out IPropertyStore? store);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string? id);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        void GetCount();
        void GetAt();

        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify();
        void UnregisterControlChangeNotify();
        void GetChannelCount();
        void SetMasterVolumeLevel();
        void SetMasterVolumeLevelScalar();
        void GetMasterVolumeLevel();
        void GetMasterVolumeLevelScalar();
        void SetChannelVolumeLevel();
        void SetChannelVolumeLevelScalar();
        void GetChannelVolumeLevel();
        void GetChannelVolumeLevelScalar();

        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, nint eventContext);

        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey(Guid formatId, int propertyId)
    {
        public Guid FormatId = formatId;
        public int PropertyId = propertyId;
    }

    /// <summary>A PROPVARIANT: its type, and the start of its union, which for a string is the pointer to it.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct PropVariant
    {
        [FieldOffset(0)]
        public ushort Type;

        [FieldOffset(8)]
        public nint Value;
    }
}

/// <summary>
/// Reads the default communications microphone's name and mute for <see cref="Providers.PrivacyProvider"/>, keeping one
/// device enumerator and looking the name up again only when the default microphone changes. Not thread-safe: use one
/// reader from one loop.
/// </summary>
internal sealed class MicrophoneReader : IDisposable
{
    private Microphone.IMMDeviceEnumerator? _enumerator;
    private string? _deviceId;
    private string? _name;

    /// <summary>The microphone's name and mute, or null when there's no microphone.</summary>
    public MicrophoneState? Read()
    {
        try
        {
            _enumerator ??= (Microphone.IMMDeviceEnumerator)new Microphone.MMDeviceEnumerator();
            string? name = null;
            var muted = Microphone.WithVolume(_enumerator, volume => volume.GetMute(out var on) >= 0 ? on : (bool?)null, device =>
            {
                if (device.GetId(out var id) >= 0 && id != _deviceId)
                {
                    _deviceId = id;
                    _name = Microphone.FriendlyName(device);
                }
                name = _name;
            });
            return muted is { } on ? new MicrophoneState(name, on) : null;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            // The audio service isn't running (or restarted): start again next time.
            Dispose();
            return null;
        }
    }

    public void Dispose()
    {
        if (_enumerator is not null)
            Marshal.ReleaseComObject(_enumerator);
        _enumerator = null;
        _deviceId = null;
    }
}
