using System.Runtime.InteropServices;

namespace ImpiousBonum.Core.Native;

/// <summary>One wireless device's battery, as one read found it.</summary>
/// <param name="Name">The device's name, as Windows' Bluetooth settings show it, e.g. WH-1000XM4.</param>
/// <param name="Percent">How much charge it has left, from 0 to 100.</param>
public sealed record DeviceBattery(string Name, int Percent);

/// <summary>
/// The batteries of the wireless devices connected now, as Windows' Bluetooth settings show them, read without admin
/// rights and without contacting the devices.
/// <para>
/// Bluetooth devices: Windows keeps each one's latest level in a device property, <c>{104EA319-…} 2</c>. Headsets send
/// it over the hands-free profile, and Bluetooth LE mice, keyboards, controllers and earbuds have a Battery Service,
/// which Windows reads itself, so there's no connection of our own to open. Windows keeps the last level after a device
/// has gone, so a device counts only while Bluetooth hasn't marked it disconnected (DN_DEVICE_DISCONNECTED, from cfg.h:
/// switched off or out of range).
/// </para>
/// <para>
/// Xbox controllers on the Xbox Wireless Adapter or a cable, through <see cref="XboxControllerReader"/>. That's only
/// started once a controller like that (a device whose id has <c>IG_</c>) has been plugged in, so PCs without one never
/// load it.
/// </para>
/// <para>
/// Looking through every device for the property takes about 10 ms, so it's done again only when a device has been added
/// or removed, or every 10 minutes; reading the levels of the ones found takes microseconds. Not thread-safe: use one
/// reader from one loop.
/// </para>
/// </summary>
internal sealed unsafe class DeviceBatteryReader : IDisposable
{
    /// <summary>How often every device is looked through again when none has come or gone: for a level Windows has only just set, or a name changed in Bluetooth settings.</summary>
    private const long LookAgainMs = 10 * 60 * 1000;

