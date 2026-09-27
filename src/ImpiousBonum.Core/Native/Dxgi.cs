using System.Runtime.InteropServices;

namespace ImpiousBonum.Core.Native;

/// <summary>Enumerates graphics adapters through DXGI to get names, VRAM size and the LUID that performance counters use.</summary>
internal static class Dxgi
{
    private const uint AdapterFlagSoftware = 2;

    public sealed record Adapter(string Name, ulong DedicatedVideoMemory, string Luid);

    public static IReadOnlyList<Adapter> EnumerateAdapters()
    {
        var adapters = new List<Adapter>();
        var iid = typeof(IDXGIFactory1).GUID;
        if (CreateDXGIFactory1(ref iid, out var factory) != 0 || factory is null)
            return adapters;

        try
        {
            for (uint i = 0; factory.EnumAdapters1(i, out var adapter) == 0; i++)
            {
                try
                {
                    if (adapter.GetDesc1(out var desc) != 0 || (desc.Flags & AdapterFlagSoftware) != 0)
                        continue;
                    // Matches the "luid_0x00000000_0x0000D1B5" form used in GPU performance counter instance names.
                    var luid = $"0x{desc.LuidHighPart:X8}_0x{desc.LuidLowPart:X8}";
                    adapters.Add(new Adapter(desc.Description.Trim(), (ulong)desc.DedicatedVideoMemory, luid));
                }
                finally
                {
                    Marshal.ReleaseComObject(adapter);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }

        return adapters;
    }

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IDXGIFactory1? factory);

    // Vtable order matters: IUnknown is implied, then IDXGIObject, IDXGIFactory, IDXGIFactory1.
    // Methods we never call are declared parameterless purely to reserve their slots.
    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        void EnumAdapters();
        void MakeWindowAssociation();
        void GetWindowAssociation();
        void CreateSwapChain();
        void CreateSoftwareAdapter();

        [PreserveSig]
        int EnumAdapters1(uint index, out IDXGIAdapter1 adapter);
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        void EnumOutputs();
        void GetDesc();
        void CheckInterfaceSupport();

        [PreserveSig]
        int GetDesc1(out AdapterDesc1 desc);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint LuidLowPart;
        public int LuidHighPart;
        public uint Flags;
    }
}
