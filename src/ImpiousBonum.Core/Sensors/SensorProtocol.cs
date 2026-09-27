using System.Text.Json;
using System.Text.Json.Serialization;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Sensors;

/// <summary>
/// Wire format between the elevated sensor host and the dashboard: newline-delimited JSON over a one-way named pipe (host → dashboard).
/// On connect the host sends <c>status</c> then <c>catalog</c>; after that <c>values</c> once a second,
/// plus a fresh <c>catalog</c> whenever hardware appears or disappears.
/// </summary>
public static class SensorProtocol
{
    /// <summary>Bump the suffix on breaking changes so old dashboards and hosts simply fail to connect rather than misread data.</summary>
    public const string PipeName = "ImpiousBonum.Sensors.v1";

    public const string ServiceName = "ImpiousBonumSensors";

    public static string Serialize(SensorMessage message) => JsonSerializer.Serialize(message, SensorJsonContext.Default.SensorMessage);

    public static SensorMessage? Deserialize(string line)
    {
        try
        {
            return JsonSerializer.Deserialize(line, SensorJsonContext.Default.SensorMessage);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record SensorMessage
{
    public const string StatusType = "status";
    public const string CatalogType = "catalog";
    public const string ValuesType = "values";

    public required string Type { get; init; }

    /// <summary>For <c>status</c>: a short human-readable line such as "Sensors running" or "PawnIO driver not installed".</summary>
    public string? Status { get; init; }

    /// <summary>For <c>status</c>: whether low-level CPU/motherboard sensors are readable (elevated with the PawnIO driver present).</summary>
    public bool? LowLevelAccess { get; init; }

    public IReadOnlyList<SensorInfo>? Sensors { get; init; }

    public IReadOnlyDictionary<string, double?>? Values { get; init; }

    /// <summary>
    /// For <c>values</c>: apps currently presenting frames and their rates. Null when frame counting is unavailable.
    /// The dashboard picks the foreground app from this list, since only it can see the user's desktop.
    /// </summary>
    public IReadOnlyList<PresenterInfo>? Presenters { get; init; }
}

public sealed record PresenterInfo(int ProcessId, string Name, double Fps)
{
    /// <summary>
    /// The presenter for the foreground app: the same process, or else the busiest process with the same exe name
    /// (Chromium/Electron apps present from a separate GPU process that shares the exe).
    /// </summary>
    public static PresenterInfo? ForForeground(IEnumerable<PresenterInfo> presenters, int processId, string? processName)
    {
        var list = presenters as IReadOnlyList<PresenterInfo> ?? presenters.ToList();
        return list.FirstOrDefault(p => p.ProcessId == processId)
            ?? (processName is null ? null : list.Where(p => p.Name.Equals(processName, StringComparison.OrdinalIgnoreCase)).MaxBy(p => p.Fps));
    }
}

public sealed record SensorInfo(string Id, string Name, string Category, MetricUnit Unit, double? Max = null)
{
    public MetricDefinition ToDefinition() => new(Id, Name, Category, Unit, Max);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(SensorMessage))]
internal sealed partial class SensorJsonContext : JsonSerializerContext;
