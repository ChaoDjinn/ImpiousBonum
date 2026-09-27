using System.Runtime.InteropServices;

namespace ImpiousBonum.Core.Native;

/// <summary>Minimal wrapper over the Performance Data Helper API for wildcard counters (e.g. <c>\GPU Engine(*)\Utilization Percentage</c>).</summary>
internal sealed partial class PdhQuery : IDisposable
{
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhFmtNoCap100 = 0x00008000;
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhCstatusValidData = 0x0;
    private const uint PdhCstatusNewData = 0x1;

    private nint _query;
    private readonly List<nint> _counters = [];

    public PdhQuery()
    {
        Check(PdhOpenQueryW(null, 0, out _query));
    }

    /// <summary>Adds a counter using its English path so it works on any display language. Returns a handle for <see cref="Read"/>.</summary>
    public int Add(string englishPath)
    {
        Check(PdhAddEnglishCounterW(_query, englishPath, 0, out var counter));
        _counters.Add(counter);
        return _counters.Count - 1;
    }

    public void Collect() => PdhCollectQueryData(_query);

    /// <summary>Reads every instance of a wildcard counter as (instance name, value) pairs.</summary>
    public unsafe List<(string Instance, double Value)> Read(int counterHandle)
    {
        var results = new List<(string, double)>();
        var counter = _counters[counterHandle];
        uint bufferSize = 0;

        var status = PdhGetFormattedCounterArrayW(counter, PdhFmtDouble | PdhFmtNoCap100, ref bufferSize, out _, 0);
        if (status != PdhMoreData || bufferSize == 0)
            return results;

        var buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            status = PdhGetFormattedCounterArrayW(counter, PdhFmtDouble | PdhFmtNoCap100, ref bufferSize, out var itemCount, buffer);
            if (status != 0)
                return results;

            var items = (CounterValueItem*)buffer;
            for (var i = 0; i < itemCount; i++)
            {
                var item = items[i];
                if (item.Status is not (PdhCstatusValidData or PdhCstatusNewData))
                    continue;
                results.Add((Marshal.PtrToStringUni(item.Name) ?? string.Empty, item.Value));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return results;
    }

    public void Dispose()
    {
        if (_query != 0)
        {
            PdhCloseQuery(_query);
            _query = 0;
        }
    }

    private static void Check(uint status)
    {
        if (status != 0)
            throw new InvalidOperationException($"PDH call failed with 0x{status:X8}.");
    }

    // PDH_FMT_COUNTERVALUE_ITEM_W: name pointer, then PDH_FMT_COUNTERVALUE { DWORD CStatus; union { double ... } } aligned to 8.
    [StructLayout(LayoutKind.Sequential)]
    private struct CounterValueItem
    {
        public nint Name;
        public uint Status;
        public double Value;
    }

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PdhOpenQueryW(string? dataSource, nint userData, out nint query);

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PdhAddEnglishCounterW(nint query, string fullCounterPath, nint userData, out nint counter);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhCollectQueryData(nint query);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint bufferSize, out uint itemCount, nint itemBuffer);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhCloseQuery(nint query);
}