    private static readonly PropertyKey BatteryLevel = new(new Guid("104ea319-6ee2-4701-bd47-8ddbf425bbe5"), 2);
    private static readonly PropertyKey FriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
    private static readonly PropertyKey DeviceDescription = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);
    private static readonly PropertyKey ContainerId = new(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);

    private const uint Success = 0;
    private const uint BufferSmall = 0x1A;
    private const uint FilterPresent = 0x100;
    private const uint DeviceDisconnected = 0x02000000;
    private const uint TypeByte = 0x03, TypeUInt16 = 0x05, TypeUInt32 = 0x07, TypeGuid = 0x0D, TypeString = 0x12;

    /// <summary>The device nodes with a level, each with its device's node in Bluetooth (which has the name and says whether it's connected), if found.</summary>
    private List<Source> _sources = [];
    private uint _listLength;
    private long _lookAgain;
    private bool _controllersSeen;
    private XboxControllerReader? _controllers;

    public IReadOnlyList<DeviceBattery> Read()
    {
        // The list of every device's id changes length when one comes or goes: a cheap sign to look through them again.
        var length = CM_Get_Device_ID_List_SizeW(out var size, null, FilterPresent) == Success ? size : 0;
        if (length != _listLength || Environment.TickCount64 >= _lookAgain)
            Find();

        var found = new List<DeviceBattery>();
        foreach (var source in _sources)
        {
            // Gone since it was found (unpaired, say): it's left out until the next look finds it gone.
            if (CM_Locate_DevNodeW(out var node, source.Node, 0) != Success)
                continue;
            if (!Connected(node) || (source.Device is { } device && !(CM_Locate_DevNodeW(out var deviceNode, device, 0) == Success && Connected(deviceNode))))
                continue;
            if (Level(node) is { } level)
                found.Add(new DeviceBattery(source.Name, level));
        }

        if (_controllersSeen)
        {
            _controllers ??= new XboxControllerReader();
            // A controller connected over Bluetooth is in both: list it once, going by its name.
            foreach (var controller in _controllers.Read())
                if (!found.Any(f => f.Name.Equals(controller.Name, StringComparison.OrdinalIgnoreCase)))
                    found.Add(controller);
        }
        return found;
    }

    /// <summary>Looks through every device present for a battery level, and for game controllers.</summary>
    private void Find()
    {
        _lookAgain = Environment.TickCount64 + LookAgainMs;
        var ids = PresentDevices(out _listLength);

        // Each Bluetooth device's own node (BTHENUM\DEV_… or BTHLE\DEV_…), by the container that groups all of its nodes.
        var devices = new Dictionary<Guid, (string Id, string? Name)>();
        var withLevel = new List<(string Id, Guid? Container, string? Name)>();
        foreach (var id in ids)
        {
            // XInput controllers (Xbox ones, and others like them) have IG_ in their ids, however they're connected.
            if (id.Contains("&IG_", StringComparison.OrdinalIgnoreCase))
                _controllersSeen = true;
            if (CM_Locate_DevNodeW(out var node, id, 0) != Success)
                continue;
            var container = GuidProperty(node, ContainerId);
            if (container is { } c && (id.StartsWith(@"BTHENUM\DEV_", StringComparison.OrdinalIgnoreCase) || id.StartsWith(@"BTHLE\DEV_", StringComparison.OrdinalIgnoreCase)))
                devices.TryAdd(c, (id, StringProperty(node, FriendlyName)));
            if (Level(node) is not null)
                withLevel.Add((id, container, StringProperty(node, FriendlyName) ?? StringProperty(node, DeviceDescription)));
        }

        // A headset can have its level on more than one of its nodes; it's one device. Its name is the one Bluetooth
        // settings show, its own node's, rather than a node's such as "Hands-Free AG".
        _sources = withLevel
            .DistinctBy(n => n.Container ?? Guid.NewGuid())
            .Select(n =>
            {
                var device = n.Container is { } c && devices.TryGetValue(c, out var d) ? d : default;
                var name = device.Name ?? n.Name;
                return new Source(n.Id, device.Id == n.Id ? null : device.Id, string.IsNullOrWhiteSpace(name) ? "Bluetooth device" : name.Trim());
            })
            .ToList();
    }

    private static List<string> PresentDevices(out uint length)
    {
        while (true)
        {
            if (CM_Get_Device_ID_List_SizeW(out length, null, FilterPresent) != Success || length == 0)
                return [];
            var buffer = new char[length];
            fixed (char* chars = buffer)
            {
                // CR_BUFFER_SMALL: a device came between the two calls; ask again.
                var result = CM_Get_Device_ID_ListW(null, chars, length, FilterPresent);
                if (result == BufferSmall)
                    continue;
                if (result != Success)
                    return [];
            }
            return [.. new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries)];
        }
    }

    /// <summary>Not marked disconnected by its driver.</summary>
    private static bool Connected(uint node) =>
        CM_Get_DevNode_Status(out var status, out _, node, 0) == Success && (status & DeviceDisconnected) == 0;

    private static int? Level(uint node)
    {
        uint value = 0;
        uint size = sizeof(uint);
        if (CM_Get_DevNode_PropertyW(node, in BatteryLevel, out var type, (byte*)&value, ref size, 0) != Success)
            return null;
        var level = type switch
        {
            TypeByte => value & 0xFF,
            TypeUInt16 => value & 0xFFFF,
            TypeUInt32 => value,
            _ => uint.MaxValue,
        };
        return level <= 100 ? (int)level : null;
    }

    private static Guid? GuidProperty(uint node, in PropertyKey key)
    {
        Guid value;
        uint size = (uint)sizeof(Guid);
        return CM_Get_DevNode_PropertyW(node, in key, out var type, (byte*)&value, ref size, 0) == Success && type == TypeGuid ? value : null;
    }

    private static string? StringProperty(uint node, in PropertyKey key)
    {
        const int Chars = 256;
        var buffer = stackalloc char[Chars];
        uint size = Chars * sizeof(char);
        if (CM_Get_DevNode_PropertyW(node, in key, out var type, (byte*)buffer, ref size, 0) != Success || type != TypeString)
            return null;
        var text = new string(buffer, 0, Math.Max(0, (int)(size / sizeof(char)) - 1)).TrimEnd('\0');
        return text.Length > 0 ? text : null;
    }

    public void Dispose()
    {
        _controllers?.Dispose();
        _controllers = null;
    }

    private sealed record Source(string Node, string? Device, string Name);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PropertyKey(Guid category, uint id)
    {
        public readonly Guid Category = category;
        public readonly uint Id = id;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_ID_List_SizeW(out uint length, string? filter, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_ID_ListW(string? filter, char* buffer, uint length, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Locate_DevNodeW(out uint node, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint node, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_DevNode_PropertyW(uint node, in PropertyKey key, out uint type, byte* buffer, ref uint size, uint flags);
}

/// <summary>
/// Xbox controllers' batteries, through Windows.Gaming.Input: those on the Xbox Wireless Adapter or a cable, which
/// Bluetooth doesn't know about, and those on Bluetooth too. Like the media reader, it talks to the WinRT ABI directly
/// rather than take on the Windows SDK projection (ids and slots from windows.gaming.input.h and windows.devices.power.h;
/// slots 0–5 are IUnknown's and IInspectable's). Windows finds controllers in the background once first asked, and keeps
/// track of them while the reader holds on to it, so a controller already plugged in shows from the second read.
/// Not thread-safe: use one reader from one loop.
/// </summary>
internal sealed unsafe class XboxControllerReader : IDisposable
{
    private const string GamepadClass = "Windows.Gaming.Input.Gamepad";
    private const string RawGameControllerClass = "Windows.Gaming.Input.RawGameController";

    private static readonly Guid GamepadStaticsId = new("8bbce529-d49c-39e9-9560-e47dde96b7c8");
    private static readonly Guid RawGameControllerStaticsId = new("eb8d0792-e95a-4b19-afc7-0a59f8bf759e");
    private static readonly Guid RawGameController2Id = new("43c0c035-bb73-4756-a787-3ed6bea617bd");
    private static readonly Guid BatteryInfoId = new("dcecc681-3963-4da6-955d-553f3b6f6161");

    /// <summary>What a controller's called when Windows doesn't say.</summary>
    public const string DefaultName = "Xbox controller";

    // IGamepadStatics
    private const int GamepadsSlot = 10;

    // IRawGameControllerStatics
    private const int FromGameControllerSlot = 11;

    // IRawGameController2
    private const int DisplayNameSlot = 8;

    // IVectorView<Gamepad>
    private const int GetAtSlot = 6;
    private const int SizeSlot = 7;

    // IGameControllerBatteryInfo
    private const int TryGetBatteryReportSlot = 6;

    // IBatteryReport; the capacities are IReference<int>, whose value is its slot 6.
    private const int FullChargeSlot = 8;
    private const int RemainingSlot = 9;
    private const int StatusSlot = 10;
    private const int ValueSlot = 6;
    private const int StatusNotPresent = 0;

    private const int RoInitMultithreaded = 1;

    [ThreadStatic]
    private static bool t_apartment;

    private nint _gamepads;
    private nint _rawControllers;
    private bool _unavailable;

    public IReadOnlyList<DeviceBattery> Read()
    {
        if (!EnsureStatics())
            return [];
        if (Get(_gamepads, GamepadsSlot, out var list) < 0 || list == 0)
            return [];
        var found = new List<DeviceBattery>();
        try
        {
            uint count;
            if (((delegate* unmanaged[Stdcall]<nint, uint*, int>)Slot(list, SizeSlot))(list, &count) < 0)
                return [];
            for (uint i = 0; i < count; i++)
            {
                nint gamepad = 0;
                if (((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Slot(list, GetAtSlot))(list, i, &gamepad) < 0 || gamepad == 0)
                    continue;
                try
                {
                    if (Percent(gamepad) is { } percent)
                        found.Add(new DeviceBattery(Name(gamepad) ?? DefaultName, percent));
                }
                finally
                {
                    Marshal.Release(gamepad);
                }
            }
        }
        finally
        {
            Marshal.Release(list);
        }
        return found;
    }

    /// <summary>How much charge it has left, or null for a controller without a battery (a wired one, say).</summary>
    private static int? Percent(nint gamepad)
    {
        if (Marshal.QueryInterface(gamepad, in BatteryInfoId, out var info) < 0)
            return null;
        try
        {
            if (Get(info, TryGetBatteryReportSlot, out var report) < 0 || report == 0)
                return null;
            try
            {
                int status;
                if (((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(report, StatusSlot))(report, &status) < 0 || status == StatusNotPresent)
                    return null;
                var full = Int(report, FullChargeSlot);
                var remaining = Int(report, RemainingSlot);
                return full > 0 && remaining is { } left ? Math.Clamp((int)Math.Round(100.0 * left / full.Value), 0, 100) : null;
            }
            finally
            {
                Marshal.Release(report);
            }
        }
        finally
        {
            Marshal.Release(info);
        }
    }

    /// <summary>The name Windows gives the controller, such as Xbox Wireless Controller.</summary>
    private string? Name(nint gamepad)
    {
        if (_rawControllers == 0)
            return null;
        nint raw = 0;
        if (((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Slot(_rawControllers, FromGameControllerSlot))(_rawControllers, gamepad, &raw) < 0 || raw == 0)
            return null;
        try
        {
            if (Marshal.QueryInterface(raw, in RawGameController2Id, out var raw2) < 0)
                return null;
            try
            {
                if (Get(raw2, DisplayNameSlot, out var hstring) < 0 || hstring == 0)
                    return null;
                try
                {
                    var buffer = WindowsGetStringRawBuffer(hstring, out var length);
                    var name = buffer is null ? null : new string(buffer, 0, (int)length).Trim();
                    return string.IsNullOrEmpty(name) ? null : name;
                }
                finally
                {
                    WindowsDeleteString(hstring);
                }
            }
            finally
            {
                Marshal.Release(raw2);
            }
        }
        finally
        {
            Marshal.Release(raw);
        }
    }

    private static int? Int(nint instance, int slot)
    {
        if (Get(instance, slot, out var reference) < 0 || reference == 0)
            return null;
        try
        {
            int value;
            return ((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(reference, ValueSlot))(reference, &value) >= 0 ? value : null;
        }
        finally
        {
            Marshal.Release(reference);
        }
    }

    private bool EnsureStatics()
    {
        // WinRT needs COM on the calling thread; the sampler's loop moves between thread-pool threads, which are MTA.
        if (!t_apartment)
        {
            RoInitialize(RoInitMultithreaded);
            t_apartment = true;
        }

        if (_gamepads != 0 || _unavailable)
            return _gamepads != 0;
        _gamepads = Factory(GamepadClass, GamepadStaticsId);
        // Only for the names, so carry on without it.
        _rawControllers = Factory(RawGameControllerClass, RawGameControllerStaticsId);
        // Windows without Windows.Gaming.Input (some server editions): don't keep asking.
        _unavailable = _gamepads == 0;
        return _gamepads != 0;
    }

    private static nint Factory(string className, in Guid id)
    {
        nint name = 0;
        try
        {
            return WindowsCreateString(className, className.Length, out name) >= 0 && RoGetActivationFactory(name, in id, out var factory) >= 0 ? factory : 0;
        }
        finally
        {
            if (name != 0)
                WindowsDeleteString(name);
        }
    }

    public void Dispose()
    {
        if (_gamepads != 0)
            Marshal.Release(_gamepads);
        if (_rawControllers != 0)
            Marshal.Release(_rawControllers);
        _gamepads = 0;
        _rawControllers = 0;
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
