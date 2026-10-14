using System.Runtime.InteropServices;

namespace ImpiousBonum.Core.Native;

/// <summary>
/// The Wi-Fi connection's signal strength and network name, read without asking for the user's location.
/// <para>
/// Since Windows 11 24H2, the Native Wi-Fi calls that reveal access points (scans, BSS lists, and
/// <c>WlanQueryInterface</c>'s current-connection opcode, which has the signal quality and SSID) need precise-location
/// consent: Windows prompts for it once, and lights the location icon in the tray on every call. So the signal comes
/// from the RSSI opcode instead, which isn't restricted, and the network's name from
/// <c>WlanConnectionProfileDetails.GetConnectedSsid</c>, which Microsoft recommends for this. Like the media reader,
/// that WinRT call goes through the ABI directly (ids and slots from windows.networking.connectivity.h) rather than the
/// Windows SDK projection. Not thread-safe: use one reader from one loop.
/// </para>
/// </summary>
internal sealed unsafe class WlanReader : IDisposable
{
    private const uint ClientVersion = 2;
    private const uint OpcodeRssi = 0x10000102;
    private const int StateConnected = 1;

    // WLAN_INTERFACE_INFO: a GUID, a 256-character description and the state.
    private const int InterfaceInfoSize = 16 + 256 * 2 + 4;
    private const int InterfaceListHeader = 8;

    private const string NetworkInformationClass = "Windows.Networking.Connectivity.NetworkInformation";
    private static readonly Guid NetworkInformationStaticsId = new("5074f851-950d-4165-9c15-365619481eea");
    private static readonly Guid ConnectionProfile2Id = new("e2045145-4c9f-400c-9150-7ec7d6e2888a");

    // INetworkInformationStatics
    private const int GetConnectionProfilesSlot = 6;

    // IVectorView<ConnectionProfile>
    private const int GetAtSlot = 6;
    private const int SizeSlot = 7;

    // IConnectionProfile2
    private const int IsWlanConnectionProfileSlot = 7;
    private const int WlanConnectionProfileDetailsSlot = 9;

    // IWlanConnectionProfileDetails
    private const int GetConnectedSsidSlot = 6;

    private const int RoInitMultithreaded = 1;

    [ThreadStatic]
    private static bool t_apartment;

    private nint _client;
    private bool _unavailable;

    /// <summary>
    /// The connected Wi-Fi adapter's signal in dBm, or null when no Wi-Fi is connected. False when this PC has no Wi-Fi
    /// (or the WLAN AutoConfig service isn't running), so there's nothing to read.
    /// </summary>
    public bool TryReadRssi(out int? rssi)
    {
        rssi = null;
        if (!EnsureClient())
            return false;
        nint list = 0;
        if (WlanEnumInterfaces(_client, 0, out list) != 0 || list == 0)
            return false;
        try
        {
            var count = *(uint*)list;
            if (count == 0)
                return false;
            for (var i = 0; i < count; i++)
            {
                var info = (byte*)list + InterfaceListHeader + i * InterfaceInfoSize;
                if (*(int*)(info + InterfaceInfoSize - 4) != StateConnected)
                    continue;
                var guid = *(Guid*)info;
                if (WlanQueryInterface(_client, in guid, OpcodeRssi, 0, out var size, out var data, out _) != 0 || data == 0)
                    continue;
                try
                {
                    if (size >= 4)
                    {
                        rssi = *(int*)data;
                        return true;
                    }
                }
                finally
                {
                    WlanFreeMemory(data);
                }
            }
            return true;
        }
        finally
        {
            WlanFreeMemory(list);
        }
    }

