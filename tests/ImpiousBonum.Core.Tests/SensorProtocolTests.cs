using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Sensors;

namespace ImpiousBonum.Core.Tests;

public sealed class SensorProtocolTests
{
    [Fact]
    public void Catalog_round_trips_with_units_as_strings()
    {
        var message = new SensorMessage
        {
            Type = SensorMessage.CatalogType,
            Sensors = [new SensorInfo("gpu.temp", "GPU temperature", "Sensors", MetricUnit.Celsius, 110)],
        };

        var line = SensorProtocol.Serialize(message);
        var parsed = SensorProtocol.Deserialize(line);

        Assert.Contains("\"Celsius\"", line);
        Assert.DoesNotContain('\n', line);
        Assert.Equal(message.Sensors, parsed!.Sensors);
    }

    [Fact]
    public void Values_keep_nulls_for_missing_readings()
    {
        var parsed = SensorProtocol.Deserialize(SensorProtocol.Serialize(new SensorMessage
        {
            Type = SensorMessage.ValuesType,
            Values = new Dictionary<string, double?> { ["cpu.temp"] = 60.25, ["fps"] = null },
        }));

        Assert.Equal(60.25, parsed!.Values!["cpu.temp"]);
        Assert.Null(parsed.Values["fps"]);
    }

    [Fact]
    public void Garbage_is_ignored_rather_than_thrown() =>
        Assert.Null(SensorProtocol.Deserialize("{not json"));
}
