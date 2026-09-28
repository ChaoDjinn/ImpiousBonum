using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.Core.Tests;

public sealed class ThresholdRuleTests
{
    private static readonly ThresholdRule[] HotCpu =
    [
        new(null, Above: 80, Below: null, "warning"),
        new(null, Above: 90, Below: null, "critical"),
    ];

    private static string? Pick(IReadOnlyList<ThresholdRule> rules, double? value, string? ownMetric = "cpu.temp") =>
        ThresholdRule.Select(rules, ownMetric, id => id == "cpu.temp" ? value : null)?.Color;

    [Theory]
    [InlineData(45.0, null)]
    [InlineData(80.0, null)]
    [InlineData(85.0, "warning")]
    [InlineData(95.0, "critical")]
    public void Above_rules_escalate_and_cool_back_down(double value, string? expected) =>
        Assert.Equal(expected, Pick(HotCpu, value));

    [Fact]
    public void Below_matches_low_values()
    {
        ThresholdRule[] lowSpace = [new(null, null, Below: 20, "warning"), new(null, null, Below: 5, "critical")];
        Assert.Null(Pick(lowSpace, 50));
        Assert.Equal("warning", Pick(lowSpace, 10));
        Assert.Equal("critical", Pick(lowSpace, 2));
    }

    [Fact]
    public void Above_and_below_together_make_a_band()
    {
        ThresholdRule[] band = [new(null, Above: 10, Below: 20, "accent")];
        Assert.Null(Pick(band, 5));
        Assert.Equal("accent", Pick(band, 15));
        Assert.Null(Pick(band, 25));
    }

    [Fact]
    public void Later_rules_win_when_several_match()
    {
        ThresholdRule[] reversed = [HotCpu[1], HotCpu[0]];
        Assert.Equal("warning", Pick(reversed, 95));
    }

    [Fact]
    public void Missing_readings_keep_the_normal_colour()
    {
        Assert.Null(Pick(HotCpu, null));
        Assert.Null(Pick(HotCpu, double.NaN));
        Assert.Null(Pick(HotCpu, 95, ownMetric: null));
    }

    [Fact]
    public void A_rule_can_watch_another_metric()
    {
        ThresholdRule[] rules = [new("cpu.temp", Above: 90, Below: null, "critical")];
        Assert.Equal("critical", Pick(rules, 95, ownMetric: "cpu.load"));
    }

    [Fact]
    public void A_rule_without_bounds_never_matches() =>
        Assert.Null(Pick([new(null, null, null, "critical")], 50));

    [Fact]
    public void Reads_values_from_the_store()
    {
        var store = new MetricStore();
        store.Register(new MetricDefinition("cpu.temp", "Temp", "CPU", MetricUnit.Celsius));
        Assert.Null(ThresholdRule.Select(HotCpu, "cpu.temp", store));
        store.Set("cpu.temp", 92);
        Assert.Equal("critical", ThresholdRule.Select(HotCpu, "cpu.temp", store)?.Color);
        Assert.Null(ThresholdRule.Select(HotCpu, "gpu.temp", store));
    }
}