    /// <summary>The name (SSID) of the Wi-Fi network this PC is connected to, or null.</summary>
    public static string? ConnectedSsid()
    {
        // WinRT needs COM on the calling thread; the sampler's loop moves between thread-pool threads, which are MTA.
        if (!t_apartment)
        {
            RoInitialize(RoInitMultithreaded);
            t_apartment = true;
        }

        nint className = 0, statics = 0, profiles = 0;
        try
        {
            if (WindowsCreateString(NetworkInformationClass, NetworkInformationClass.Length, out className) < 0
                || RoGetActivationFactory(className, in NetworkInformationStaticsId, out statics) < 0
                || Get(statics, GetConnectionProfilesSlot, out profiles) < 0 || profiles == 0)
                return null;
            uint count;
            if (((delegate* unmanaged[Stdcall]<nint, uint*, int>)Slot(profiles, SizeSlot))(profiles, &count) < 0)
                return null;
            for (uint i = 0; i < count; i++)
            {
                nint profile = 0;
                if (((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Slot(profiles, GetAtSlot))(profiles, i, &profile) < 0 || profile == 0)
                    continue;
                try
                {
                    if (Ssid(profile) is { Length: > 0 } ssid)
                        return ssid;
                }
                finally
                {
                    Marshal.Release(profile);
                }
            }
            return null;
        }
        finally
        {
            if (profiles != 0)
                Marshal.Release(profiles);
            if (statics != 0)
                Marshal.Release(statics);
            if (className != 0)
                WindowsDeleteString(className);
        }
    }

    /// <summary>A connection profile's connected SSID: empty for one that isn't Wi-Fi or isn't connected.</summary>
    private static string? Ssid(nint profile)
    {
        if (Marshal.QueryInterface(profile, in ConnectionProfile2Id, out var profile2) < 0)
            return null;
        try
        {
            byte isWlan;
            if (((delegate* unmanaged[Stdcall]<nint, byte*, int>)Slot(profile2, IsWlanConnectionProfileSlot))(profile2, &isWlan) < 0 || isWlan == 0)
                return null;
            if (Get(profile2, WlanConnectionProfileDetailsSlot, out var details) < 0 || details == 0)
                return null;
            try
            {
                if (Get(details, GetConnectedSsidSlot, out var hstring) < 0 || hstring == 0)
                    return null;
                try
                {
                    var buffer = WindowsGetStringRawBuffer(hstring, out var length);
                    return buffer is null ? null : new string(buffer, 0, (int)length);
                }
                finally
                {
                    WindowsDeleteString(hstring);
                }
            }
            finally
            {
                Marshal.Release(details);
            }
        }
        finally
        {
            Marshal.Release(profile2);
        }
    }

    private bool EnsureClient()
    {
        if (_client != 0 || _unavailable)
            return _client != 0;
        // Fails on PCs without Wi-Fi, where the WLAN AutoConfig service isn't installed or running.
        if (WlanOpenHandle(ClientVersion, 0, out _, out _client) != 0)
        {
            _client = 0;
            _unavailable = true;
        }
        return _client != 0;
    }

    private static void* Slot(nint instance, int slot) => (*(void***)instance)[slot];

    /// <summary>Calls a method whose only parameter is an out interface pointer (or HSTRING).</summary>
    private static int Get(nint instance, int slot, out nint result)
    {
        nint value = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(instance, slot))(instance, &value);
        result = hr >= 0 ? value : 0;
        return hr;
    }

    public void Dispose()
    {
        if (_client != 0)
            WlanCloseHandle(_client, 0);
        _client = 0;
    }

    [DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(uint clientVersion, nint reserved, out uint negotiatedVersion, out nint clientHandle);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(nint clientHandle, nint reserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanEnumInterfaces(nint clientHandle, nint reserved, out nint interfaceList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanQueryInterface(nint clientHandle, in Guid interfaceGuid, uint opCode, nint reserved, out uint dataSize, out nint data, out uint valueType);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(nint memory);

    [DllImport("combase.dll")]
    private static extern int RoInitialize(int initType);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(nint activatableClassId, in Guid iid, out nint factory);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, int length, out nint hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(nint hstring);

    [DllImport("combase.dll")]
    private static extern char* WindowsGetStringRawBuffer(nint hstring, out uint length);
}
