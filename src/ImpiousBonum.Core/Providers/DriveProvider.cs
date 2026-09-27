using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Providers;

/// <summary>
/// Free/used space for every ready fixed or removable drive, as <c>disk.C.free</c>, <c>disk.C.total</c> and so on.
/// Drives that appear or disappear (USB sticks, card readers) are registered and removed as they come and go.
/// </summary>
public sealed class DriveProvider : IMetricProvider
{
    /// <summary>Lists the letters of drives currently reported, e.g. "C,D,E". Widgets use it to build their rows.</summary>
    public const string Letters = "disk.letters";

    private HashSet<string> _known = [];

    public TimeSpan Interval => TimeSpan.FromSeconds(10);

    public static string Free(string letter) => $"disk.{letter}.free";
    public static string Used(string letter) => $"disk.{letter}.used";
    public static string Total(string letter) => $"disk.{letter}.total";
    public static string UsedPercent(string letter) => $"disk.{letter}.usedPct";
    public static string Label(string letter) => $"disk.{letter}.label";

    public void Initialize(MetricStore store)
    {
        store.Register(new MetricDefinition(Letters, "Drive letters", "Disk", MetricUnit.Text));
    }

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
                continue;

            try
            {
                // IsReady is false for empty card readers; skip them rather than showing "0 free / 0".
                if (!drive.IsReady)
                    continue;

                var letter = drive.Name[..1].ToUpperInvariant();
                seen.Add(letter);

                if (!_known.Contains(letter))
                {
                    var category = $"Disk {letter}:";
                    store.Register(new MetricDefinition(Free(letter), $"{letter}: free", category, MetricUnit.Bytes, drive.TotalSize));
                    store.Register(new MetricDefinition(Used(letter), $"{letter}: used", category, MetricUnit.Bytes, drive.TotalSize));
                    store.Register(new MetricDefinition(Total(letter), $"{letter}: total", category, MetricUnit.Bytes));
                    store.Register(new MetricDefinition(UsedPercent(letter), $"{letter}: used %", category, MetricUnit.Percent, 100));
                    store.Register(new MetricDefinition(Label(letter), $"{letter}: label", category, MetricUnit.Text));
                }

                store.Set(Free(letter), drive.AvailableFreeSpace);
                store.Set(Used(letter), drive.TotalSize - drive.TotalFreeSpace);
                store.Set(Total(letter), drive.TotalSize);
                store.Set(UsedPercent(letter), drive.TotalSize == 0 ? null : 100.0 * (drive.TotalSize - drive.TotalFreeSpace) / drive.TotalSize);
                store.SetText(Label(letter), drive.VolumeLabel);
            }
            catch (IOException)
            {
                // Drive vanished between enumeration and query.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        foreach (var gone in _known.Except(seen))
        {
            store.Unregister(Free(gone));
            store.Unregister(Used(gone));
            store.Unregister(Total(gone));
            store.Unregister(UsedPercent(gone));
            store.Unregister(Label(gone));
        }

        _known = seen;
        store.SetText(Letters, string.Join(',', seen.Order()));
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
    }
}
