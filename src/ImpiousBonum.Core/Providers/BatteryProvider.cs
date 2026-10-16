using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Native;

namespace ImpiousBonum.Core.Providers;

/// <summary>A connected device's battery, as the batteries widget shows it.</summary>
/// <param name="Slug">The middle of its readings' ids: <c>headset</c> for <c>battery.headset</c>.</param>
/// <param name="Name">The device's name, e.g. WH-1000XM4.</param>
/// <param name="Percent">How much charge it has left, from 0 to 100.</param>
public sealed record BatteryLevel(string Slug, string Name, int Percent);

/// <summary>
/// The batteries of wireless devices, as Windows' Bluetooth settings show them: Bluetooth headsets, mice, keyboards and
/// game controllers, and Xbox controllers (see <see cref="DeviceBatteryReader"/>), every minute. Each device has its
/// level and its name as readings (see <see cref="LevelId"/>) from when it's first seen connected; the level is empty
/// while it's away. <see cref="Low"/> names the one with the least charge once it's under <see cref="LowBelow"/>, and
/// the connected ones (an <c>IReadOnlyList&lt;BatteryLevel&gt;</c>, by name) are an attachment for the batteries
/// widget. Nothing appears on PCs without such devices.
/// </summary>
/// <param name="read">The connected devices' batteries; Windows unless a test swaps it.</param>
public sealed class BatteryProvider(Func<IReadOnlyList<DeviceBattery>>? read = null) : IMetricProvider
{
    public const string Prefix = "battery.";
    public const string Low = "battery.low";

    /// <summary>The connected devices' batteries, an <c>IReadOnlyList&lt;BatteryLevel&gt;</c> by name, as a <see cref="MetricStore"/> attachment.</summary>
    public const string Devices = "battery.devices";

    /// <summary>Under this, a device's battery is low: <see cref="Low"/> names it, and the batteries widget turns it the critical colour.</summary>
    public const int LowBelow = 20;

    /// <summary>The last part of a device's name reading's id: <c>battery.headset.name</c>.</summary>
    public const string NameReading = "name";

    private const string Category = "Device batteries";

    private readonly DeviceBatteryReader? _reader = read is null ? new DeviceBatteryReader() : null;

    // Each device's id part by its name, from when it was first seen: it keeps it while it's away.
    private readonly Dictionary<string, string> _slugs = new(StringComparer.OrdinalIgnoreCase);

    public TimeSpan Interval => TimeSpan.FromMinutes(1);

    public void Initialize(MetricStore store)
    {
    }

    public ValueTask SampleAsync(MetricStore store, CancellationToken cancellationToken)
    {
        Publish(store, read is null ? _reader!.Read() : read());
        return ValueTask.CompletedTask;
    }

    /// <summary>The id of a device's level (%): <c>battery.headset</c> for <paramref name="slug"/> <c>headset</c>.</summary>
    public static string LevelId(string slug) => Prefix + slug;

    /// <summary>The id of a device's name: <c>battery.headset.name</c>.</summary>
    public static string NameId(string slug) => $"{Prefix}{slug}.{NameReading}";

    /// <summary>
    /// Writes one reading of the connected devices, registering each device's readings the first time it's seen. Every
    /// device seen before and missing now has an empty level.
    /// </summary>
    internal void Publish(MetricStore store, IReadOnlyList<DeviceBattery> devices)
    {
        var levels = new List<BatteryLevel>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var named = devices.Select(d => d with { Name = d.Name.Trim() is { Length: > 0 } trimmed ? trimmed : "Device" });
        foreach (var device in named.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            // Two devices with the same name, such as a pair of controllers: the second is "Controller 2".
            var name = device.Name;
            for (var n = 2; !names.Add(name); n++)
                name = $"{device.Name} {n}";
            levels.Add(new BatteryLevel(SlugFor(store, name), name, Math.Clamp(device.Percent, 0, 100)));
        }

        foreach (var slug in _slugs.Values)
            store.Set(LevelId(slug), levels.FirstOrDefault(l => l.Slug == slug)?.Percent);
        store.SetText(Low, Lowest(levels));
        store.SetAttachment(Devices, levels.Count > 0 ? levels : null);
    }

    /// <summary>The name of the device with the least charge, if that's under <see cref="LowBelow"/>; null while none is.</summary>
    public static string? Lowest(IReadOnlyList<BatteryLevel> levels) =>
        levels.Where(l => l.Percent < LowBelow).MinBy(l => l.Percent)?.Name;

    /// <summary>The names of every device seen since the app started, connected or not, in order: what the batteries widget can pick from.</summary>
    public static IReadOnlyList<string> Names(MetricStore store) =>
        [.. store.Definitions
            .Where(d => d.Category == Category && d.Unit == MetricUnit.Text && d.Id != Low)
            .Select(d => store.TryGet(d.Id, out var sample) ? sample.Text : null)
            .OfType<string>()
            .Order(StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>
    /// A device's id part, from its name: "WH-1000XM4" is <c>wh-1000xm4</c>, "Xbox Wireless Controller"
    /// <c>xbox-wireless-controller</c>. One that's taken (by <see cref="Low"/>, or a name that differs only in its
    /// punctuation) gets a number. Its readings are registered the first time.
    /// </summary>
    private string SlugFor(MetricStore store, string name)
    {
        if (_slugs.TryGetValue(name, out var slug))
            return slug;
        var part = MetricIdPart.From(name) is { Length: > 0 } p ? p : "device";
        slug = part;
        for (var n = 2; LevelId(slug) == Low || _slugs.ContainsValue(slug); n++)
            slug = $"{part}-{n}";

        if (_slugs.Count == 0)
            store.Register(new MetricDefinition(Low, $"Device batteries: the one running low (under {LowBelow} %)", Category, MetricUnit.Text));
        _slugs[name] = slug;
        store.Register(new MetricDefinition(LevelId(slug), $"{name}: battery", Category, MetricUnit.Percent, 100, Format: "0"));
        store.Register(new MetricDefinition(NameId(slug), $"{name}: name", Category, MetricUnit.Text));
        store.SetText(NameId(slug), name);
        return slug;
    }

    public void Dispose() => _reader?.Dispose();
}
